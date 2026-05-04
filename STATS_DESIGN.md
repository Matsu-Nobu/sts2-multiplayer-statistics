# 統計情報設計ドキュメント

ゲームDLL（`sts2.dll`）のリフレクションによる実調査に基づく。

---

## 収集する統計情報

### ターンごとの統計（`turn_end` イベント）

| フィールド | 型 | 説明 | 取得元hook |
|-----------|-----|------|-----------|
| `damage_dealt` | int | 敵に与えたダメージ合計 | `AfterDamageGiven` |
| `damage_received` | int | 受けたダメージ合計（ブロック貫通分） | `AfterDamageReceived` |
| `block_gained` | int | 獲得したブロック合計 | `AfterBlockGained` |
| `cards_played` | int | プレイしたカード枚数 | `AfterCardPlayed` |
| `cards_drawn` | int | ドローしたカード枚数 | `AfterCardDrawn` |
| `energy_used` | int | 消費したエナジー合計 | `AfterEnergySpent` |
| `potions_used` | int | 使用したポーション数 | `AfterPotionUsed` |

#### カード内訳（ターンごと）

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `attacks_played` | int | アタックカードのプレイ数 |
| `skills_played` | int | スキルカードのプレイ数 |
| `powers_played` | int | パワーカードのプレイ数 |
| `cards_exhausted` | int | エグゾーストしたカード数 |

---

### 戦闘ごとの統計（`combat_end` イベント）

ターン統計の累計に加えて以下を追加する。

| フィールド | 型 | 説明 | 取得元 |
|-----------|-----|------|--------|
| `total_turns` | int | 戦闘のターン数 | 集計 |
| `overkill_damage` | int | トドメ後の余剰ダメージ合計 | `AfterDamageGiven` の `results.OverkillDamage` |
| `cards_shuffled` | int | シャッフル回数（デッキを引ききった回数） | `AfterShuffle` |

---

## 使用するHook（実調査済み）

### 新規で追加するHook

```csharp
// カードプレイ: プレイ枚数・タイプ・エナジー消費の根拠
Hook.AfterCardPlayed(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)

// カードドロー
Hook.AfterCardDrawn(ICombatState combatState, PlayerChoiceContext choiceContext, CardModel card, Boolean fromHandDraw)

// ブロック獲得
Hook.AfterBlockGained(ICombatState combatState, Creature creature, Decimal amount, ValueProp props, CardModel cardSource)

// エナジー消費
Hook.AfterEnergySpent(ICombatState combatState, CardModel card, Int32 amount)

// 被ダメージ（ブロック貫通分）
Hook.AfterDamageReceived(PlayerChoiceContext choiceContext, IRunState runState, ICombatState combatState,
    Creature target, DamageResult result, ValueProp props, Creature dealer, CardModel cardSource)

// カードエグゾースト
Hook.AfterCardExhausted(ICombatState combatState, PlayerChoiceContext choiceContext, CardModel card, Boolean causedByEthereal)

// ポーション使用
Hook.AfterPotionUsed(IRunState runState, ICombatState combatState, PotionModel potion, Creature target)

// シャッフル（デッキを引ききった）
Hook.AfterShuffle(ICombatState combatState, PlayerChoiceContext choiceContext, Player shuffler)
```

### 既存のHook（変更なし）

```csharp
Hook.BeforeCombatStart    // 初期化
Hook.AfterDamageGiven     // 与ダメージ（既存）
Hook.AfterPlayerTurnStart // ターン終了タイミング（既存）
Hook.AfterCombatEnd       // 戦闘終了（既存）
```

---

## プレイヤーの特定方法

### `AfterCardPlayed` での特定
`CardPlay` → `cardPlay.Card.Owner`（`Player`型）でプレイヤーを直接取得可能。

### `AfterCardDrawn` での特定
`PlayerChoiceContext` からプレイヤーを取得する。

### `AfterBlockGained` での特定
`creature` が `CombatState.Players` のいずれかの `Creature` と一致するか確認する（既存の `TryFindPlayerForCreature` を流用）。

### `AfterEnergySpent` での特定
`card.Owner` でプレイヤーを直接取得可能。

### `AfterDamageReceived` での特定
`target` が `CombatState.Players` の `Creature` と一致するか確認（プレイヤーが受けたダメージのみ記録、モンスターへのダメージは無視）。

---

## `CardPlay` 型の活用

`AfterCardPlayed` の `CardPlay` パラメータから以下が取得できる（実API確認済み）:

```csharp
cardPlay.Card           // CardModel（カード情報）
cardPlay.Card.Type      // CardType: Attack / Skill / Power
cardPlay.Card.EnergyCost // エナジーコスト（ただしAfterEnergySpentの方が正確）
cardPlay.Card.Owner     // Player（プレイヤー直接取得）
cardPlay.Card.Title     // カード名（string）
```

---

## データ構造（更新版）

```csharp
internal record PlayerTurnStats(
    string PlayerId,
    string PlayerName,
    // ダメージ
    int DamageDealt,
    int DamageReceived,
    int OverkillDamage,
    // 防御
    int BlockGained,
    // カード
    int CardsPlayed,
    int AttacksPlayed,
    int SkillsPlayed,
    int PowersPlayed,
    int CardsDrawn,
    int CardsExhausted,
    // エナジー
    int EnergyUsed,
    // その他
    int PotionsUsed,
    int TimesShuffled
);
```

---

## 取得しないもの・理由

| 候補 | 除外理由 |
|------|---------|
| HPの推移 | `AfterCurrentHpChanged` で取れるが、ターン単位では意味が薄い（ボスHP等と混在する） |
| オーブ発動数 | `AfterOrbEvoked` で取れるが、キャラ固有でありマルチ統計として比較が難しい |
| リレイク使用 | `AfterForge` で取れるが、ゲームへの影響が間接的 |
| カード別内訳（何枚プレイしたか） | フェーズ4候補。まずは集計値で十分 |
| スター消費 | `AfterStarsSpent` で取れるが、まずは基本統計を優先 |

---

## 実装優先順位

### 今回実装（Phase 1.5）
1. `damage_dealt` ✅ 実装済み
2. `block_gained` — `AfterBlockGained`
3. `cards_played` / `attacks_played` / `skills_played` / `powers_played` — `AfterCardPlayed`
4. `cards_drawn` — `AfterCardDrawn`
5. `energy_used` — `AfterEnergySpent`
6. `damage_received` — `AfterDamageReceived`

### 後回し（Phase 4）
- `potions_used`
- `cards_exhausted`
- `times_shuffled`
- `overkill_damage`
- カード別内訳（どのカードを何枚プレイしたか）

---

## 懸念事項

| 懸念 | 対処方針 |
|------|---------|
| `AfterBlockGained` でモンスターのブロックも発火する | `TryFindPlayerForCreature` でプレイヤーのものだけ記録 |
| `AfterEnergySpent` がカード以外（リレイク等）でも発火する可能性 | `card` パラメータがnullの場合はスキップ |
| `AfterDamageReceived` でプレイヤーのOstyへのダメージが含まれる可能性 | `target` がプレイヤーの `Creature` と一致するものだけ記録 |
| `AfterCardDrawn` の `fromHandDraw` フラグ | 通常ドローのみカウントするか全ドローをカウントするか → 全ドローで集計（fromHandDrawは内訳として将来利用） |
