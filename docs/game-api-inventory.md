# STS2 ゲーム API 棚卸し (v0.111.0)

ゲーム更新 (sts2.dll 2026-08-14 / Release v0.111.0) を受けて、デコンパイル結果を体系的に読み、
「UI が必要とするデータ」→「ゲーム内の正規経路」→「現 mod 実装」を突き合わせた結果。
場当たり的な patch 追加ではなく、この表を起点に spec / 実装を直す。

調査方法:
- `ilspycmd -p sts2.dll` で全 3537 ファイルをデコンパイル
- 現 mod を新 DLL でビルド (コンパイルエラー検出)
- mod が文字列 reflection で触る全メンバーを `MetadataLoadContext` で新 DLL と機械照合
- 過去ログ `sts_stats.jsonl` (〜2026-07) で実際に出ていた値を集計

---

## 1. アーキテクチャに影響する発見

### 1.1 `PlayerMapPointHistoryEntry` — ラン全体ビューの正規ソース

ゲーム自身が **階 × プレイヤーごと** に以下を記録している (マップ画面の履歴ホバー `NMapPointHistoryHoverTip` の元データ):

| フィールド | 書き込み元 (正規経路) |
|---|---|
| `CardsGained` (SerializableCard: id + upgrade level) | `CardPileCmd.Add` (pile=Deck かつ `ShouldAddToDeck`) |
| `CardsRemoved` | `CardPileCmd` (deck 除去) |
| `UpgradedCards` / `DowngradedCards` | `CardCmd.Upgrade` / `CardCmd.Downgrade` |
| `CardsEnchanted` | `CardCmd.Enchant` |
| `CardsTransformed` (original → final) | `CardCmd.Transform` |
| `CardChoices` (picked / skipped) | `CardReward` / `SpecialCardReward` / `EventModel` / `MerchantRoom` / `RewardSynchronizer` (MP peer) / 一部レリック |
| `RelicChoices` / `PotionChoices` | `RelicCmd.Obtain` / `RelicReward` / `PotionCmd` / `PotionReward` / `RewardSynchronizer` |
| `RelicsRemoved` | `RelicCmd.Remove` |
| `PotionUsed` / `PotionDiscarded` | `PotionModel` / `PotionCmd.Discard` |
| `EventChoices` | `EventSynchronizer` |
| `AncientChoices` | `AncientEventModel` |
| `RestSiteChoices` | `RestSiteSynchronizer` |
| `BoughtRelics` / `BoughtPotions` / `BoughtColorless` | `Merchant*Entry.OnTryPurchase` |
| `GoldGained` / `GoldSpent` / `GoldLost` / `GoldStolen` | `PlayerCmd.GainGold` / `LoseGold` |
| `DamageTaken` / `HpHealed` / `MaxHpGained` / `MaxHpLost` | `CreatureCmd.Damage` / `Heal` / `GainMaxHp` / `LoseMaxHp` |
| `CurrentHp` / `MaxHp` / `CurrentGold` (退出時) | `RunManager.UpdatePlayerStatsInMapPointHistory()` |

部屋情報は `MapPointHistoryEntry.Rooms[]` (`RoomType`, `ModelId`=encounter/event, `MonsterIds`, `TurnsTaken`)。

**確定タイミング**: `RunManager.UpdatePlayerStatsInMapPointHistory()` (private, **sync**) は
次の階に入る直前 (`EnterMapPointInternal` 冒頭) と ラン終了時 (`OnEnded`) に呼ばれる。
→ ここに Postfix 1 本で「確定済み 1 階分 (全プレイヤー)」を `floor_summary` として送れる。

MP: peer 分も `RewardSynchronizer` / `EventSynchronizer` 等が各クライアント上で埋めるので、host だけで全員分が揃う
(現行の「host のみ送信」方針と整合)。

### 1.2 `CombatHistory` — 戦闘イベントの単一同期ログ

`CombatManager.Instance.History` に、ゲームが「各イベント発生直後・AfterX hook 実行前」に sync で追記している:
`CardPlayStarted/Finished`, `CardDrawn`, `CardDiscarded`, `CardExhausted`, `CardGenerated`, `CardAfflicted`,
`DamageReceived(Result, Dealer, CardSource)`, `CreatureAttacked`, `BlockGained(Amount, Props, CardPlay)`,
`EnergySpent`, `PowerReceived(Power, Amount, Applier)`, `PotionUsed`, `OrbChanneled`, `StarsModified`, `Summoned`,
`MonsterPerformedMove`。各 entry は `Actor` と **プレイヤーごとの TurnNumber** を持つ。

現 mod の hook 群 (async hook の Postfix = state machine 開始時に走る) より タイミングと網羅性で優れる。
ただし間接ダメの帰属 (Poison は `Dealer=null, CardSource=null`) は依然として別途必要。

### 1.3 `DamageResult` がブロック・overkill を直接持つ

`BlockedDamage` / `UnblockedDamage` / `OverkillDamage` / `WasTargetKilled` / `WasBlockBroken` / `WasFullyBlocked` / `Receiver`。
現 mod の「ModifyDamage post を drain + BeforeDamageReceived で HP snapshot → 差分で overkill 推定」は不要になる可能性が高い。
(`OverkillDamage` は `LoseHpInternal` が算出。旧コメント「信用できない」は再検証要)

### 1.4 `RunManager.OnEnded(bool isVictory)` — run_end の正規点

sync。勝利 (`WinRun`) と全滅 (`CreatureCmd.Kill` で全員死亡) の両方がここを通る唯一の出口。
勝利時は `OnEnded` の直後に `GuaranteeKillAllPlayers()` が走る → これが「クリア後 HP=0」「ボス撃破時に誤 run_end」の原因。
現 mod の `AfterDeath` + `_currentCombatWasVictory` + `_runEndEmitted` フラグの迂回策、
spec の「run_end 以降の hp_changed を除外」ルールは、`OnEnded` を canonical にすれば不要。
放棄 (`Abandon`) は `OnEnded` を通らないので別途 (`AbandonInternal` / `RunLobby.AbandonRun`)。

---

## 2. 実装必要リスト

### P0: 更新で壊れた (ビルド不可)

| # | 内容 | 対応 |
|---|---|---|
| P0-1 | `Hook.AfterTurnEnd` 削除 | `Hook.AfterSideTurnEnd(ICombatState, CombatSide side, IEnumerable<Creature>)` に置換 (`side` 名同じ、player side 終了ごと・extra turn 含め 1 回) |
| P0-2 | `DoomPower.BeforeTurnEnd` 削除 | `BeforeSideTurnEnd` (敵側) / `AfterSideTurnEnd` (player 側) に分割。ただし P1-3 参照 |
| P0-3 | Hook 引数が `CombatState` → `ICombatState`、`NullCombatState` 新設 | 全 postfix の引数型を `ICombatState?` に |

### P1: 以前から壊れていた (データ欠損)

| # | 内容 | 根拠 | 対応 |
|---|---|---|---|
| P1-1 | **毒ダメージが 1 件も記録されていない** | `PoisonPower` は `dealer=null` で `CreatureCmd.Damage`。`AfterDamageGivenPostfix` 冒頭が `if (dealer == null) return;` で間接帰属分岐に到達しない。過去ログ: 毒付与中の被弾 970 件に対し `(poison)` 0 件 | dealer=null 早期 return を撤去し DamageSourceContext / Power.Applier で帰属 |
| P1-2 | **ペット (Osty) のダメージが落ちる** | `TryFindPlayerForCreature` が `player.Creature == dealer` のみ比較。Osty は `Creature.PetOwner` を持つ別 creature | `dealer.Player ?? dealer.PetOwner` で解決 (reflection ループ不要) |
| P1-3 | **Doom はダメージではない** | `DoomPower.DoomKill` → `CreatureCmd.Kill` (`LoseHpInternal` + `AfterCurrentHpChanged` のみ、damage hook 不発)。過去ログ `(doom)` 0 件 | `Hook.AfterDiedToDoom(ICombatState, IReadOnlyList<Creature>)` で doom kill を記録 (killed HP 量 = 直前 CurrentHp)。rDPS の doom 按分仕様を spec で再定義 |
| P1-4 | catalog の `card_type` / `cost` が全件空 | `CardModel.CardType` / `Cost` は存在しない (正: `Type`, cost は `EnergyCost` 系を要確認) | CatalogDumper 修正 |
| P1-5 | `hit_index` が常に 0 | `DamageResult.HitIndex` は存在しない。web 未使用 | api.md から削除 |
| P1-6 | catalog が古い | 新規: カード 19 (ABUNDANCE, BLADE_SYMPHONY, BLAZE, CACOPHONY, CONCOCT, CONSTELLATION, DOWSING, FADE, HIBERNATE, IMITATION_LEARNING, MIDNIGHT, ONE_FOR_ALL, OUTRAGE, PLOT, SIDESTEP, SOULBOUND, THE_BALL, TUTOR, UNDERWORLD) / レリック 3 (DOWSING_ROD, NEOWS_SACRIFICE, VAKUU_CARD_SELECTOR) / ポーション 1 (AMBERGRIS)。削除: FOLLOW_THROUGH | `make dump-catalog` (ゲーム起動必要) |

### P2: 正規経路への置換 (場当たり hack の撤去)

| # | 現状 | 置換先 | 消せるもの |
|---|---|---|---|
| P2-1 | run-overview 用に 15 本前後の個別 patch (FloorAddedToDeck setter ×2, CardCmd.Upgrade, CardCmd.Enchant, BeforeCardRemoved, EventOption.Chosen, CardReward.OnSkipped, AfterRewardTaken, AfterRestSite*, AfterPotion*, Player.Gold setter …) + web 側 dedup | `UpdatePlayerStatsInMapPointHistory` Postfix → `floor_summary` (1.1) | web の shop/card 二重表示 dedup, enchant dedup, skip 時 synthetic reward_taken, rest heal の「次階 room_entered から hp_out を推定」 |
| P2-2 | run_end を AfterDeath + 各種フラグで推定 | `RunManager.OnEnded(isVictory)` Postfix (1.4) + Abandon 経路 | `_runEndEmitted` / `_currentCombatWasVictory`、spec §3.5 の run_end 後 hp_changed 除外 |
| P2-3 | overkill/blocked を snapshot 差分で推定 | `DamageResult` の値をそのまま (1.3) | `DamageModificationLog.TakeLastPost`, `TargetHpSnapshot` (modifications 一覧が要るなら ModifyDamage は残す) |
| P2-4 | 戦闘 event を async hook の Postfix で取得 | `CombatHistory.Add` Postfix (1.2) を検討 | Before/AfterCardPlayed の scope 管理等。**要 PoC** |

ショップの「どのカードをいくらで買ったか」は history に無い (`BoughtColorless` と `GoldSpent` 合計のみ) → `Merchant*Entry.OnTryPurchase` patch は残す。
カード名 / rarity は history に無い (ModelId のみ) → web の catalog で解決 (既に catalog あり)。

### P3: spec「既知の制約」を解消できるもの

| 制約 (spec) | 使える経路 |
|---|---|
| 味方へのエナジー付与が未追跡 | `PlayerCmd.GainEnergy(amount, Player)` (呼び出し元 CardPlay から付与者を特定) |
| スター消費が未追跡 | `Hook.AfterStarsSpent` / `AfterStarsGained`、`CombatHistory.StarsModified` |
| シャッフル回数が未追跡 | `Hook.AfterShuffle` |
| (新規に出せる) カード変化 / ダウングレード / 最大 HP 増減 / ゴールド内訳 (獲得・消費・喪失・盗難) / Ancient 選択 / レリック・ポーション選択の skip | 1.1 の各フィールド |

---

## 3. 現行 docs の乖離 (実装と食い違っている記述)

`docs/spec/data-sources.md`:
- `Hook.AfterTurnEnd` → 存在しない (P0-1)
- `DoomPower.AfterSideTurnStart` → 実装は `BeforeTurnEnd`、新版は Before/AfterSideTurnEnd、そもそも damage hook 不発 (P1-3)
- `LightningOrb.{Evoke / EvokePassive / EvokeAuto}` → 実在は `Evoke` / `Passive`
- `ThornsPower.AfterDamageGiven` / `FlameBarrierPower.AfterDamageGiven` → 実装・実在は `BeforeDamageReceived` / `AfterDamageReceived`
- `RampartPower.OnTurnStart` / `BlockNextTurnPower.OnTurnStart` → 実在は `AfterSideTurnStart` / `AfterBlockCleared`
- レリック canonical `RelicCmd.Obtain` → 実装は `RelicModel.FloorAddedToDeck` setter
- エンチャント `CardModel.EnchantInternal` → 実装は `CardCmd.Enchant`

`docs/architecture.md`: `AfterTurnEnd(side=Player)` ×2。

---

## 4. 未確認 (実機で確認が必要)

- 1.1 の floor 1 (Neow) が `MapPointHistory` に入るか (`RunManager` L1156 に別経路の `AppendToMapPointHistory` あり)
- 1.1 の MP peer 分が host 上で確定タイミングまでに全部埋まっているか
- 1.2 `CombatHistory.Add` (private) を patch して性能・タイミングに問題が無いか
- `DamageResult.OverkillDamage` の値が妥当か
- Abandon 時に何を emit すべきか (現 mod の挙動含め)
