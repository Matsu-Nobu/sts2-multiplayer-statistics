# 全体再設計 (v2) — ゲーム自身の記録を正とする

対象ゲーム: Slay the Spire 2 v0.111.0 (sts2.dll 2026-08-14)。
前提資料: [`game-api-inventory.md`](./game-api-inventory.md) (ゲーム API 棚卸し)。

この文書は **実装前に確定させる設計** である。ここに書いていない挙動は実装しない。
実装中に変えたくなったら、先にこの文書を直す (CLAUDE.md §1)。

---

## 0. なぜ作り直すか

これまでの mod は「web に出したい項目ごとに、それっぽいメソッドを個別に patch する」方式で育ってきた。
その結果、次の問題が構造的に起きている (2026-10-01 の実データ 60 セッション調査より):

| 問題 | 根本原因 |
|---|---|
| マルチプレイで他人の入手物が混ざる | 一部の event を「誰の行動か」無しで送っている (ポーション入手・カード除去・ポーション廃棄は 100% 空) |
| ホスト名が空欄 (60 件中 59 件) | プレイヤー ID が経路によって Steam ID だったり "1" だったりする |
| ダブルボスの 1 体目で「勝利」になる | 勝利を「最終幕のボス部屋突破」という推測で判定している |
| 戦闘の勝敗が全戦闘「負け」 | 勝敗フラグを立てる前に戦闘終了を送っている (Hook の呼び出し順の誤解) |
| 放棄したランが永遠に「進行中」 | 放棄を検出していない |
| 毒ダメージが 0 件 | 攻撃者が空のダメージを捨てていた |
| Osty のダメージが持ち主に付かない | ペットを考慮していない |
| とどめの一撃の被ダメが抜ける | 被ダメを `AfterDamageReceived` から取っていた。これは致死の一撃では呼ばれない |
| 中断→再開で戦闘が重複 | 再開時の巻き戻しを考慮していない |
| 宝箱・休憩所の HP 等で推測ロジックが増殖 | 正しいデータ源を使っていない |

一方、デコンパイル調査で **ゲーム自身が、これらを全部正しく記録している** ことが分かった。
v2 では「ゲーム自身の記録を読んで送る」ことを原則にし、推測・重複除去・フラグ管理を無くす。

---

## 1. 調査で確定した事実 (根拠)

### 1.1 プレイヤーの識別子

| 状況 | `Player.NetId` の値 | 根拠 |
|---|---|---|
| シングルプレイ | 常に `1` | `NetSingleplayerGameService.NetId => 1uL` |
| Steam 経由のマルチプレイ | 各自の Steam ID | `SteamHost/SteamClient.NetId => SteamUser.GetSteamID()` |
| LAN (ENet) のマルチプレイ | ホストが `1`、他は割当値 | `ENetHost.NetId => 1uL` |

→ **v2 では mod 側で必ず正規化する**: `NetId == 1` かつ (シングルプレイ または ENet ホスト) のときは
ローカルの Steam ID (`PlatformUtil.GetLocalPlayerId`) に置き換える。それ以外は `NetId` をそのまま使う。
送信する全 event の `player_id` はこの正規化済み ID。web の「"1" をホストに読み替える」処理は廃止する。

### 1.2 ゲーム自身の階ごとの記録 (`MapPointHistoryEntry`)

- 階に入るたびに `RunState.AppendToMapPointHistory` で 1 件追加される。**全プレイヤー分の枠
  (`PlayerStats`) をその時点で作る**。1 階 (ネオウ) も通常の経路で入る (`EnterAct` → `EnterMapCoord`)。
- 1 つの階に部屋が複数入りうる (`Rooms[]`。「？」マス→イベント→戦闘など、`EnterRoomWithoutExitingCurrentRoom`)。
- 各プレイヤー分の記録項目と書き込み元は `game-api-inventory.md` §1.1 の表のとおり。
  マルチプレイの相手の分も、全員が同じ処理を実行する方式 (ロックステップ) と各種 Synchronizer により、
  ホストの手元で埋まる。
- **階の確定点**: `RunManager.UpdatePlayerStatsInMapPointHistory()` (同期メソッド)。
  次の階に入る直前 (`EnterMapPointInternal` 冒頭) とラン終了時 (`OnEnded`) に呼ばれ、
  退出時の HP・最大 HP・ゴールドを書き込む。
- レリック入手は `RelicCmd.Obtain` が `RelicChoices` に「選んだ」で追加 (宝箱・イベント・報酬すべて)。
  報酬をスキップしたときだけ報酬側が「選ばなかった」を追加する → 二重にならない。ポーションも同様。
- カード報酬の選択肢 (`CardChoices`) は **階ごとに 1 本の平らなリスト**。報酬ごとの区切りは無い。
  ショップの階では買わなかった商品カードも「選ばなかった」として入る (`MerchantRoom`)。

### 1.3 ゲーム自身の戦闘記録 (`CombatHistory`)

- `CombatManager.Instance.History` に、各出来事の **直後・対応する Hook より前** に同期で追記される。
  追記は 1 か所 (`CombatHistory.Add`、private) に集約されている。
- ローカルかどうかの絞り込みは無い → マルチプレイでも全員分がホストに入る。
- 記録される種類と内容:

| 記録 | 中身 | 書き込み元 |
|---|---|---|
| `DamageReceivedEntry` | `DamageResult` (ブロック量・HP 減少量・超過分・撃破), 攻撃者, カード | `CreatureCmd.Damage`。ただし **「戦闘中かつ終了処理中でない」ときだけ** 書かれる。`IsEnding` は「生きている主要な敵がいない」なので、HP を減らした後に書き込む都合上 **最後の敵へのとどめの一撃は記録されない** |
| `BlockGainedEntry` | 量, 種別, `CardPlay` (誰がどのカードで) | `CreatureCmd.GainBlock` |
| `CardPlayStartedEntry` | `CardPlay` (カード, 使用者, 対象, 自動使用か, 連続使用の何回目か) | `CardModel` |
| `CardDrawnEntry` | カード, 手札補充か | `CardPileCmd` |
| `EnergySpentEntry` | 量, プレイヤー | `CardModel` |
| `PotionUsedEntry` | ポーション, 対象 | `PotionModel` |
| `PowerReceivedEntry` | パワー, 増減量, 付与者 (**カードは無い**) | `PowerCmd.Apply / ModifyAmount` |
| その他 | 捨て札・廃棄・生成・苦悩・オーブ・スター・召喚・敵の行動 | 各 Cmd |

- 戦闘開始時と終了時に `History.Clear()` される。
- ダメージ以外 (ブロック・カード使用・ドロー・エナジー・ポーション・パワー) の書き込みには上記の終了処理中の除外が無い。
- `Hook.AfterDamageGiven(dealer, results, props, target, cardSource)` は **終了処理中でも、攻撃者が空でも、致死の一撃でも、敵→プレイヤーでも必ず呼ばれる** (`CreatureCmd.Damage` 内 `if (combatState != null)`)。一方 `Hook.AfterDamageReceived` は致死の一撃では呼ばれない。

### 1.4 ダメージの結果

`DamageResult` に `BlockedDamage / UnblockedDamage / OverkillDamage / WasTargetKilled / WasBlockBroken /
WasFullyBlocked / Receiver` がそのまま入っている。HP の前後比較による推測は不要。

### 1.5 ラン・戦闘の始まりと終わり

| 出来事 | 正しい検出点 | 根拠 |
|---|---|---|
| 戦闘開始 | `Hook.BeforeCombatStart` | `CombatManager.StartCombatInternal` |
| 戦闘勝利 | `Hook.AfterCombatEnd` | **勝ったときしか呼ばれない** (`EndCombatInternal`)。敗北は `LoseCombat` → `ProcessPendingLoss` の別経路 |
| ラン終了 | `RunManager.OnEnded(bool isVictory)` | 勝利 (`TheArchitect` → `WinRun`)・全滅 (`CreatureCmd.Kill` で全員死亡)・放棄 (`AbandonInternal` → 全員を倒す) の全てがここを通る。2 回目以降はゲーム側が `_runHistoryWasUploaded` で無視する。放棄かどうかは `RunManager.IsAbandoned` |
| ダブルボス | 2 体目は別の階 (`SecondBossMapPoint`) | `StandardActMap`。勝利は 2 体目撃破後の `TheArchitect` |
| ターン終了 | `Hook.AfterSideTurnEnd(side=Player)` | v0.111.0 で改名 |

### 1.6 中断・再開

- セーブは「階に入った直後」「戦闘終了直後」「イベント部屋」の 3 か所。
- 戦闘中に中断→再開すると、**その階に最初から入り直す** (階の記録も作り直される)。
  戦闘終了後のセーブから再開したときは、記録済みのまま続きから。
- 戦闘中の中断→再開では、中断前の戦闘はゲーム上「無かったこと」になる。

### 1.7 間接ダメージ・ブロック・付与の出どころ

- 攻撃者が空 (`dealer = null`) のダメージで敵に当たるもの: 毒 (`PoisonPower`)、`StranglePower`、
  `HauntPower`、`DemisePower`。プレイヤー自身に当たるもの: 呪い・状態異常カード、一部レリック・イベント。
- 攻撃者はいるがカードが空のもの: オーブ、トゲ、Flame Barrier、Thunder、ポーション等多数。
- Doom はダメージではなく `CreatureCmd.Kill` → `Hook.AfterDiedToDoom`。ダメージの Hook は通らない。
- ペット (Osty) は別の生き物。`Creature.PetOwner` が持ち主。
- パワー・レリック・オーブの「何かを起こす」処理は、各クラスが上書きした Hook メソッド
  (`AfterSideTurnStart` 等。パワー 253 / レリック 227 / オーブ 14 個) の中にある。共通の
  「実行中のモデル」記録はゲームに無い (`PushModel` は一部の Hook だけ)。

### 1.8 実機で確認済みの事実 (2026-10-01、修正版 mod でシングルプレイ 3 階 → 放棄)

ゲームが保存したラン履歴 (`saves/history/1790859445.run`) と、mod のログで確認した:

| 確認項目 | 結果 |
|---|---|
| mod の読み込み | patch 47 本 (patch 44 + setter 3) すべて成功、エラー 0 件 |
| シングルプレイのプレイヤー ID | 履歴でも `player_id: 1`。mod の event も一部が `"1"`、一部が Steam ID で混在 (§1.1 の正規化が必要なことを確認) |
| 1 階 (ネオウ) | 履歴の 1 件目が `type=ancient`、`EVENT.NEOW`。`ancient_choice` に 3 択と選んだもの、`relic_choices` に入手したレリック |
| カード報酬 | `card_choices` に選んだ 1 枚と選ばなかった 2 枚、`cards_gained` に入手カード |
| ポーション | `potion_choices` の `was_picked: true` |
| 部屋 | `rooms` に遭遇 ID・敵 ID 一覧・ターン数。「？」マスで戦闘になった階は `type=unknown` で部屋が `monster` |
| 放棄 | 履歴に `was_abandoned: true` / `win: false`。ゲームのログでは放棄 → 全員死亡 → 「ラン履歴を作成」の順。**mod は `run_end` を送っておらず、サーバでは「進行中」のまま** (§0 の問題を新しいゲームでも再現) |
| 放棄時の最後の階の HP | **0 になる** (全員を倒してから記録を確定するため)。→ §2.3 の注記で対応 |
| 毒ダメージ | 修正版で `(poison)` の与ダメが正しいプレイヤーに付いた (1 件) |
| 戦闘の勝敗 | 2 戦とも勝ったのに `victory: false` (§0 の問題を再現) |

未確認のまま残っているもの: マルチプレイ、戦闘中の中断→再開、勝利 (ダブルボスを含む)。

---

## 2. 全体設計

### 2.1 データの正規ソース

| 画面 | データ | v2 の正規ソース | 送る event |
|---|---|---|---|
| ラン全体 | 階ごとの全項目 (入手・除去・強化・エンチャント・変化・選択・購入・HP・ゴールド) | 階の記録 (`MapPointHistoryEntry`) | `floor_snapshot` |
| ラン全体 | ショップで買った品の **値段** | `Merchant*Entry.OnTryPurchase` (階の記録に値段が無いため残す) | `item_purchased` |
| ヘッダー | ラン結果 (勝利 / 死亡 / 放棄)・最終階 | `RunManager.OnEnded` + `IsAbandoned` | `run_end` |
| ヘッダー | キャラ・アセンション・シード・参加者 | ラン開始時の `RunState` | `run_start` |
| 戦闘統計 | 戦闘の開始・勝敗 | `BeforeCombatStart` / `AfterCombatEnd` (=勝利) / `OnEnded`・全滅 (=敗北) | `combat_start` / `combat_end` |
| 戦闘統計 | 与ダメ・被ダメ | `Hook.AfterDamageGiven` (全ヒットで必ず呼ばれる唯一の点。§1.3)。受けた側が敵なら与ダメ、プレイヤーなら被ダメとして送る | `damage_dealt` / `damage_received` |
| 戦闘統計 | ブロック・カード使用・ドロー・エナジー・ポーション | 現行の Hook (`AfterBlockGained` / `AfterCardPlayed` / `AfterCardDrawn` / `AfterEnergySpent` / `AfterPotionUsed`) | `block_gained` / `card_played` / `card_drawn` / `energy_spent` / `potion_used` |
| 戦闘統計 | パワーの増減 (付与者・カード付き) | `Hook.AfterPowerAmountChanged` (戦闘記録にはカードが無いため) | `power_changed` |
| 戦闘統計 | Doom による撃破 | Doom の実行中 (§2.4-2) の `Hook.AfterCurrentHpChanged` (敵の HP 減少) | `damage_dealt` (`is_doom_kill: true`) |
| 戦闘統計 | ターン区切り | `Hook.AfterSideTurnEnd(side=Player)` | (送信の区切りのみ) |

**原則**: 1 つのデータは 1 つのソースからだけ取る。web 側の重複除去は無くす。

### 2.2 event 一覧 (v2)

全 event 共通: `event_uuid`, `event_type`, `occurred_at`, `player_id` (§1.1 で正規化済み。
プレイヤーに属さない event だけ空), `floor`, `combat_index` (戦闘中のみ。= 階番号), `turn_number`, `sequence`。

| event | いつ | 主な中身 |
|---|---|---|
| `run_start` | ラン開始 (最初の部屋) | 各プレイヤーの ID・名前・キャラ・開始時の HP / 最大 HP / ゴールド、アセンション、シード、ゲームモード |
| `floor_snapshot` | 階の確定時 (`UpdatePlayerStatsInMapPointHistory` の後) と、ライブ表示用に送信の区切りごと (現在の階) | §2.3 |
| `item_purchased` | ショップ購入時 | 品物の ID・名前・値段、購入者 |
| `combat_start` | 戦闘開始 | `combat_index`、遭遇 (encounter) の ID・名前、部屋の種類 |
| `combat_end` | 勝利 = `AfterCombatEnd`、敗北 = 戦闘中のラン終了 (`OnEnded`) | `combat_index`、`victory` (true / false) |
| `damage_dealt` | `Hook.AfterDamageGiven` (受けた側が敵) / Doom による撃破 | 与えた人、対象、カード / 出どころ、ブロック量・HP 減少量・超過分・撃破、双方の付与中パワー一覧、ダメージ補正の記録 (`ModifyDamage`)、`is_doom_kill` |
| `damage_received` | `Hook.AfterDamageGiven` (受けた側がプレイヤー。**致死の一撃を含む**) | 受けた人、攻撃者、ブロック量・HP 減少量、双方の付与中パワー一覧 |
| `block_gained` | `BlockGainedEntry` | 受けた人、付与した人、量、カード / 出どころタグ |
| `card_played` | `CardPlayStartedEntry` | 使用者、カード (ID・名前・種類・強化段階)、対象、自動使用か、連続使用の何回目か |
| `card_drawn` | `CardDrawnEntry` | プレイヤー、カード、手札補充か |
| `energy_spent` | `EnergySpentEntry` | プレイヤー、量 |
| `potion_used` | `PotionUsedEntry` | 使用者、ポーション、対象 |
| `power_changed` | `Hook.AfterPowerAmountChanged` | 対象、付与者、カード、パワー ID・名前、増減量、付与後の量 |
| `run_end` | `RunManager.OnEnded` (最初の 1 回だけ) | `outcome` (`victory` / `death` / `abandoned`)、最終階、各プレイヤーの `final_hp` (§2.3) |

v1 から **廃止** する event: `room_entered`、
`hp_changed`、`gold_changed`、`act_entered`、`rest_action`、`reward_taken`、`card_obtained`、`card_upgraded`、
`card_enchanted`、`card_removed`、`relic_obtained`、`potion_obtained`、`potion_discarded`、`event_choice`
(→ すべて `floor_snapshot` に含まれる)。

### 2.3 `floor_snapshot` の中身

ゲームの階の記録をそのまま写し、ID には名前・レアリティを付けて送る (mod 側で `ModelDb` から引く。
web がカタログ無しでもチップを描けるようにするため)。

```
{
  floor, act_index, is_final (確定済みか),
  rooms: [{ room_type, model_id, model_name, monster_ids[], turns_taken }],
  players: [{
    player_id,
    hp: { current, max, damage_taken, healed, max_gained, max_lost },
    gold: { current, gained, spent, lost, stolen },
    cards_gained:      [card],
    cards_removed:     [card],
    cards_transformed: [{ from: card, to: card }],
    cards_upgraded:    [card_ref],
    cards_downgraded:  [card_ref],
    cards_enchanted:   [{ card, enchantment_id, enchantment_name }],
    card_choices:      [{ card, was_picked }],
    relic_choices:     [{ id, name, rarity, was_picked }],   // was_picked=true が入手
    potion_choices:    [{ id, name, was_picked }],
    potions_used:      [id], potions_discarded: [id], relics_removed: [id],
    event_choices:     [title], ancient_choices: [{ title, was_chosen }],
    rest_site_choices: [option_id],
    bought:            { relics: [id], potions: [id], colorless: [id] },
    completed_quests:  [id]
  }]
}
card = { id, name, rarity, type, upgrade_level, enchantment_id? }
```

- 同じ階の `floor_snapshot` は何度送ってもよい。**web は階ごとに最後に受け取ったものだけを使う**
  (中断→再開で階が作り直されても、最新の記録で上書きされて正しくなる)。
- 階に入ったときの HP・ゴールドは「1 つ前の階の確定値」。1 階は `run_start` の値。
- **ラン終了時の HP**: 勝利・放棄のどちらでも、ゲームは全員を倒してから最後の階を確定させるので、
  最後の階の HP は必ず 0 になる (実機で確認、§1.8)。そこで `RunManager.WinRun` と
  `RunManager.Abandon` の **直前** (Prefix) に各プレイヤーの HP を記録し、`run_end` に
  `final_hp` として入れる。web は勝利・放棄のランでは最後の階の HP にこの値を使う。全滅の場合は 0 が正しい。

### 2.4 誰の行為か (帰属)

1. **ペット**: 与えた側が `PetOwner` を持つなら持ち主のプレイヤーに付ける。受けた側では付けない
   (ペットの被ダメは持ち主の被ダメに数えない)。
2. **出どころの自動追跡 (呼ばれる側に目印を付ける)**: 起動時に、パワー・レリック・オーブ・エンチャント・ポーションの
   全具象クラスを列挙し、**そのクラスが宣言している全メソッド** (Hook の上書きに限らない。static も含む) のうち、
   本体 (非同期メソッドの実体 `MoveNext`、同じクラス内のヘルパーを 3 段まで) で
   `CreatureCmd.Damage` / `GainBlock` / `Kill`、`DamageCmd.Attack`、`PowerCmd.Apply`、`PlayerCmd.GainEnergy`
   を呼ぶものを自動で選んで patch する。patch は「今このモデルの処理中」を非同期でも引き継がれる変数
   (`AsyncLocal`) に積むだけ。
   - **呼ぶ側 (カード・レリック・別のパワー) を個別に扱わない。** 例えば毒の本体 `PoisonPower.Trigger` に目印を付けるので、
     ターン開始時の発動でも、カード効果 (`Outbreak`) からの発動でも、今後追加される何かからの発動でも、同じ「毒の処理中」になる
     (2026-10-02 確定。調査時点で外から呼ばれる処理: `PoisonPower.Trigger` / `DoomPower.DoomKill` /
     `BlackHolePower.DealDamageToAllEnemies` / `RollingBoulderPower.DoDamage` / `LightningOrb.ApplyLightningDamage`、
     呼ぶカード: `Outbreak` / `EndOfDays`)。
   - static メソッド (`DoomPower.DoomKill` 等) はインスタンスが無いので **型だけ** を積む。どのパワーかは、
     ダメージを受けた creature に付いている同じ型のパワーで決める。
   - 優先順位: カードが自分を出どころとして渡している (`cardSource`) ならカード。目印が重なったら一番内側。
   - これで、ダメージやブロックにカードが無いとき、出どころ (例: `POISON_POWER`, `LIGHTNING_ORB`,
     `BURNING_BLOOD` …) が **クラスを個別に書かなくても** 自動で付く。新しいコンテンツにも追従する。
   - **ダメージを受けた creature 自身に付いているパワー** (毒・Doom・絞殺など、相手に付けたデバフ) が出どころなら、
     そのダメージは付与した人たちのもの。付与者ごとのスタック数 (`PowerOriginRegistry`) を `source_appliers` として送り、
     web は **与ダメージ・カード別の表・貢献スコアのすべてで、スタック数の比で按分** する (2026-10-02 確定)。
     `player_id` には最大スタックの人を入れる (表示の都合。集計は `source_appliers` を使う)。
   - それ以外 (自分に付いたバフ・レリック・オーブ等) は、その持ち主の行為。
   - 現行の `IndirectDamagePatches` (毒・Doom・雷・トゲ等の手書き patch) は廃止する。
3. **Doom**: Doom の実行中に敵の HP が減ったら、減った量を `damage_dealt` (`is_doom_kill: true`、出どころ `DOOM_POWER`) として送る。
   貢献スコアでは、その量を Doom の付与者に付与量の比で配分する (§5 #3 で確定)。
4. **エナジーの付与** (今回の実装範囲外。後続): `PlayerCmd.GainEnergy` を patch し、出どころ (カード使用中ならその使用者、
   モデル実行中ならそのモデルの持ち主) を付与者として送る。戦闘画面に表示欄が無いため、画面側の仕様と合わせて別途行う。

### 2.5 戦闘の識別と中断・再開

- 戦闘の識別は現行どおり `combat_index` = その戦闘の階番号。1 つの階で戦闘は高々 1 回
  (「？」マスのイベントから戦闘になる場合も 1 回)。
- 中断→再開で同じ階の戦闘をやり直した場合、`combat_start` が同じ `combat_index` で再び届く。
  web は各 `combat_index` について **最後の `combat_start` より後の event だけ** を使う
  (中断前の戦闘はゲーム上無かったことになっているため)。mod 側の変更は不要。
- `turn_number` は現行どおり mod のカウンタ (プレイヤー側のターン終了ごとに +1)。ゲームのラウンド番号とは
  追加ターンのときだけずれるが、「プレイヤーが行動したターン」単位のほうが集計の意味に合うため。

### 2.6 mod の構成

```
mod/src/
  ModEntry.cs           patch 登録 (表で一覧化し、全件の成否をログに出す)
  Identity.cs           プレイヤー ID の正規化 (§1.1)、名前の解決
  SourceContext.cs      出どころの自動追跡 (§2.4-2)
  Lifecycle.cs          run_start / run_end / combat_start / combat_end / ターン区切り
  CombatRecorder.cs     戦闘 event (damage_dealt / damage_received / block_gained / card_played /
                        card_drawn / energy_spent / potion_used / power_changed)
  FloorRecorder.cs      floor_snapshot と item_purchased
  ModelInfo.cs          ID → 名前・レアリティ・種類の解決 (ModelDb)
  (送信まわり: EventBuffer / HttpSender / ApiClient / SessionManager / RunSessionStore は流用)
```

- patch 対象はすべて **型付きで参照** する (`nameof` / `typeof`)。名前の文字列によるリフレクションは、
  private メンバー (`UpdatePlayerStatsInMapPointHistory` 等) に限り、
  起動時に存在を確認して見つからなければエラーログを出す (黙って空文字を返すことはしない)。

### 2.7 web の変更

- ラン全体 (`runOverview.ts`): `floor_snapshot` を階ごとに最新 1 件選び、その中のプレイヤー分を表示する
  だけにする。v1 の推測ロジック (休憩所の HP、宝箱、ショップの重複除去、エンチャントの重複除去、
  ラン終了後の HP 除外、プレイヤー ID が空の event を全員に配る処理) はすべて削除。
- 戦闘統計 (`aggregate.ts`, `rdps.ts`, `rmit.ts`): 各 `combat_index` の最後の `combat_start` 以降だけ使う。
  出どころの名前が合成タグ (`(poison)` 等) からモデル ID (`POISON_POWER` 等) に変わるのに合わせる。戦闘の勝敗を表示する (spec §3.1)。
- ヘッダー: 結果に「放棄」を追加。終了済みのセッションでは定期的な取り直しを止める。
- `SessionView` の "1" → ホストの読み替えは削除 (mod で正規化済みのため)。
- 既存の型エラー (`svelte-check` の 10 件) もこの機会に解消する。

### 2.8 backend

変更なし。event を中身を見ずに保存・返却する現行方式のまま。`run_end` の `outcome` に
`abandoned` が増えるだけ (文字列なのでそのまま保存できる)。

### 2.9 ゲーム更新への備え

- 今回使った検証ツール 2 本をリポジトリに入れ、`make verify-game-api` で実行できるようにする:
  1. mod が参照する全メンバーが新しい `sts2.dll` に存在するか
  2. 全 patch の引数が Harmony で正しく結び付くか
- ゲーム更新時の手順を `docs/operations.md` に追記する (デコンパイル → 検証 → カタログ再生成)。

---

## 3. 今回見つかった問題が v2 でどう解消するか

| 問題 | v2 での扱い |
|---|---|
| マルチプレイで入手物が混ざる | 階の記録はプレイヤーごとに分かれている。プレイヤー ID が空の event を全員に配る処理も削除 |
| ホスト名が空欄 | ID を mod で正規化 (§1.1) |
| ダブルボスで誤勝利 | `OnEnded(isVictory)` を使う |
| 戦闘の勝敗が全部「負け」 | `AfterCombatEnd` = 勝利と判定 |
| 放棄が「進行中」のまま | `OnEnded` + `IsAbandoned` |
| 毒ダメージ 0 件 / Doom 0 件 | 出どころの自動追跡 + Doom による撃破を `damage_dealt` として送る |
| Osty | `PetOwner` |
| 致死の一撃の被ダメ抜け | 与ダメ・被ダメとも `AfterDamageGiven` から取る (致死の一撃でも呼ばれる) |
| 中断→再開で戦闘重複 | web が各戦闘の最後の `combat_start` 以降だけ使う |
| 戦闘外の event で空の戦闘ができる | 戦闘中 (`CombatManager.IsInProgress`) に起きたものだけを戦闘 event として送る。戦闘外のパワー変化は送らない |
| 宝箱のレリックが出ないことがある | 階の記録の `relic_choices` を表示 (宝箱を開けなかった場合は本当に空) |
| カタログ不足でチップの名前が出ない | mod が名前・レアリティを付けて送る |

---

## 4. 実装順序

各段階の終わりに CLAUDE.md §1 Step 4 の確認 (実データで画面まで見る) を行う。

1. spec 更新: `data-sources.md` / `run-overview.md` / `combat-stats.md` / `api.md` / `architecture.md` を
   この設計に合わせて書き換える (実装より先)。
2. 検証ツールのリポジトリ化 (`make verify-game-api`)。
3. mod: `Identity` → `Lifecycle` (run / combat の始まりと終わり) → `FloorRecorder` → `SourceContext` →
   `CombatRecorder` の順。旧 patch はこの段階で削除する。
4. web: 戦闘統計 → ラン全体 → ヘッダー → 型エラー解消。
5. 実機確認 (§6)。問題が無ければデプロイ。

---

## 5. 判断事項 (2026-10-01 にすべて推奨どおりで確定)

| # | 内容 | 推奨 |
|---|---|---|
| 1 | 既存セッション (v1 形式) の扱い。v2 の web では v1 の event を表示できない | DB を空にして v2 から取り直す (以前「古いデータは消してよい」との方針) |
| 2 | カード報酬の選択肢を報酬ごとに「#1 #2」と分けて出す表示 (spec §2.5)。ゲームの記録は階ごとに 1 本のリストで区切りが無い | 区切りをやめ、階ごとに「選んだ / 選ばなかった」の一覧にする。ショップの階では買わなかった商品が入るので、ショップの階は選択肢欄を出さない |
| 3 | Doom による撃破を貢献スコア (rDPS) にどう入れるか | 撃破時の HP を、Doom の付与者に付与量の比で配分して与ダメとして数える |
| 4 | ライブ表示で、今いる階の内容をどの頻度で更新するか | 送信の区切り (ターン終了・戦闘終了・階の移動) ごとに現在の階の `floor_snapshot` を送る |

---

## 5.1 v2 でも残る既知の制約

- Osty がダメージを肩代わりし、その超過分が持ち主に通った場合、持ち主側の減少分は `AfterDamageGiven` に
  渡されない (ゲームが元の対象分の結果しか渡さないため)。持ち主の被ダメがその分だけ少なく出る。
  階の記録の `damage_taken` (階合計) には含まれるので、ラン全体の画面では正しい。

## 6. 実機で確認すること (実装後)

デコンパイルだけでは断定できず、実際のゲームで確かめる項目:

- シングルプレイ / Steam マルチプレイの両方で、全 event の `player_id` が Steam ID になっていること (正規化前の値はシングルプレイで `1` と確認済み)
- マルチプレイで、相手プレイヤー分の階の記録 (入手・購入・選択) がホスト上で埋まっていること
- 1 階 (ネオウ) の `floor_snapshot` に、ネオウの選択 (`ancient_choices`) が入っていること (ゲームの記録に入ることは確認済み)
- 戦闘中に中断→再開したとき、web で戦闘が 1 回分だけ表示されること
- 勝利・全滅・放棄の 3 通りで `run_end.outcome` が正しいこと (アセンション 10 以上のダブルボスを含む)
- 毒・Doom・オーブ・トゲ・Osty の与ダメが正しいプレイヤーに付くこと
- 出どころの自動追跡で patch した件数と、起動時間への影響 (ログに件数と所要時間を出す)

---

## 7. 変更履歴

- 2026-10-01: 初版。
- 2026-10-01: 実機確認の結果を反映 (§1.8、放棄・勝利時の HP)。
- 2026-10-01: 実装着手前の見直し。web の既存集計を読んだ結果、次の 4 点を変更した。
  1. 与ダメ・被ダメは `damage` に統合せず、`damage_dealt` / `damage_received` の名前のまま両方
     `AfterDamageGiven` 1 か所から送る (データ源は 1 つに絞れる。web の戦闘集計・貢献スコア・
     タイムラインの書き直しを避ける)。
  2. ブロック・カード使用・ドロー・エナジー・ポーションは、戦闘記録 (`CombatHistory`) に切り替えず
     現行の Hook のまま (正しく取れている。戦闘記録はカード使用がダメージより前に来るため、
     最大単発ダメージの集計方式と合わない)。
  3. 戦闘の識別は `combat_key` を新設せず `combat_index` (= 階番号) のまま。中断→再開は web 側の
     「最後の `combat_start` 以降だけ使う」で解決する。
  4. Doom は新しい `doom_kill` にせず、`damage_dealt` に `is_doom_kill` を付けて送る。

