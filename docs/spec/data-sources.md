# データソース正規経路

UI に出るデータの「正解の出処」を定義する。根拠にしたゲームの挙動は、この文書の最後の節「ゲームの挙動」に書く。

**原則: 1 つのデータは 1 つのソースからだけ取る。** ここに書いていない経路から同じデータを取ったらバグ。
web 側で重複除去が必要になったら、それはソースの選び方が間違っているサイン。

---

## mod: ゲーム内のどこから取るか

### ラン・階

| 何 | 正規ソース (v0.111.0 で確認) | event |
|---|---|---|
| ラン開始 | 最初に部屋に入ったとき (`Hook.AfterRoomEntered`)、セッション確保と同時に 1 回 | `run_start` (プレイヤー人数分) |
| 今のラン | ゲームの `RunState` の同一性 (`RunManager.State`)。部屋に入る・戦闘開始・階の確定・購入のどの Hook でも、送る前に今のランを確かめ、前と違うランならセッションを切り替えて、ラン終了の送信済みの目印などランごとの状態を作り直す (再開直後は「部屋に入った」より先に階の確定が走るため) | — |
| 各階の内容 (入手・除去・強化・ダウングレード・エンチャント・変化・選択・購入品・HP・ゴールド・被ダメ・部屋・ターン数) | ゲーム自身の階ごとの記録 `RunState.CurrentMapPointHistoryEntry` (`MapPointHistoryEntry` / `PlayerMapPointHistoryEntry`) | `floor_snapshot` |
| 階の確定 | `RunManager.UpdatePlayerStatsInMapPointHistory()` (private、同期) の Postfix。次の階に入る直前とラン終了時に呼ばれる | `floor_snapshot` (`is_final: true`) |
| 階の途中経過 (ライブ表示) | 送信の区切り (ターン終了・戦闘終了・報酬取得・ショップ購入・休憩所・イベント選択) | `floor_snapshot` (`is_final: false`) |
| ショップの値段 | `MerchantCardEntry / MerchantPotionEntry / MerchantRelicEntry / MerchantCardRemovalEntry` の `OnTryPurchaseInternal(MerchantInventory, bool)` の Postfix (v0.113.0 で確認。それより前の名前は `OnTryPurchase`)。v0.113.0 では **購入処理が全員の PC で実行される** (`MerchantSynchronizer.PurchaseLocalEntry` が購入を全員に送信し、受け取った PC も `MerchantEntry.OnTryPurchaseUnsynchronized` を実行する) ので、全プレイヤーの購入が取れる | `item_purchased` |
| ゲームのバージョン | ゲームの実行ファイルと同じフォルダの `release_info.json` の `version` (例 `v0.113.1`)。セッションを作るときに 1 回読む | `POST /sessions` の `game_version` |
| ラン終了 | `RunManager.OnEnded(bool isVictory)` の Postfix (最初の 1 回だけ)。放棄は `RunManager.IsAbandoned` | `run_end` |
| メニューからの放棄 | `RunHistoryUtilities.CreateRunHistoryEntry(run, victory, isAbandoned, platformType)` の Postfix。`isAbandoned` が true で、ランを読み込んでいない (`RunManager.IsInProgress` が false) ときだけ。手元に保存したセッション (自分の Steam ID とシードで引く) があれば、そのセッションに送る。`final_floor` はセーブの階の数、`final_hp` はセーブの各プレイヤーの `CurrentHp`、`badges` は全員空 | `run_end` (`abandoned`) |
| ラン終了直前の HP | `RunManager.WinRun()` / `RunManager.Abandon()` の Prefix (この後ゲームが全員を倒すため) | `run_end.final_hp` |
| デッキ・レリック・ポーション | 階の確定・途中経過の送信時に `Player.Deck.Cards` / `Player.Relics` / `Player.Potions` | `floor_snapshot.players[].deck` / `relics` / `potions` |
| バッジ | `RunManager.OnEnded` の Postfix で、戻り値 `PostRunProgressUpdateResult` の `run` (`SerializableRun`。v0.113.0 で確認) に対して `ScoreUtility.GetBadges(run, playerId, isVictory)`。名前は翻訳 `badges` の `{ID}.{bronze\|silver\|gold}Title` (無ければ `{ID}.title`) | `run_end.badges` |

### 戦闘

| 何 | 正規ソース | event |
|---|---|---|
| 戦闘開始 | `Hook.BeforeCombatStart` の Prefix (レリック等の戦闘開始時の効果より前。v0.113.0 で確認) | `combat_start` |
| 戦闘勝利 | `Hook.AfterCombatEnd` (**勝ったときしか呼ばれない**) | `combat_end` (`victory: true`) |
| 戦闘敗北 | 戦闘中の `RunManager.OnEnded` | `combat_end` (`victory: false`) |
| ターン区切り | `Hook.AfterSideTurnEnd(side=Player)` | (送信の区切り。`turn_number` を進める) |
| 与ダメ・被ダメ | `Hook.AfterDamageGiven` 1 か所。受けた側が敵なら与ダメ、プレイヤーなら被ダメ (致死の一撃も呼ばれる) | `damage_dealt` / `damage_received` |
| ダメージ補正の内訳 (補正 1 つずつ) | `Hook.ModifyDamage` の Postfix で、返された「値を変えたモデル」に同じ引数で補正計算をもう一度させる。**`CreatureCmd.Damage` の実行中だけ記録する**。実行中かどうかは、`Damage` のすべての多重定義が最後に呼ぶ `Damage(PlayerChoiceContext, IEnumerable<DamageTargetData>, decimal, ValueProp, Creature, CardModel, CardPlay, ModifyDamageHookType)` (v0.113.0 で確認) の Prefix / Postfix で区切る (攻撃予告 `AttackIntent`・カード表示 `DamageVar` も同じ Hook を呼ぶため)。`Damage` は全対象の補正計算を先に済ませてから、対象ごとに `AfterDamageGiven` を呼ぶ。そのため `AfterDamageGiven` では、`DamageResult.OriginalTarget` (攻撃が本来狙った相手) と攻撃者が同じで、まだ使っていない記録を 1 つ使う。記録を使うのは event を送るときだけにする (オスティが受けた分のように送らない結果で、本人の分の記録を使い切らないため) | `damage_dealt.modifications` / `damage_received.modifications` |
| HP 減少補正の内訳 (バッファー・霊体等) | `Hook.ModifyHpLost` の Postfix (同上)。ダメージ補正の記録の後から次のダメージ補正の記録の前までにある、被ダメを受けたプレイヤーへの記録を使う | `damage_received.modifications` (`hp_lost`) |
| 誰のブロックが残っているか | `Hook.AfterBlockGained` で付けた人ごとに積み、被弾時に防いだ量を残量の比で消費、`Hook.AfterBlockCleared` で消す | `damage_received.block_sources` |
| Doom による撃破 | Doom 実行中 (「誰の行為か」) の `Hook.AfterCurrentHpChanged` で敵の HP が減ったとき | `damage_dealt` (`is_doom_kill: true`) |
| ブロック | `Hook.AfterBlockGained` | `block_gained` |
| カード使用 | `Hook.AfterCardPlayed` | `card_played` |
| ドロー | `Hook.AfterCardDrawn` | `card_drawn` |
| エナジー | `Hook.AfterEnergySpent` | `energy_spent` |
| ポーション使用 | `Hook.AfterPotionUsed` | `potion_used` |
| パワー増減 | `Hook.AfterPowerAmountChanged` (戦闘中のみ) | `power_changed` |

### 誰の行為か

| 何 | 方法 |
|---|---|
| プレイヤー ID | `Identity.Normalize(Player)`: `NetId == 1` (シングルプレイ・LAN ホスト) ならローカルの Steam ID、それ以外は `NetId` |
| ペット (Osty) | 与え手・付与者の側でだけ `Creature.PetOwner` の持ち主に解決 (受けた側では解決しない) |
| カードが無いダメージ・ブロックの出どころ | `SourceContext`: 起動時に自動で patch した「パワー・レリック・オーブ・エンチャント・ポーションが宣言する、ダメージ等を起こす全メソッド」(Hook の上書きに限らない。static は型だけ) の一番内側の実行中モデル (`AsyncLocal`)。呼ぶ側 (カード等) は個別に扱わない |
| 受けた敵自身に付いたデバフ (毒・Doom 等) によるダメージの与え手 | そのデバフの付与者全員。スタック数の内訳を `source_appliers` で送り、web が全欄でスタック比で按分 (`player_id` は最大スタックの人) |

---

## event → web 画面の対応

### ラン全体 (`runOverview.ts`)

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
| ショップ購入 | ショップの階で入手したもの (`cards_gained` / 選んだ `relic_choices` / 選んだ `potion_choices`)。値段は同じ階・同じ品物・同じプレイヤーの `item_purchased` |
| 選択 / 休憩所 | `rest_site_choices` |
| 選択 / イベント | `ancient_choices` (選んだもの) と `event_choices` |
| 選択 / カード選択肢 | `card_choices` (ショップの階では出さない) |
| 部屋・遭遇名 | `rooms[]` |

### 戦闘統計 (`aggregate.ts` / `rdps.ts` / `rmit.ts`)

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
| パワーによるダメージ・ブロックのカード別の行 | 付与時の `Hook.AfterPowerAmountChanged` の `cardSource` (無ければ実行中のレリック・ポーション等、パワーならそのパワーを付けた持ち物) を `PowerOriginRegistry` に「誰が・何で」の内訳として記録 → `source_appliers[].origin` / `source_origin` |
| カードの効果が直接発動させた、カード以外のもののダメージ・ブロック (感染爆発の毒、オーブの解放・自動効果、終末の日の破滅等) | `CardModel.OnPlayWrapper` の Prefix / Postfix で「プレイ中のカード」を AsyncLocal に持つ (この中では実行中モデルの記録を空から始める)。SourceContext は Hook の上書き (`AbstractModel` で宣言されたメソッドの上書き) を「反応」として印を付け、プレイ中で反応の中でなく、`OrbCmd.Channel` (生成。枠があふれると解放する) の中でもなければ `triggered_by` を付ける |

---

## 重複排除ルール

重複排除はしない。同じデータを 2 つの経路から取らないように、ソースを選ぶ。

「後勝ち」の規則は次の 2 つだけ:
- `floor_snapshot`: 同じ階は最後に受け取ったものを使う
- 戦闘: 同じ `combat_index` は最後の `combat_start` 以降を使う

---

## 使わない経路

次の経路は、表の理由で正しいデータが取れない。同じデータを取るときに使わない:

| 廃止 | 廃止理由 |
|---|---|
| `CardModel.OnUpgrade` patch | +1 カード報酬の生成時にも発火する偽陽性 |
| `Hook.AfterItemPurchased` | `ClearAfterPurchase` 後に fire するため `MerchantEntry.CreationResult` が null |
| `RelicCmd.Obtain` 直接 patch | `Obtain<T>(Player)` との overload 衝突 |
| `CardModel.EnchantInternal` patch | deck reload 等でも発火し大量誤検出 |
| `Hook.AfterTurnEnd` | v0.111.0 で削除 (`AfterSideTurnEnd` に改名) |
| `DoomPower.BeforeTurnEnd` | v0.111.0 で削除 |
| `CardModel.FloorAddedToDeck` / `RelicModel.FloorAddedToDeck` / `Player.Gold` の setter patch、`CardCmd.Upgrade` / `CardCmd.Enchant` / `EventOption.Chosen` / `CardReward.OnSkipped` patch、`Hook.BeforeCardRemoved` / `AfterPotionProcured` / `AfterPotionDiscarded` / `AfterGoldGained` / `AfterCurrentHpChanged` (HP 記録用) / `AfterRestSiteHeal` / `AfterRestSiteSmith` / `AfterActEntered` / `AfterRewardTaken` の記録用途 | 階ごとの記録 (`floor_snapshot`) に統合。プレイヤーが空になる・二重になる・宝箱や休憩所で推測が要る、といった v1 の問題の原因 |
| `Hook.BeforeDamageReceived` / `AfterDamageReceived` patch と HP の前後比較 | `AfterDamageReceived` は致死の一撃で呼ばれない。`DamageResult` がブロック量・超過分を直接持つ |
| `Hook.AfterCombatVictory` / `AfterDeath` による勝敗・ラン終了の推定 | 呼び出し順の誤解で勝敗が常に false、ダブルボスで誤勝利、放棄を検出できない。`AfterCombatEnd` / `OnEnded` に置換 |
| `IndirectDamagePatches` (毒・Doom・雷・トゲ・Flame Barrier・Rampart・BlockNextTurn の手書き patch) と合成タグ `(poison)` 等 | `SourceContext` の自動追跡に置換 |

---

## ゲームの挙動

上の経路を選んだ根拠。デコンパイル (`ilspycmd -p sts2.dll`) で確認した。新しく patch を入れるときは、
必ずデコンパイルでシグネチャと呼び出し元を確かめる (CLAUDE.md §2.1)。ゲームを更新したら `make verify-game-api` で、
mod が参照する全メンバーと全 patch の引数の結び付きを確かめる。

### プレイヤーの識別子

| 状況 | `Player.NetId` の値 | 根拠 |
|---|---|---|
| シングルプレイ | 常に `1` | `NetSingleplayerGameService.NetId => 1uL` |
| Steam 経由のマルチプレイ | 各自の Steam ID | `SteamHost/SteamClient.NetId => SteamUser.GetSteamID()` |
| LAN (ENet) のマルチプレイ | ホストが `1`、他は割当値 | `ENetHost.NetId => 1uL` |

mod は `NetId == 1` かつ (シングルプレイ または ホスト) のとき、ローカルの Steam ID に置き換えて送る (`Identity`)。

### 階ごとの記録

- 階に入るたびに `RunState.AppendToMapPointHistory` で 1 件追加され、全プレイヤー分の枠 (`PlayerStats`) がその時点で作られる。
  1 階 (ネオウ) も同じ経路で入る。マルチプレイの相手の分も、全員が同じ処理を実行する方式 (ロックステップ) によりホストの手元で埋まる。
- 1 つの階に部屋が複数入りうる (「？」マスからの戦闘など。`Rooms[]`)。
- 確定点は `RunManager.UpdatePlayerStatsInMapPointHistory()`。次の階に入る直前とラン終了時に呼ばれ、退出時の HP・ゴールドを書き込む。
- レリックの入手は `RelicCmd.Obtain` が `RelicChoices` に「選んだ」で追加する。報酬をスキップしたときだけ報酬側が「選ばなかった」を追加するので、二重にならない。ポーションも同じ。
- カード報酬の選択肢 (`CardChoices`) は階ごとに 1 本の平らなリストで、報酬ごとの区切りが無い。ショップの階では、買わなかった商品カードも「選ばなかった」として入る。
- `MapPointHistoryEntry.GetEntry` の引数は `ulong` (プレイヤーの NetId)。

### ダメージの処理

- `CreatureCmd.Damage` は、全対象について補正計算 (`Hook.ModifyDamage`) と HP の減少を先に済ませ、その後で結果ごとに `Hook.AfterDamageGiven` を呼ぶ。
- `Hook.AfterDamageGiven` は、終了処理中でも、攻撃者が空でも、致死の一撃でも、敵からプレイヤーへの攻撃でも呼ばれる。`Hook.AfterDamageReceived` は致死の一撃では呼ばれない。
- `DamageResult` は `BlockedDamage` / `UnblockedDamage` / `OverkillDamage` / `WasTargetKilled` / `Receiver` / `OriginalTarget` (攻撃が本来狙った相手) を持つ。`TotalDamage` は `BlockedDamage + UnblockedDamage` で、超過分を含まない。
- ネクロバインダーへのダメージがオスティに移るとき、補正計算は本来の相手 (プレイヤー) で行われ、結果はオスティの分とプレイヤーの分に分かれる。
- `Hook.ModifyDamage` の計算順は、カードのエンチャント → 足し算 (`ModifyDamageAdditive`) → 掛け算 (`ModifyDamageMultiplicative` → `ModifyDamageMultiplicativeLate`) → 上限 (`ModifyDamageCap`)。結果は 0 未満にならない。
- `Hook.ModifyHpLost` の段階は `BeforeOsty` (`ModifyHpLostBeforeOsty` → `…Late`) と `AfterOsty` (`ModifyHpLostAfterOsty` → `…Late`)。`HpLossHookPhase.BeforeWuppy` を受け取る補正の関数はゲームに無い。

### カードの無いダメージ

- 攻撃者が空のダメージ: 毒 (`PoisonPower`)、絞殺 (`StranglePower`) など、相手に付いたデバフの処理。
- 攻撃者はいるがカードが空のダメージ: オーブ、棘、ポーション、レリックなど。
- 破滅はダメージではなく `CreatureCmd.Kill` で、ダメージの Hook を通らない (`DoomPower.DoomKill`)。
- `CreatureCmd.SetCurrentHp` で HP を直接変える処理 (カード「審判」、レリック「毛皮のコート」) も、ダメージの Hook を通らない。
  ゲーム自身も与ダメージに数えないので、mod も記録しない。
- ペット (オスティ) は別の生き物で、`Creature.PetOwner` が持ち主。
- パワー・レリック・オーブの「何かを起こす」処理は、各クラスが上書きした Hook のメソッドの中にあり、ゲームには「今どのモデルの処理中か」の共通の記録が無い。
  そのため mod は、起動時にパワー・レリック・オーブ・エンチャント・ポーションの全クラスについて、`CreatureCmd.Damage` / `GainBlock` / `Kill`、`DamageCmd.Attack`、
  `PowerCmd.Apply`、`PlayerCmd.GainEnergy` を呼ぶメソッドを自動で選んで patch し、実行中のモデルを `AsyncLocal` に積む (`SourceContext`)。
  呼ぶ側 (カード等) は個別に扱わない。static メソッド (`DoomPower.DoomKill` 等) は型だけを積み、どのパワーかはダメージを受けた相手に付いている同じ型のパワーで決める。

### ラン・戦闘の始まりと終わり

| 出来事 | 検出点 | 根拠 |
|---|---|---|
| 戦闘開始 | `Hook.BeforeCombatStart` の Prefix | `CombatManager.StartCombatInternal`。この Hook は非同期で、Postfix は効果が最初に待つ所まで進んだ後に呼ばれる |
| 戦闘勝利 | `Hook.AfterCombatEnd` | 勝ったときしか呼ばれない (`EndCombatInternal`)。敗北は `LoseCombat` の別経路 |
| ラン終了 | `RunManager.OnEnded(bool isVictory)` | 勝利・全滅・放棄の全てが通る。勝利後に全員を倒す処理で 2 回目が来る (ゲームは `_runHistoryWasUploaded` で無視する)。放棄かどうかは `RunManager.IsAbandoned` |
| メニューからの放棄 | `RunHistoryUtilities.CreateRunHistoryEntry` | メインメニューの放棄 (`NMainMenu.AbandonRun`) とマルチプレイのメニューの放棄 (`NMultiplayerSubmenu.TryAbandonMultiplayerRun`) は、ランを読み込まずに、セーブデータからこのメソッドを直接呼ぶ。`RunManager.OnEnded` は通らない。ラン中の放棄でも `OnEnded` からこのメソッドが呼ばれる。ゲームは放棄したランにバッジを付けない (`GetBadgesForPlayer`) |
| ラン終了時の HP | `RunManager.WinRun` / `Abandon` の Prefix | 勝利・放棄では、ゲームが全員を倒してから最後の階を確定させるので、最後の階の記録の HP は 0 になる |
| ダブルボス | 2 体目は別の階 (`SecondBossMapPoint`) | 勝利は 2 体目の撃破後 |
| ターン終了 | `Hook.AfterSideTurnEnd(side=Player)` | |

Hook の戦闘の引数は `ICombatState` (実装は `CombatState` / `NullCombatState`)。

### 中断・再開

- セーブは「階に入った直後」「戦闘終了直後」「イベント部屋」の 3 か所。
- 戦闘中に中断して再開すると、その階に最初から入り直す (階の記録も作り直される)。中断前の戦闘はゲーム上無かったことになる。
- 再開直後は、「部屋に入った」(`Hook.AfterRoomEntered`) より先に階の確定 (`UpdatePlayerStatsInMapPointHistory`) が走る。

### その他

- `CardRarity`: None / Basic / Common / Uncommon / Rare / Ancient / Event / Token / Status / Curse / Quest
- 休憩所の選択肢の名前は、翻訳 `rest_site_ui` の `OPTION_{OptionId}.name` (`RestSiteOption.Title`)
