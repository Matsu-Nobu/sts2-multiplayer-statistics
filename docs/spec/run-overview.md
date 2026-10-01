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

プレイヤー ID は mod が正規化して送る (シングルプレイ・LAN ホストの `NetId=1` → Steam ID)。web 側の読み替えは無い。

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
| **デッキ改造** | アップグレード / エンチャント / 変化 / 除去 | `cards_upgraded` / `cards_enchanted` / `cards_transformed` / `cards_removed` |
| **ショップ購入** | (フラットな chip 列) | `shop_purchases` |
| **選択** | 休憩所 / イベント / カード選択肢 | `rest_options` / `event_choices` / `card_choices` |

変化 (`cards_transformed`) は `変化前 → 変化後` の chip 2 つを矢印でつなぐ。

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
- 階ごとに 1 本の並び (報酬ごとの `#1 #2` 区切りは無い。ゲームの記録に区切りが無いため。2026-10-01 確定)
- **ショップの階では出さない** (ゲームの記録に、買わなかった商品カードが「選ばなかった」として入るため)

ショップ購入の chip には末尾に `NNNG` (値段) を黄色で付加する。

### 2.6 chip ホバー tooltip (description)

カード / レリック / ポーション / エンチャント の chip に hover すると、
カタログ (`docs/architecture.md` 「カタログ」節参照) から取得した description を popover で表示する。

実装: `web/src/components/CardTooltip.svelte`、レンダラ: `web/src/lib/catalog.ts` `renderCardDescription`。

- 上段: アイテム名 (アップグレード反映: `name_upgraded` if `is_upgraded`)
- 下段: description text
  - STS2 内部装飾タグ → HTML span に変換:
    - `[gold]X[/gold]` → `text-yellow-300`
    - `[blue]X[/blue]` → `text-sky-400`
    - `[green]X[/green]` → `text-lime-300` (アップグレード差分強調)
    - `[red]X[/red]` → `text-red-400`
    - `[purple]X[/purple]` → `text-purple-400`
    - `[img]res://...[/img]` → 削除
  - 戦闘文脈依存で解決不能な `{...}` placeholder → `XX` にフォールバック (灰色)
    - 例: `{CalculatedDamage}ダメージ` → `XXダメージ`
- カードの description は `is_upgraded` に応じて base / upgraded を出し分け
- カタログに該当 id が無い場合は tooltip を出さない (chip だけ表示)
- カタログ未ロード中 (`catalog == null`) も tooltip 出さない

---

## 3. データ集計 (`runOverview.ts`)

データの出処は [`data-sources.md`](./data-sources.md) §2.1。

### 3.1 入力

- `events: EventRecord[]` — セッション全 event
- `playerId: string` — 表示するプレイヤー (単一プレイでもその人の ID を渡す)

### 3.2 出力

`FloorSummary[]` — 階番号順。

```ts
interface FloorSummary {
  floor, act_index, room_type, room_class
  encounter_name?, combat_index?, victory?
  hp_in, hp_out, max_hp_in, max_hp_out, gold_in, gold_out
  damage_taken
  cards_obtained:    { card_id, card_name?, card_rarity?, is_upgraded? }[]
  relics_obtained:   { relic_id, relic_name? }[]
  potions_obtained:  { potion_id, potion_name? }[]
  cards_upgraded:    { card_id, card_name?, card_rarity? }[]
  cards_enchanted:   { card_id, card_name?, enchantment_id, enchantment_name? }[]
  cards_transformed: { from: Card, to: Card }[]
  cards_removed:     { card_id, card_name? }[]
  rest_options:      string[]
  shop_purchases:    ItemPurchasedPayload[]
  event_choices:     { title }[]
  card_choices:      { picked_card_id, choices: { card_id, card_name, card_rarity?, is_upgraded?, was_picked }[] }[]  // 0 か 1 件
}
```

### 3.3 floor 列の構築ロジック

1. `floor_snapshot` を階ごとに集め、**各階で最後に受け取ったもの** だけを使う
2. 階の並び = snapshot のある階を番号順に (1 階 = ネオウも記録から必ず来る)
3. 各階の `players[]` から `playerId` の分を取り出して `FloorSummary` を作る
4. `room_type` / `encounter_name` は `rooms[]` の最後の部屋 (「？」マスから戦闘になった場合は戦闘の部屋)。
   `combat_index` / `victory` は同じ階の `combat_start` / `combat_end` (最後の `combat_start` 以降) から

### 3.4 HP / ゴールド

- `hp_out` / `max_hp_out` / `gold_out` = その階の `hp.current` / `hp.max` / `gold.current`
- `hp_in` / `max_hp_in` / `gold_in` = 1 つ前の階の退出値。1 階は `run_start` (そのプレイヤー) の `hp` / `max_hp` / `gold`
- **勝利・放棄のランの最後の階**: ゲームがラン終了処理で全員を倒すため記録の HP は 0。`run_end.final_hp[playerId]` を `hp_out` に使う
- 全滅のランの最後の階は記録どおり 0

### 3.5 ショップ

- ショップの階 (`room_type == Shop`) で入手したもの (`cards_gained` / `relic_choices` の選んだもの / `potion_choices` の選んだもの)
  は **すべて購入品** として「ショップ購入」欄に出し、「入手」欄には出さない (CLAUDE.md §6「ショップで買ったカードが二重表示されない」)。
  カードの除去サービスは「デッキ改造 / 除去」に出る。
- 値段: その階に同じ品物の `item_purchased` (同じプレイヤー) があれば `NNNG` を付ける。
  `item_purchased` が取れるのはホスト自身の購入だけ (ゲームの購入処理は買った人の手元でしか動かず、
  ホストには結果だけが同期されるため)。マルチプレイの相手の購入品は値段なしで出す。

---

## 4. 既知の制約

- **v1 形式のセッション** (2026-10 以前) は表示できない (DB を空にして v2 から取り直す方針、2026-10-01 確定)
- **Event の選択肢**はタイトルだけ。結果 (HP・カード・レリック等) は同じ階の各欄に出る
- **ライブ表示**: 今いる階の内容は送信の区切り (ターン終了・戦闘終了・報酬・購入・休憩所・イベント選択) ごとに更新される
