# データソース正規経路

UI に出るデータの「正解の出処」を定義する。同じデータが複数経路で取れる場合があるが、必ず以下の **canonical path** から取ることで二重計上 / 漏れ / 順序問題を防ぐ。

実装が canonical path 以外を使ってしまっていたらバグ。

---

## 1. mod hook → event_type 一覧

### 戦闘内 (combat-scoped)

| Hook / patch 対象 | event_type | 説明 |
|---|---|---|
| `Hook.BeforeCombatStart` | `combat_start` | 戦闘開始 |
| `Hook.AfterCombatEnd` | `combat_end` | 戦闘終了 (victory フラグ付き) |
| `Hook.AfterTurnEnd(side=Player)` | (flush trigger) | turn 確定点。emit せずバッファ flush |
| `Hook.AfterDamageGiven` + `Hook.ModifyDamage` (post snapshot) | `damage_dealt` | overkill / blocked_damage は ModifyDamage post の HP snapshot で確定 |
| `Hook.BeforeDamageReceived` / `Hook.AfterDamageReceived` | `damage_received` | snapshot 差分で実 HP loss を計算 |
| `Hook.AfterBlockGained` | `block_gained` | from_player で味方付与判別 |
| `Hook.AfterEnergySpent` | `energy_spent` | |
| `Hook.BeforeCardPlayed` / `Hook.AfterCardPlayed` | `card_played` | |
| `Hook.AfterCardDrawn` | `card_drawn` | |
| `Hook.AfterPowerAmountChanged` | `power_changed` | applier 解決、複数 applier の stacks 内訳 |
| `Hook.AfterPotionUsed` | `potion_used` | `PotionModel.Owner.NetId` を user とする |
| `Hook.AfterCombatVictory` | (combat_end の victory フラグ補強) | |
| `Hook.AfterDeath` | `run_end` (dedup あり) | |

### 間接ダメージ帰属 (Harmony patch)

`spec/combat-stats.md` の rDPS のため、以下の Power / Orb 個別 patch:
- `PoisonPower.AfterSideTurnStart`
- `DoomPower.AfterSideTurnStart`
- `LightningOrb.{Evoke / EvokePassive / EvokeAuto}`
- `ThornsPower.AfterDamageGiven`
- `FlameBarrierPower.AfterDamageGiven`
- `RampartPower.OnTurnStart`
- `BlockNextTurnPower.OnTurnStart`

→ `damage_dealt` / `block_gained` の `source_card_id` に `(poison)` / `(doom)` / `(lightning)` 等の合成タグで帰属を記録する。

### ラン全体 (run-scoped)

| Hook / patch 対象 | event_type | 説明 |
|---|---|---|
| `Hook.AfterRoomEntered` | `room_entered` | floor / room_type / hp / max_hp / gold |
| `Hook.AfterCurrentHpChanged` | `hp_changed` | creature 全般 (敵含む) → web 側で playerId フィルタ必須 |
| `Hook.AfterGoldGained` | `gold_changed` | |
| `Hook.AfterActEntered` | `act_entered` | |
| `Hook.AfterRestSiteHeal` | `rest_action`(option=heal) | silent heal、HP 変化は room_entered[next].hp との差分で導出 |
| `Hook.AfterRestSiteSmith` | `rest_action`(option=smith) | アップグレード自体は CardCmd.Upgrade で取る |
| `Hook.AfterRewardTaken` | `reward_taken` | gold / potion / relic / card_choices (CardReward) |
| `Hook.AfterPotionProcured` | `potion_obtained` | 全ポーション入手の正規経路 |
| `Hook.AfterPotionDiscarded` | `potion_discarded` | |
| `Hook.BeforeCardRemoved` | `card_removed` | |
| `EventOption.Chosen` (instance method patch) | `event_choice` | LocalContext.NetId で player_id |
| `CardModel.EnchantInternal` (instance method patch) | `card_enchanted` | mod 側 dedup + web 側 dedup |
| `MerchantCardEntry.OnTryPurchase` 等 (instance method patch) | `item_purchased` | Hook.AfterItemPurchased では遅すぎるため直接 patch |

### Canonical: 正規一本化された patch

| 何 | canonical path | event_type |
|---|---|---|
| **カード追加** (戦闘報酬 / event / shop / Neow / 戦闘中生成) | `CardModel.FloorAddedToDeck` setter | `card_obtained` |
| **カードアップグレード** (smith / Apotheosis / Falling event 等) | `CardCmd.Upgrade(IEnumerable<CardModel>, CardPreviewStyle)` Postfix (sync メソッド) | `card_upgraded` |
| **レリック取得** (treasure / reward / event / 戦闘ドロップ全部) | `MegaCrit.Sts2.Core.Commands.RelicCmd.Obtain(RelicModel, Player, int)` Postfix | `relic_obtained` |

これら canonical path を使う理由:
- `CardCmd.Add` は async Task で Postfix のタイミングが state-machine 開始時 (deck 追加完了前) のため使えない → 同期の `FloorAddedToDeck` setter で代替
- `CardModel.OnUpgrade` は +1 カード報酬の生成時にも発火するため信頼できない → `CardCmd.Upgrade` は内部で `pile.Type==Deck` のときだけ `UpgradedCards.Add` する仕様 (デコンパイル確認済) なので、同じ条件で emit
- `Hook.AfterRewardTaken` の `RelicReward` 分岐だけだと宝箱 / event のレリックが取れない → `RelicCmd.Obtain` は STS2 全レリック取得経路 (treasure 含む) の集約点

---

## 2. event_type → web UI section の対応

ラン全体ビューの per-floor 詳細パネル (`runOverview.ts`):

| UI section | event_type (canonical) | 補足 |
|---|---|---|
| **入手 / カード** | `card_obtained` | `card_id`, `card_rarity`, `is_upgraded` 含む |
| **入手 / レリック** | `relic_obtained` | |
| **入手 / ポーション** | `potion_obtained` | |
| **デッキ改造 / アップグレード** | `card_upgraded` | rarity 色付き |
| **デッキ改造 / エンチャント** | `card_enchanted` | floor 単位 (card_id, enchantment_id) で web 側 dedup |
| **デッキ改造 / 除去** | `card_removed` | |
| **ショップ購入** | `item_purchased` | カードは shop 側で表示 → `cards_obtained` から最終 dedup pass で除去 |
| **選択 / 休憩所** | `rest_action` | |
| **選択 / イベント** | `event_choice` | |
| **選択 / カード選択肢** | `reward_taken` (CardReward の `card_choices`) | 提示された全 choice (picked + skipped) |

各 section の表示ルールは [`spec/run-overview.md`](./run-overview.md) §2.4 / §2.5 を見る。

---

## 3. 重複排除ルール

| 重複源 | 解決策 |
|---|---|
| ショップで買ったカードが `card_obtained` (CardCmd.Add 経由) と `item_purchased` 両方に乗る | 最終 pass で `cards_obtained` から `shop_purchases.card_id` 一致のものを除去 |
| 同じカードへの enchant が `EnchantInternal` 複数 instance で複数発火 | mod 側で `(card hashcode, enchantment_id)` HashSet dedup + web 側で `(card_id, enchantment_id)` 単位 floor 単位 dedup |
| `combat_end` が `AfterCombatEnd` と `AfterDeath` で 2 回 fire | mod 側 `_currentCombatEndEmitted` flag |
| `run_end` がボス撃破時に erroneous fire | `_currentCombatWasVictory` で AfterDeath をスキップ + `_runEndEmitted` flag |
| MP host 自身の event が player_id "1" と steam_id 両方で記録される | `SessionView` で `host_steam_id` が実 Steam ID のとき "1" を `host_steam_id` にエイリアス |

---

## 4. 削除済み / 廃止された経路

実装はもう使ってない。誤って復活させないよう履歴目的で記録:

| 廃止 | 廃止理由 |
|---|---|
| `CardModel.OnUpgrade` patch | +1 カード報酬の生成時にも発火する偽陽性。`CardCmd.Upgrade` の deck 限定条件で代替済 |
| `Hook.AfterRewardTaken` の `RelicReward` 分岐 | 宝箱 / event のレリックを通らない。`RelicCmd.Obtain` で全経路カバー |
| `Hook.AfterItemPurchased` | `ClearAfterPurchase` 後に fire するため `MerchantEntry.CreationResult` が null。各 `MerchantEntry.OnTryPurchase` を直接 patch |
| `AfterRestSiteSmithPostfix` 内の `UpgradedCards` 列挙 | 上位 `CardCmd.Upgrade` Postfix で全 upgrade 経路を 1 本にまとめたため不要 |

---

## 5. デコンパイルからの参照ポイント

実装時に「正解のシグネチャ」を確認した STS2 内部コードの参照ポイント (`/tmp/sts2_dec/sts2.decompiled.cs` 行番号、参考):

- `MapPointHistoryEntry.GetEntry(ulong playerId)` — `string` ではなく **ulong**
- `PlayerMapPointHistoryEntry` プロパティ群: `CardChoices` / `RelicChoices` / `UpgradedCards` / `BoughtRelics` / `BoughtPotions` / `CardsRemoved` / `CardsEnchanted` / `EventChoices` / `RestSiteChoices`
- `RelicCmd.Obtain(RelicModel, Player, int)` — namespace `MegaCrit.Sts2.Core.Commands`
- `CardCmd.Upgrade(IEnumerable<CardModel>, CardPreviewStyle)` — sync void method
- `CardModel.IsUpgraded` (`= CurrentUpgradeLevel > 0`) / `CardModel.Rarity` (CardRarity enum)
- `CardRarity` enum: None / Basic / Common / Uncommon / Rare / Ancient / Event / Token / Status / Curse / Quest

新規 patch を入れる際は必ずデコンパイルでシグネチャを確認すること。reflection で「string で呼んだら null だったので空返した」みたいな silent failure に何度も嵌まった (履歴: ~~Card_id 空~~, ~~smith upgrade 0 件~~)。
