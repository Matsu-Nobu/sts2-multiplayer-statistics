# データソース正規経路 (v2)

UI に出るデータの「正解の出処」を定義する。全体設計は [`../redesign-v2.md`](../redesign-v2.md)、
ゲーム内部の根拠は [`../game-api-inventory.md`](../game-api-inventory.md)。

**原則: 1 つのデータは 1 つのソースからだけ取る。** ここに書いていない経路から同じデータを取ったらバグ。
web 側で重複除去が必要になったら、それはソースの選び方が間違っているサイン。

---

## 1. mod: ゲーム内のどこから取るか

### 1.1 ラン・階

| 何 | 正規ソース (v0.111.0 で確認) | event |
|---|---|---|
| ラン開始 | 最初に部屋に入ったとき (`Hook.AfterRoomEntered`)、セッション確保と同時に 1 回 | `run_start` (プレイヤー人数分) |
| 各階の内容 (入手・除去・強化・ダウングレード・エンチャント・変化・選択・購入品・HP・ゴールド・被ダメ・部屋・ターン数) | ゲーム自身の階ごとの記録 `RunState.CurrentMapPointHistoryEntry` (`MapPointHistoryEntry` / `PlayerMapPointHistoryEntry`) | `floor_snapshot` |
| 階の確定 | `RunManager.UpdatePlayerStatsInMapPointHistory()` (private、同期) の Postfix。次の階に入る直前とラン終了時に呼ばれる | `floor_snapshot` (`is_final: true`) |
| 階の途中経過 (ライブ表示) | 送信の区切り (ターン終了・戦闘終了・報酬取得・ショップ購入・休憩所・イベント選択) | `floor_snapshot` (`is_final: false`) |
| ショップの値段 | `MerchantCardEntry / MerchantPotionEntry / MerchantRelicEntry / MerchantCardRemovalEntry` の `OnTryPurchase`。**購入処理は買った人の手元でしか動かない** (ホストには `RewardSynchronizer` で結果だけ届く) ので、取れるのはホスト自身の購入だけ | `item_purchased` |
| ラン終了 | `RunManager.OnEnded(bool isVictory)` の Postfix (最初の 1 回だけ)。放棄は `RunManager.IsAbandoned` | `run_end` |
| ラン終了直前の HP | `RunManager.WinRun()` / `RunManager.Abandon()` の Prefix (この後ゲームが全員を倒すため) | `run_end.final_hp` |
| デッキ・レリック・ポーション | 階の確定・途中経過の送信時に `Player.Deck.Cards` / `Player.Relics` / `Player.Potions` | `floor_snapshot.players[].deck` / `relics` / `potions` |
| バッジ | `RunManager.OnEnded` の Postfix で、戻り値の `SerializableRun` に対して `ScoreUtility.GetBadges(run, playerId, isVictory)`。名前は翻訳 `badges` の `{ID}.{bronze\|silver\|gold}Title` (無ければ `{ID}.title`) | `run_end.badges` |

### 1.2 戦闘

| 何 | 正規ソース | event |
|---|---|---|
| 戦闘開始 | `Hook.BeforeCombatStart` | `combat_start` |
| 戦闘勝利 | `Hook.AfterCombatEnd` (**勝ったときしか呼ばれない**) | `combat_end` (`victory: true`) |
| 戦闘敗北 | 戦闘中の `RunManager.OnEnded` | `combat_end` (`victory: false`) |
| ターン区切り | `Hook.AfterSideTurnEnd(side=Player)` | (送信の区切り。`turn_number` を進める) |
| 与ダメ・被ダメ | `Hook.AfterDamageGiven` 1 か所。受けた側が敵なら与ダメ、プレイヤーなら被ダメ (致死の一撃も呼ばれる) | `damage_dealt` / `damage_received` |
| ダメージ補正の内訳 (補正 1 つずつ) | `Hook.ModifyDamage` の Postfix で、返された「値を変えたモデル」に同じ引数で補正計算をもう一度させる。**`CreatureCmd.Damage` の実行中だけ記録する** (攻撃予告 `AttackIntent`・カード表示 `DamageVar` も同じ Hook を呼ぶため)。`AfterDamageGiven` で、その処理の中で同じ対象・攻撃者について最初に記録されたものを使う | `damage_dealt.modifications` / `damage_received.modifications` |
| HP 減少補正の内訳 (バッファー・霊体等) | `Hook.ModifyHpLost` の Postfix (同上) | `damage_received.modifications` (`hp_lost`) |
| 誰のブロックが残っているか | `Hook.AfterBlockGained` で付けた人ごとに積み、被弾時に防いだ量を残量の比で消費、`Hook.AfterBlockCleared` で消す | `damage_received.block_sources` |
| Doom による撃破 | Doom 実行中 (§1.3) の `Hook.AfterCurrentHpChanged` で敵の HP が減ったとき | `damage_dealt` (`is_doom_kill: true`) |
| ブロック | `Hook.AfterBlockGained` | `block_gained` |
| カード使用 | `Hook.AfterCardPlayed` | `card_played` |
| ドロー | `Hook.AfterCardDrawn` | `card_drawn` |
| エナジー | `Hook.AfterEnergySpent` | `energy_spent` |
| ポーション使用 | `Hook.AfterPotionUsed` | `potion_used` |
| パワー増減 | `Hook.AfterPowerAmountChanged` (戦闘中のみ) | `power_changed` |

### 1.3 誰の行為か

| 何 | 方法 |
|---|---|
| プレイヤー ID | `Identity.Normalize(Player)`: `NetId == 1` (シングルプレイ・LAN ホスト) ならローカルの Steam ID、それ以外は `NetId` |
| ペット (Osty) | 与え手・付与者の側でだけ `Creature.PetOwner` の持ち主に解決 (受けた側では解決しない) |
| カードが無いダメージ・ブロックの出どころ | `SourceContext`: 起動時に自動で patch した「パワー・レリック・オーブ・エンチャント・ポーションが宣言する、ダメージ等を起こす全メソッド」(Hook の上書きに限らない。static は型だけ) の一番内側の実行中モデル (`AsyncLocal`)。呼ぶ側 (カード等) は個別に扱わない |
| 受けた敵自身に付いたデバフ (毒・Doom 等) によるダメージの与え手 | そのデバフの付与者全員。スタック数の内訳を `source_appliers` で送り、web が全欄でスタック比で按分 (`player_id` は最大スタックの人) |

---

## 2. event → web 画面の対応

### 2.1 ラン全体 (`runOverview.ts`)

| 画面の項目 | 使う event (各階で最後の `floor_snapshot` だけ) |
|---|---|
| HP グラフ (退出時 HP) | `players[].hp.current` / `.max`。勝利・放棄のランの最後の階は `run_end.final_hp` |
| 階の HP・ゴールド (入場 → 退出) | 入場 = 1 つ前の階の退出値 (1 階は `run_start` の `hp` / `max_hp` / `gold`)、退出 = `hp.current` / `gold.current` |
| 入手 / カード | `cards_gained` |
| 入手 / レリック | `relic_choices` の `was_picked: true` |
| 入手 / ポーション | `potion_choices` の `was_picked: true` |
| デッキ改造 / アップグレード | `cards_upgraded` |
| デッキ改造 / エンチャント | `cards_enchanted` |
| デッキ改造 / 変化 | `cards_transformed` |
| デッキ改造 / 除去 | `cards_removed` |
| ショップ購入 | ショップの階で入手したもの (`cards_gained` / 選んだ `relic_choices` / 選んだ `potion_choices`)。値段は同じ階・同じ品物の `item_purchased` (ホスト自身の購入のみ取れる) |
| 選択 / 休憩所 | `rest_site_choices` |
| 選択 / イベント | `ancient_choices` (選んだもの) と `event_choices` |
| 選択 / カード選択肢 | `card_choices` (ショップの階では出さない) |
| 部屋・遭遇名 | `rooms[]` |

### 2.2 戦闘統計 (`aggregate.ts` / `rdps.ts` / `rmit.ts`)

各 `combat_index` について、**最後の `combat_start` より後の event だけ** を使う (中断→再開対策)。

| 集計 | event |
|---|---|
| 与ダメ・最大単発・オーバーキル | `damage_dealt` |
| 被ダメ・有効ブロック | `damage_received` |
| 獲得ブロック・味方付与ブロック | `block_gained` |
| カード使用・ドロー・エナジー・ポーション | `card_played` / `card_drawn` / `energy_spent` / `potion_used` |
| デバフ付与 | `power_changed` |
| 戦闘の勝敗 | `combat_end.victory` |
| 相手に付けたデバフによるダメージの按分 (与ダメ・カード別・rDPS) | `damage_dealt.source_appliers` |

---

## 3. 重複排除ルール

v2 では **無い**。v1 にあった重複排除 (ショップのカード二重表示、エンチャントの多重観測、combat_end / run_end の
二重発行、ホストの "1" と Steam ID の統合) は、すべてソースの選び直しで不要になった。

唯一の「後勝ち」ルール:
- `floor_snapshot`: 同じ階は最後に受け取ったものを使う
- 戦闘: 同じ `combat_index` は最後の `combat_start` 以降を使う

---

## 4. 削除済み / 廃止された経路

実装はもう使っていない。誤って復活させないよう履歴目的で記録:

| 廃止 | 廃止理由 |
|---|---|
| `CardModel.OnUpgrade` patch | +1 カード報酬の生成時にも発火する偽陽性 |
| `Hook.AfterItemPurchased` | `ClearAfterPurchase` 後に fire するため `MerchantEntry.CreationResult` が null |
| `RelicCmd.Obtain` 直接 patch | `Obtain<T>(Player)` との overload 衝突 |
| `CardModel.EnchantInternal` patch | deck reload 等でも発火し大量誤検出 |
| `Hook.AfterTurnEnd` | v0.111.0 で削除 (`AfterSideTurnEnd` に改名) |
| `DoomPower.BeforeTurnEnd` | v0.111.0 で削除 |
| (v2) `CardModel.FloorAddedToDeck` / `RelicModel.FloorAddedToDeck` / `Player.Gold` の setter patch、`CardCmd.Upgrade` / `CardCmd.Enchant` / `EventOption.Chosen` / `CardReward.OnSkipped` patch、`Hook.BeforeCardRemoved` / `AfterPotionProcured` / `AfterPotionDiscarded` / `AfterGoldGained` / `AfterCurrentHpChanged` (HP 記録用) / `AfterRestSiteHeal` / `AfterRestSiteSmith` / `AfterActEntered` / `AfterRewardTaken` の記録用途 | 階ごとの記録 (`floor_snapshot`) に統合。プレイヤーが空になる・二重になる・宝箱や休憩所で推測が要る、といった v1 の問題の原因 |
| (v2) `Hook.BeforeDamageReceived` / `AfterDamageReceived` patch と HP の前後比較 | `AfterDamageReceived` は致死の一撃で呼ばれない。`DamageResult` がブロック量・超過分を直接持つ |
| (v2) `Hook.AfterCombatVictory` / `AfterDeath` による勝敗・ラン終了の推定 | 呼び出し順の誤解で勝敗が常に false、ダブルボスで誤勝利、放棄を検出できない。`AfterCombatEnd` / `OnEnded` に置換 |
| (v2) `IndirectDamagePatches` (毒・Doom・雷・トゲ・Flame Barrier・Rampart・BlockNextTurn の手書き patch) と合成タグ `(poison)` 等 | `SourceContext` の自動追跡に置換 |

---

## 5. デコンパイルからの参照ポイント

新規 patch を入れる際は必ずデコンパイルでシグネチャを確認すること (CLAUDE.md §2.1)。
ゲーム更新時は `make verify-game-api` で、mod が参照する全メンバーと全 patch の引数の結び付きを確認する。

- `MapPointHistoryEntry.GetEntry(ulong playerId)` — `string` ではなく **ulong**
- Hook の combat 引数は v0.111.0 から `ICombatState` (実装は `CombatState` / `NullCombatState`)
- `Hook.AfterCombatEnd` は `CombatManager.EndCombatInternal` (勝利時) からしか呼ばれない
- `RunManager.OnEnded` は 2 回呼ばれうる (勝利後に全員を倒す処理)。ゲーム側は `_runHistoryWasUploaded` で 2 回目を無視する
- `CardRarity` enum: None / Basic / Common / Uncommon / Rare / Ancient / Event / Token / Status / Curse / Quest
