# 統計情報設計ドキュメント

ゲームDLL（`sts2.dll`）のリフレクションによる実調査に基づく。
**表示設計を先に決め、そこから必要なデータを逆算する。**

---

## 1. Webダッシュボードの表示設計

### ページ構成

```
/s/{sessionId}
├── ヘッダー: プレイヤー名一覧・現在の階層
├── 戦闘セレクタ: [戦闘1] [戦闘2] [戦闘3] ...
└── 選択した戦闘の詳細
    ├── A. ダメージ貢献（誰がどれだけ与えたか）
    ├── B. ターン推移グラフ
    ├── C. カード別ダメージ内訳（プレイヤーごと）
    ├── D. 効率指標
    └── E. その他統計サマリー
```

---

### A. ダメージ貢献（棒グラフ）

```
戦闘2 - ダメージ貢献
┌──────────────────────────────────────────┐
│ PlayerA ████████████████████  320 (64%)  │
│ PlayerB ██████████           180 (36%)   │
└──────────────────────────────────────────┘
```

必要なデータ: プレイヤーごとの `damage_dealt` 合計（戦闘単位）

---

### B. ターン推移グラフ（折れ線グラフ）

```
ターンごとのダメージ推移
100 │     A
 80 │   A   A
 60 │ A       A    B
 40 │   B       B
 20 │
  0 └─────────────────
    T1  T2  T3  T4  T5
```

必要なデータ: プレイヤーごとの `damage_dealt`（ターン単位）

---

### C. カード別ダメージ内訳（テーブル）

```
PlayerA のカード使用状況
┌────────────────┬──────┬────────┬──────────────┐
│ カード名       │ 使用 │ 総ダメ │ 1回あたり    │
├────────────────┼──────┼────────┼──────────────┤
│ Strike+        │  4回 │    80  │   20.0       │
│ Bash           │  2回 │    36  │   18.0       │
│ Sword Boomerang│  3回 │    45  │   15.0       │
│ Defend         │  3回 │     0  │    —         │
└────────────────┴──────┴────────┴──────────────┘
```

必要なデータ: カードごとに `play_count` + `damage_dealt`（プレイヤー・戦闘単位）

> ダメージ0のカード（スキル・パワー）も表示する（プレイ頻度の把握のため）

---

### D. 効率指標（数値カード）

```
┌─────────────┐ ┌─��───────────┐ ┌─────────────┐
│  エナジー効率 │ │ カード効率  │ │  被ダメ     │
│  80 dmg/E   │ │  32 dmg/枚  │ │   45 受     │
└─────────────┘ └─────────────┘ └─────────────┘
```

必要なデータ: `damage_dealt` ÷ `energy_used`、`damage_dealt` ÷ `cards_played`、`damage_received`（戦闘単位）

---

### E. その他統計サマリー（テーブル）

```
┌────────────────┬──────────┬──────────┐
│                │ PlayerA  │ PlayerB  │
├─���──────────────┼──────────┼──────────┤
│ 与ダメージ     │   320    │   180    │
│ 被ダメージ     │    45    │    72    │
│ 獲得ブロック   │   210    │   150    │
│ カード使用枚数 │    28    │    22    │
│ ドロー枚数     │    40    │    35    │
│ エナジー消費   │    18    │    15    │
│ ポーション使用 │     1    │     0    │
└────────────────┴──────────┴──────────┘
```

---

## 2. 必要なデータ（表示設計からの逆算）

### ターンレベルのデータ（B に使用）

| フィールド | 型 | 用途 |
|-----------|-----|------|
| `damage_dealt` | int | ターン推移グラフ |
| `damage_received` | int | 参考表示 |
| `block_gained` | int | 参考表示 |
| `cards_played` | int | 効率計算の分母 |
| `energy_used` | int | 効率計算の分母 |
| `cards_drawn` | int | サマリー |

### 戦闘レベルのデータ（A・D・E に使用）

ターンデータの累計に加えて:

| フィールド | 型 | 用途 |
|-----------|-----|------|
| `potions_used` | int | サマリー |

### カードレベルのデータ（C に使用）

カードは**戦闘単位で集計**する（ターン単位は不要）。

| フィールド | 型 | 用途 |
|-----------|-----|------|
| `card_id` | string | カード識別（`ModelId.Entry`） |
| `card_name` | string | 表示用 |
| `card_type` | string | "Attack" / "Skill" / "Power" |
| `play_count` | int | 使用回数 |
| `damage_dealt` | int | そのカードが与えた総ダメージ |

---

## 3. 収集するイベントとHook

### イベント①: カードプレイ（プレイ記録）

**Hook**: `AfterCardPlayed(ICombatState, PlayerChoiceContext, CardPlay cardPlay)`

```csharp
cardPlay.Card.Owner     // Player（直接取得可能）
cardPlay.Card.Id.Entry  // カードID（string）
cardPlay.Card.Title     // カード名
cardPlay.Card.Type      // CardType: Attack / Skill / Power
```

記録内容:
- プレイヤーの `cards_played++`
- カード別の `play_count++`（damage_dealtは0で初期化）

### イベント②: ダメージ発生（カードへのダメージ帰属）

**Hook**: `AfterDamageGiven(... Creature dealer, DamageResult results, ..., CardModel cardSource)`

`cardSource` が非nullのとき → そのカードの `damage_dealt` に加算。
`cardSource` がnullのとき（毒・Doom等）→ `"(indirect)"` キーに集計。

```csharp
string cardKey = cardSource?.Id.Entry ?? "(indirect)";
```

### イベント③: 被ダメージ

**Hook**: `AfterDamageReceived(... Creature target, DamageResult result, ..., Creature dealer, ...)`

`target` がプレイヤーのCreatureと一致する場合のみ記録。

### イベント④: ブロック獲得

**Hook**: `AfterBlockGained(ICombatState, Creature creature, Decimal amount, ValueProp props, CardModel cardSource)`

`creature` がプレイヤーのCreatureと一致する場合のみ記録。

### イベント⑤: エナジー消費

**Hook**: `AfterEnergySpent(ICombatState, CardModel card, Int32 amount)`

`card.Owner` でプレイヤーを直接取得。

### イベント⑥: カードドロー

**Hook**: `AfterCardDrawn(ICombatState, PlayerChoiceContext, CardModel card, Boolean fromHandDraw)`

`card.Owner` でプレイヤーを取得し `cards_drawn++`。

### イベント⑦: ポーション使用

**Hook**: `AfterPotionUsed(IRunState, ICombatState, PotionModel potion, Creature target)`

`PlayerChoiceContext` が使えないため、`target` がプレイヤーのCreatureと一致するプレイヤーを特定。

---

## 4. データ構造（実装向け）

```csharp
// ターンスナップショット（折れ線グラフ用）
record TurnSnapshot(
    int CombatIndex,
    int TurnNumber,
    DateTime Timestamp,
    Dictionary<string, PlayerTurnStats> StatsByPlayer  // key: playerId
);

record PlayerTurnStats(
    string PlayerId,
    string PlayerName,
    int DamageDealt,
    int DamageReceived,
    int BlockGained,
    int CardsPlayed,
    int CardsDrawn,
    int EnergyUsed
);

// 戦闘サマリー（棒グラフ・テーブル・カード内訳用）
record CombatSummary(
    int CombatIndex,
    int TotalTurns,
    DateTime Timestamp,
    Dictionary<string, PlayerCombatStats> StatsByPlayer,
    List<CardCombatStats> CardStats
);

record PlayerCombatStats(
    string PlayerId,
    string PlayerName,
    int DamageDealt,
    int DamageReceived,
    int BlockGained,
    int CardsPlayed,
    int CardsDrawn,
    int EnergyUsed,
    int PotionsUsed
);

// カード別集計（戦闘単位）
record CardCombatStats(
    string PlayerId,
    string PlayerName,
    string CardId,
    string CardName,
    string CardType,   // "Attack" / "Skill" / "Power" / "(indirect)"
    int PlayCount,
    int DamageDealt
);
```

---

## 5. 送信するJSON（バックエンドAPI向け）

### `POST /sessions/{id}/turns`（ターン終了時）

```json
{
  "combat_index": 2,
  "turn_number": 3,
  "timestamp": "2026-05-05T00:00:00Z",
  "stats": {
    "76561199204788207": {
      "player_name": "Ironclad",
      "damage_dealt": 45,
      "damage_received": 12,
      "block_gained": 20,
      "cards_played": 5,
      "cards_drawn": 5,
      "energy_used": 3
    }
  }
}
```

### `POST /sessions/{id}/combat_end`（戦闘終了時）

```json
{
  "combat_index": 2,
  "total_turns": 5,
  "timestamp": "2026-05-05T00:00:00Z",
  "stats": {
    "76561199204788207": {
      "player_name": "Ironclad",
      "damage_dealt": 180,
      "damage_received": 45,
      "block_gained": 110,
      "cards_played": 24,
      "cards_drawn": 38,
      "energy_used": 15,
      "potions_used": 1
    }
  },
  "card_stats": [
    {
      "player_id": "76561199204788207",
      "player_name": "Ironclad",
      "card_id": "Strike_R+1",
      "card_name": "Strike+",
      "card_type": "Attack",
      "play_count": 4,
      "damage_dealt": 80
    },
    {
      "player_id": "76561199204788207",
      "player_name": "Ironclad",
      "card_id": "(indirect)",
      "card_name": "(間接ダメージ)",
      "card_type": "(indirect)",
      "play_count": 0,
      "damage_dealt": 12
    }
  ]
}
```

---

## 6. 実装スコープ

### Phase 1.5（mod拡充・今回実装）

- [x] `damage_dealt`（実装済み）
- [ ] `damage_received` — `AfterDamageReceived`
- [ ] `block_gained` — `AfterBlockGained`
- [ ] `cards_played` — `AfterCardPlayed`
- [ ] `cards_drawn` — `AfterCardDrawn`
- [ ] `energy_used` — `AfterEnergySpent`
- [ ] カード別 `play_count` + `damage_dealt`

### Phase 2（バックエンド実装時）

- [ ] HTTP送信（`StatsLogger` → `HttpSender`）
- [ ] `potions_used` — `AfterPotionUsed`
- [ ] `POST /sessions/{id}/turns` エンドポイント
- [ ] `POST /sessions/{id}/combat_end` エンドポイント

### Phase 3（WebUI）

- [ ] A〜E の各表示コンポーネント
- [ ] 10秒ポーリング

### Phase 4（後回し）

- ターン別カード使用内訳
- スター消費（`AfterStarsSpent`）
- オーバーキルダメージ
- カード別ダメージの履歴（どのターンに多く使ったか）
