# ラン全体統計画面 仕様

`SessionView` 上部タブ「ラン全体」の挙動を定義する。実装は `web/src/components/RunOverview.svelte` および `web/src/lib/runOverview.ts`。

戦闘単位の統計（`combats` タブ）とは独立。互いの実装に依存しない。

---

## 1. ナビゲーション

`SessionView` 上部に 2 つのタブ:

- `戦闘統計` (`combats`) — 既存ビュー
- `ラン全体` (`run`) — このドキュメントの対象

タブ state は `SessionView.svelte` 内の `topTab: 'combats' | 'run'` で持つ。URL 反映なし。

階詳細パネル内の **「この戦闘の統計を見る →」** ボタンを押すと、`topTab='combats'` に切り替えつつ `activeTab=combat_index` を設定して該当戦闘の統計に飛ぶ。

---

## 2. 画面構成

```
┌───────────────────────────────┐
│ プレイヤータブ (MP のみ)        │ ← playerIds.length > 1 のとき表示
├───────────────────────────────┤
│ HP 折れ線グラフ                  │
├───────────────────────────────┤
│ 階セレクタ (プルダウン)          │
├───────────────────────────────┤
│ 階詳細パネル (展開時)            │
└───────────────────────────────┘
```

### 2.1 プレイヤータブ

- `playerIds.length > 1` のときのみ表示。
- 表示順は `doc.players` の順。
- `activePlayer` が選ばれてる player は accent 背景。
- タブ切り替えで `selectedFloor = null` にリセット。

`SessionView` で MP の host 自身の event は `LocalContext.NetId` が `"1"` (local-player pseudo) になるため、別人扱いされて 2 タブ出る問題があった。`SessionView` で `host_steam_id` が実 Steam ID のとき `"1"` を `host_steam_id` にエイリアスすることで 1 タブに統合される。

### 2.2 HP 折れ線グラフ

実装: `web/src/components/HpChart.svelte`

- x 軸: 階番号 (1 開始)
- y 軸: その階の **退出時 HP** (`hp_out`)
  - 入場時 HP ではない（理由: 階内の戦闘結果が反映される）
- 線は完全な直線 (`tension: 0`)
- ノード色: `room_type` ごとに色分け (`runOverview.ts` の `ROOM_TYPE_VISUAL`)
  - Monster: slate / Elite: orange / Boss: red / Event: purple / Shop: yellow / RestSite: green / Treasure: amber
- 選択中の階のノードは半径拡大
- ノードクリックで `selectedFloor` を更新
- ホバーでツールチップ:
  - 階番号 + room_type + encounter 名
  - HP `hp_in/max_hp_in → hp_out/max_hp_out (Δ)`
  - ゴールド `gold_in → gold_out (Δ)`
  - 入手物 / アップグレード / 除去 / エンチャント / イベント選択 (空でない要素のみ)

### 2.3 階セレクタ

- ドロップダウン形式（48 ボタンを並べる UI は廃止）
- 表示: `{floor} - {label}` または `{floor} - {label} ({encounter_name})` (戦闘あり時)
  - label は `room_type` の日本語ラベル (例: "通常戦闘 / エリート / ボス / イベント / ショップ / 休憩所 / 宝箱")
- 「— 階を選んでください —」がデフォルト
- 選択値を `selectedFloor` に同期

### 2.4 階詳細パネル

`selectedFloor != null` のとき表示。

ヘッダー:
- `room_type 絵文字` `階 N` `room_type ラベル` `— encounter_name`
- 右側: 戦闘階のとき「**この戦闘の統計を見る →**」ボタン (combat_index 紐付き) + 「閉じる ✕」ボタン

サマリ (HP / ゴールド の 2 枠):
- HP: `hp_in/max_hp_in → hp_out/max_hp_out`
- ゴールド: `gold_in → gold_out (±delta)`

詳細グループ (該当データありの group のみ表示。全部空なら「変化なし」表示):

| グループ | 表示行 | データ source |
|---|---|---|
| **入手** | カード / レリック / ポーション | `cards_obtained` / `relics_obtained` / `potions_obtained` |
| **デッキ改造** | アップグレード / エンチャント / 除去 | `cards_upgraded` / `cards_enchanted` / `cards_removed` |
| **ショップ購入** | (フラットな chip 列) | `shop_purchases` |
| **選択** | 休憩所 / イベント / カード選択肢 | `rest_options` / `event_choices` / `card_choices` |

各 group は枠付きカード内、label-value 2 列レイアウト (`grid-cols-[6rem_1fr]`)。

戦闘統計 (与ダメ/被ダメ/カード別等) は **per-floor 詳細には出さない**（「戦闘統計」タブで見る）。

### 2.5 chip 表記ルール

カード chip は **rarity で背景色 + 枠色**、**is_upgraded で文字色**:

| rarity | 背景 | 枠 |
|---|---|---|
| `Common`   | `slate-700/60` | `slate-500` |
| `Uncommon` | `sky-900/60`   | `sky-500`   |
| `Rare`     | `yellow-900/60`| `yellow-500`|
| その他 (Curse / Status / Token / Basic / Event 等) | `bg-2` | `bg-3` |

| is_upgraded | 文字色 |
|---|---|
| true | `lime-300` (黄緑) |
| false | `slate-200` (通常) |

非カード chip (レリック / ポーション / 休憩所 option / イベント等) は **rarity 概念なしのデフォルト chip** (`bg-2 / bg-3 / slate-200`)。

提示カード選択肢:
- pick されたものは通常の rarity chip
- skip されたものは `opacity-60 line-through text-slate-500`、rarity 色は維持
- 同階に複数 group ある場合は `#1 #2` の番号付き

ショップ購入のカード chip には末尾に `(NNNG)` を黄色で付加する。

---

## 3. データ集計 (`runOverview.ts`)

### 3.1 入力

- `events: EventRecord[]` — セッション全 event
- `filterPlayerId?: string` — マルチプレイ時、特定 player の視点
  - 単一プレイヤーセッション (`playerIds.length === 1`) では `undefined` を渡し、フィルタしない

### 3.2 出力

`FloorSummary[]` — 階番号順。

```ts
interface FloorSummary {
  floor, act_index, room_type, room_class
  encounter_name?, combat_index?, victory?
  hp_in, hp_out, max_hp_in, max_hp_out, gold_in, gold_out
  damage_taken, damage_dealt
  cards_obtained:   { card_id, card_name?, card_rarity?, is_upgraded? }[]
  relics_obtained:  { relic_id, relic_name? }[]
  potions_obtained: { potion_id, potion_name? }[]
  cards_upgraded:   { card_id, card_name?, card_rarity? }[]
  cards_enchanted:  { card_id, card_name?, enchantment_id, amount }[]
  cards_removed:    { card_id, card_name? }[]
  rest_options:     string[]
  shop_purchases:   ItemPurchasedPayload[]
  event_choices:    { title, history_name, text_key }[]
  card_choices:     { picked_card_id, choices: { card_id, card_name, card_rarity?, is_upgraded?, was_picked }[] }[]
}
```

### 3.3 floor 列の構築ロジック

1. `room_entered` event のある floor を skeleton として作成
2. `room_entered` が無い floor も、他 event (reward_taken / event_choice / combat_start 等) に floor 番号がついていれば skeleton 追加 (Hook.AfterRoomEntered は Neow / 初期 floor で発火しないため)
3. 最小 floor から 1 まで empty floor で backfill (Neow = floor 1 を必ず表示)

### 3.4 hp_in / gold_in の決定

- 基本: `room_entered.hp` / `.gold` (= local プレイヤーの値)
- `filterPlayerId` 指定時: 該当 player の `hp_changed` / `gold_changed` の `room_entered` 直前の最新値で上書き

### 3.5 hp_out / gold_out の決定

- `hp_out`: その階内で発生した最後の **playerId 付き** `hp_changed.current_hp`
  - playerId=null の hp_changed は **敵の HP 変動** なので除外（混入すると敵が死んだ瞬間の `cur=0` を player の hp_out として表示してしまう）
  - 階内に対象 hp_changed なしなら `hp_out = hp_in`
  - **`run_end` 以降の hp_changed は除外**: ラン終了後の cleanup / 状態リセットで HP=0 が emit されることがある (特に Act 3 ラスボス勝利後、player の死亡アニメーションが走る等)。これを拾うと「クリアしたのに HP=0」という不自然な表示になる。run_end 時点の HP を「ラン終了時 HP」として固定する。
  - 戦闘終了直後の Burning Blood (+6 等) のような戦闘終了 trigger 効果は **保持する** (combat_end の直後、run_end より前に発生する正規の HP 変化)。
- 休憩所 (`rest_action: heal`) は silent heal で `hp_changed` を発火しないため、`room_entered[restFloor+1].hp` を直接 `hp_out` として採用
- `gold_out`: その階内で発生した最後の `gold_changed.current_gold` (なければ `gold_in`)

### 3.6 重複排除

- ショップで買ったカードは `shop_purchases` と `card_obtained` 両方に乗るため、最終 pass で `cards_obtained` から `shop_purchases.card_id` と一致するものを除去
- エンチャントは mod 側 instance hashcode dedup が漏れる場合があるため、web 側で `(card_id, enchantment_id)` 単位で floor 単位 dedup

---

## 4. 既知の制約

- **CardReward の `card_choices`** は新 mod のみ (`MapPointHistoryEntry.GetEntry(ulong)` で正規取得)。古い mod で記録されたセッションは `card_id=""` / `card_choices=[]`。
- **Event の選択肢**は `EventOption.Chosen` で title のみ取得。「結果」(HP delta / カード追加 / レリック追加等) は別 event 経路 (`hp_changed` / `card_obtained` / `relic_obtained` / `gold_changed`) で同階に紐付いて見える。
- **マルチプレイ表示**: 階詳細はプレイヤータブで切り替えた人視点。HP グラフは `playerIds.length > 1` 時のみ filter 適用。
- **`room_type` 不明**な合成 floor (Neow 等で `room_entered` が無い場合) は `room_type=""` で表示される。アイコン / 色はデフォルト。
