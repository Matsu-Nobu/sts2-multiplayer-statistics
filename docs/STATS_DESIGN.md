# 統計情報設計ドキュメント

ゲームDLL（`sts2.dll`）のリフレクションによる実調査に基づく。
設計は **「何を表示するか」→「何を収集するか」** の順で行う。

---

## 1. 統計項目 × 時間軸マトリクス

### 凡例
- ◎ … 主要表示（グラフ化）
- ○ … 補助表示（数値）
- △ … 導出値（他の値から計算）
- — … 表示しない（粒度が合わない）

| 統計項目 | ターン毎 | 戦闘毎 | 累計 |
|---------|---------|--------|------|
| **与ダメージ** | ◎ 折れ線 | ◎ 棒グラフ | ◎ 棒グラフ |
| **カードあたり与ダメージ** | △ | ◎ 数値カード | ◎ 数値カード |
| **エナジーあたり与ダメージ** | △ | ◎ 数値カード | ◎ 数値カード |
| **シールド獲得量（自分）** | ○ | ◎ 棒グラフ | ◎ 棒グラフ |
| **被ダメージ** | ○ | ◎ 棒グラフ | ◎ 棒グラフ |
| **エナジー使用量** | ○ | ○ | ○ |
| **カード使用枚数** | ○ | ○ | ○ |
| **カードドロー枚数** | ○ | ○ | ○ |
| **状態異常付与（種別ごと）** | — | ◎ テーブル | ◎ テーブル |
| **味方へのシールド付与** | — | ○ | ○ |
| **貢献スコア** | — | ◎ 棒グラフ | ◎ 棒グラフ |

### ランキング（累計のみ）

| ランキング項目 | 内容 |
|--------------|------|
| 最大単発ダメージ | その1ヒットで出した最大ダメージと使用カード名 |
| カード別総ダメージ | 戦闘全体を通じて最もダメージを稼いだカード上位N枚 |
| カード別平均ダメージ | 1プレイあたり平均ダメージが高いカード上位N枚 |
| 最多使用カード | プレイ回数が多いカード上位N枚 |
| 最多ポイズン付与カード | 付与スタック数が多いカード上位N枚（毒キャラ限定） |

---

## 2. 貢献スコアの定義

「ダメージを出さないサポート役」もフェアに評価するための合算指標。

**現状（Phase 2）**: 実装しない。集計値ベースでは正確な算定ができないため、固定係数の概算値を出してもユーザーに誤解を与えるだけと判断。

**将来（Phase 3.5 以降）**: ターン送信 API を「ターン中に発生したイベント列」を送る形に変更したあと、`damage_dealt` イベントごとに発火時の active power と applier を埋め込んで、Skada 系 mod や FFXIV FFLogs と同様の二段階（additive + multiplicative）で算出する rDPS 相当を実装する。詳細は `ROADMAP.md` の Phase 3.5 を参照。

参考調査:
- [Skada Damage Meter (STS2 Nexus)](https://www.nexusmods.com/slaythespire2/mods/33) — rDPS 二段階アルゴリズムを採用（closed-source）
- [STS2-DamageTracker (GitHub)](https://github.com/BAIGUANGMEI/STS2-DamageTracker) — 貢献度なし、生ダメージのみ
- [FFLogs rDPS Guide](https://www.fflogs.com/help/rdps) — 元ネタの定式化

### 味方エナジー付与について
STS2に専用hookが確認できなかったため、今フェーズでは追跡しない。  
`EnergyNextTurnPower` 等の特定Powerを通じて間接的に追跡できる可能性あり（Phase 4候補）。

---

## 3. 収集するraw data

### 3-1. ターン単位で収集するデータ

```
TurnData {
    player_id:              string
    player_name:            string
    combat_index:           int
    turn_number:            int
    timestamp:              ISO8601

    // 直接計測
    damage_dealt:           int     // 敵への与ダメージ合計
    damage_received:        int     // 受けたダメージ（ブロック貫通分）
    block_gained_self:      int     // 自分が獲得したブロック
    block_given_to_allies:  int     // 他プレイヤーに付与したブロック
    energy_used:            int     // 消費エナジー
    cards_played:           int     // プレイしたカード枚数
    cards_drawn:            int     // ドローした枚数
}
```

### 3-2. 戦闘単位で収集するデータ

ターンデータの累計に加えて:

```
CombatSummaryData {
    ...（TurnDataの全フィールドの累計）

    potions_used:           int

    // 状態異常付与（種別ごと）
    debuffs_applied:        { power_id: stacks_total }
    // 例: { "Poison": 18, "Vulnerable": 4, "Weak": 6 }

    // カード別集計
    card_stats: [
        {
            card_id:        string   // ModelId.Entry
            card_name:      string
            card_type:      string   // "Attack" / "Skill" / "Power" / "Status" / "Curse"
            play_count:     int
            damage_dealt:   int      // このカードが起因の総ダメージ
            block_provided: int      // このカードが生成したブロック（自分+味方）
            debuffs_applied: { power_id: stacks }
            max_single_hit: int      // このカードの1回あたり最大ダメージ
        }
    ]
}
```

### 3-3. ランキング用データ（累計・run全体）

```
RunRankings {
    player_id:      string
    player_name:    string

    max_single_hit: {
        amount:       int
        card_id:      string
        card_name:    string
        combat_index: int
        turn_number:  int
    }

    top_cards_by_total_damage:   CardStat[]  // damage_dealt降順
    top_cards_by_avg_damage:     CardStat[]  // damage_dealt/play_count降順
    top_cards_by_play_count:     CardStat[]  // play_count降順
    top_cards_by_debuff_stacks:  CardStat[]  // 特定power_idのstacks降順
}
```

---

## 4. 使用するHook一覧

### 新規追加

| Hook | 用途 |
|------|------|
| `AfterCardPlayed(combatState, choiceContext, CardPlay)` | cards_played++、カード別play_count++、カード別block/debuff初期化 |
| `AfterCardDrawn(combatState, choiceContext, CardModel, fromHandDraw)` | cards_drawn++ |
| `AfterBlockGained(combatState, Creature, amount, props, CardModel)` | creature が自分 → block_gained_self / 他プレイヤー → block_given_to_allies |
| `AfterEnergySpent(combatState, CardModel, Int32 amount)` | energy_used += amount |
| `AfterDamageReceived(choiceContext, runState, combatState, Creature target, DamageResult, props, Creature dealer, CardModel)` | target がプレイヤーのみ damage_received += amount |
| `AfterPowerAmountChanged(combatState, choiceContext, PowerModel, Decimal amount, Creature applier, CardModel)` | applier がプレイヤー かつ power.Owner が敵 かつ amount > 0 → debuffs_applied[power.Id.Entry] += amount |
| `AfterPotionUsed(runState, combatState, PotionModel, Creature target)` | potions_used++ |

### 既存（変更あり）

| Hook | 変更内容 |
|------|---------|
| `AfterDamageGiven` | カードソース帰属ロジック追加（card_stats.damage_dealt更新）、max_single_hit更新 |

### 既存（変更なし）

| Hook | 用途 |
|------|------|
| `BeforeCombatStart` | 状態リセット |
| `AfterPlayerTurnStart` | ターン確定・送信 |
| `AfterCombatEnd` | 戦闘サマリー確定・送信 |

---

## 5. APIのJSONスキーマ（バックエンド向け）

### `POST /sessions/{id}/turns`（ターン終了ごと）

```json
{
  "combat_index": 2,
  "turn_number": 3,
  "timestamp": "2026-05-05T00:00:00Z",
  "players": {
    "76561199204788207": {
      "player_name": "Ironclad",
      "damage_dealt": 45,
      "damage_received": 12,
      "block_gained_self": 20,
      "block_given_to_allies": 5,
      "energy_used": 3,
      "cards_played": 5,
      "cards_drawn": 5
    }
  }
}
```

### `POST /sessions/{id}/combat_end`（戦闘終了ごと）

```json
{
  "combat_index": 2,
  "total_turns": 5,
  "timestamp": "2026-05-05T00:00:00Z",
  "players": {
    "76561199204788207": {
      "player_name": "Ironclad",
      "damage_dealt": 180,
      "damage_received": 45,
      "block_gained_self": 110,
      "block_given_to_allies": 15,
      "energy_used": 15,
      "cards_played": 24,
      "cards_drawn": 38,
      "potions_used": 1,
      "debuffs_applied": {
        "Poison": 18,
        "Vulnerable": 4
      },
      "card_stats": [
        {
          "card_id": "Strike_R+1",
          "card_name": "Strike+",
          "card_type": "Attack",
          "play_count": 4,
          "damage_dealt": 80,
          "block_provided": 0,
          "debuffs_applied": {},
          "max_single_hit": 24
        },
        {
          "card_id": "(indirect)",
          "card_name": "(間接ダメージ: Poison等)",
          "card_type": "(indirect)",
          "play_count": 0,
          "damage_dealt": 18,
          "block_provided": 0,
          "debuffs_applied": {},
          "max_single_hit": 3
        }
      ]
    }
  }
}
```

---

## 6. 実装スコープ

### Phase 1.5（mod拡充・次フェーズ）

- [x] damage_dealt（実装済み）
- [ ] damage_received — `AfterDamageReceived`
- [ ] block_gained_self / block_given_to_allies — `AfterBlockGained`
- [ ] energy_used — `AfterEnergySpent`
- [ ] cards_played — `AfterCardPlayed`
- [ ] cards_drawn — `AfterCardDrawn`
- [ ] card_stats（play_count・damage_dealt・max_single_hit）
- [ ] debuffs_applied — `AfterPowerAmountChanged`

### Phase 2（バックエンド + HTTP送信）

- [ ] potions_used — `AfterPotionUsed`
- [ ] HTTP送信実装
- [ ] APIエンドポイント実装（turns / combat_end）
- [ ] ランキング集計（サーバー側）

### Phase 3（WebUI）

- [ ] ターン毎折れ線グラフ（与ダメ）
- [ ] 戦闘毎棒グラフ（与ダメ・被ダメ・シールド）
- [ ] カード別ダメージテーブル
- [ ] 状態異常付与テーブル
- [ ] 貢献スコア棒グラフ（換算係数はUI側で設定可）
- [ ] ランキングセクション

### Phase 4（後回し）

- 味方エナジー付与量
- オーバーキルダメージ
- スター消費（`AfterStarsSpent`）
- シャッフル回数
- 換算係数のUI調整機能
