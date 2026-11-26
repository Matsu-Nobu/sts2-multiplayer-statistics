# ラン全体・階の画面 仕様

ラン全体の画面と階の画面に **何を出すか** (データ・集計方法・チップの規則) を定める。
画面の構成・配置・URL は [`ui.md`](./ui.md) に従う。
実装は `web/src/lib/runOverview.ts` (集計)、`web/src/pages/RunPage.svelte` / `FloorPage.svelte`、
`web/src/components/HpRunChart.svelte` (推移のグラフ) / `FloorDetail.svelte` (階の詳細)。

---

## 対象のプレイヤー

階の詳細とプレイヤーの最終状態は、AppShell のプレイヤー切り替えで選んでいるプレイヤーの視点で出す (ui.md「画面の骨格 (AppShell)」)。
推移のグラフは全員の線を重ねる。

---

## 表示

### 部屋の種類の表示

部屋の種類 (`room_type`) ごとの絵文字・日本語ラベル・色 (`runOverview.ts` の `ROOM_TYPE_VISUAL`)。推移のグラフの点の色と、階セレクタ・階の見出しに使う。

| room_type | 絵文字 | ラベル | 色 |
|---|---|---|---|
| Monster | ⚔️ | 通常戦闘 | slate |
| Elite | 💀 | エリート | orange |
| Boss | 👑 | ボス | red |
| Event | ❓ | イベント | purple |
| Shop | 🏪 | ショップ | yellow |
| RestSite | 🔥 | 休憩所 | green |
| Treasure | 📦 | 宝箱 | amber |

### 階の詳細

詳細グループ (該当データありの group のみ表示。全部空なら「変化なし」表示):

| グループ | 表示行 | データ source |
|---|---|---|
| **入手** | カード / レリック / ポーション | `cards_obtained` / `relics_obtained` / `potions_obtained` |
| **デッキ改造** | アップグレード / エンチャント / 変化 / 除去 | `cards_upgraded` / `cards_enchanted` / `cards_transformed` / `cards_removed` |
| **ショップ購入** | (フラットな chip 列) | `shop_purchases` |
| **選択** | 休憩所 / イベント / カード選択肢 | `rest_options` / `event_choices` / `card_choices` |
| **AI の評価** | カード報酬 | `card_reward_evaluations` (「AI の評価」) |

変化 (`cards_transformed`) は `変化前 → 変化後` の chip 2 つを矢印でつなぐ。

休憩所の chip は、ゲームの選択肢の ID (`SMITH` 等) をカタログの `rest_site_options` で日本語名 (「鍛冶」等) にして出す。カタログに無い ID はそのまま出す。

各 group は枠付きカード内、label-value 2 列レイアウト (`grid-cols-[6rem_1fr]`)。

戦闘の統計 (与ダメ・被ダメ・カード別等) は階の詳細には出さない (戦闘詳細の画面で見る)。

### AI の評価

カード報酬の AI の評価 ([`card-evaluation.md`](./card-evaluation.md)) を、表示しているプレイヤーのこの階の分だけ出す。評価が 1 件も無い階では枠を出さない。

- 見出し「AI の評価」の横に「?」ボタン (`InfoTip`) を置く。説明は「カード報酬を開いたときに、AI（AlphaDefect）がそれぞれの選択肢を選ぶ確率です。AI は 1 つを強く選ぶ傾向があり、100% 近い数字は、ほかの選択肢が悪いという意味ではありません。」
- カード報酬 1 つにつき、見出しの行と chip の行を 1 組ずつ出す。ほかの枠のような「見出しと値の 2 列」にはせず、画面の幅にかかわらず、見出しを上の行に、chip をその下の行に置く。見出しは、この階のカード報酬が 1 つなら「カード報酬」、2 つ以上なら「カード報酬 1」「カード報酬 2」…。
- 同じカード報酬を開き直したときは、最後に開いたときの評価だけを出す。同じプレイヤー・同じ階で、提示されたカードの並びが同じ評価を同じカード報酬とみなす。
- `status` が `ok` の行は、提示されたカードの chip を並べ、各 chip の後ろに確率を付ける。そのあとに `alternatives` の chip (ゲームの表記に合わせて、`Skip` は「スキップ」、`REROLL` は「リロール」、`SACRIFICE` は「捧げる」。それ以外は ID のまま) と確率を付ける。
- カードの chip は「chip 表記ルール」に従う。選ばなかったカードの打ち消し線は付けない。
- 確率は % で、小数 1 桁。0.1% 未満か 99.9% より大きいときは小数 2 桁。行の中で一番高い確率だけを白の太字にする。
- `status` が `ok` でない行は、提示されたカードの chip のあとに、確率の代わりに次の文を出す。

| `status` | 文 |
|---|---|
| `unknown_card` | AI が学習していないカードがあるため、評価していません |
| `unsupported_version` | このゲームのバージョンには、AI が対応していません |
| `unsupported_character` | このキャラクターには、AI が対応していません |
| `invalid_payload` | 評価できませんでした |

### chip 表記ルール

カード chip は **rarity で背景色 + 枠色**、**エンチャントと is_upgraded で文字色**:

| rarity | 背景 | 枠 |
|---|---|---|
| `Common`   | `slate-700/60` | `slate-500` |
| `Uncommon` | `sky-900/60`   | `sky-500`   |
| `Rare`     | `yellow-900/60`| `yellow-500`|
| その他 (Curse / Status / Token / Basic / Event 等) | `bg-2` | `bg-3` |

| カードの状態 | 文字色 |
|---|---|
| エンチャントが付いている (`enchantment_id` あり) | `purple-300` (紫)。強化済みでも紫を優先する |
| 強化済み (`is_upgraded`) | `lime-300` (黄緑) |
| どちらでもない | `slate-200` (通常) |

非カード chip (レリック / ポーション / 休憩所 option / イベント等) は **rarity 概念なしのデフォルト chip** (`bg-2 / bg-3 / slate-200`)。

提示カード選択肢:
- pick されたものは通常の rarity chip
- skip されたものは `opacity-60 line-through text-slate-500`、rarity 色は維持
- 階ごとに 1 本の並び (報酬ごとの `#1 #2` 区切りは無い。ゲームの記録に区切りが無いため)
- **ショップの階では出さない** (ゲームの記録に、買わなかった商品カードが「選ばなかった」として入るため)

ショップ購入の chip には末尾に `NNNG` (値段) を黄色で付加する。

### chip ホバー tooltip (description)

カード / レリック / ポーション / エンチャント の chip に hover すると、
カタログ (`docs/architecture.md` 「カタログ」節参照) から取得した description を popover で表示する。

実装: `web/src/components/CardTooltip.svelte`、レンダラ: `web/src/lib/catalog.ts` `renderCardDescription`。

- 名前は chip に書いてあるので、popover には description だけを出す
- エンチャントの付いたカードは、カードの description の下に、区切り線を挟んでエンチャントの名前 (紫) とその description を出す
- description text
  - STS2 内部装飾タグ → HTML span に変換:
    - `[gold]X[/gold]` → `text-yellow-300`
    - `[blue]X[/blue]` → `text-sky-400`
    - `[green]X[/green]` → `text-lime-300` (アップグレード差分強調)
    - `[red]X[/red]` → `text-red-400`
    - `[purple]X[/purple]` → `text-purple-400`
    - `[img]res://.../*energy_icon.png[/img]` (エナジーのアイコン) → `〇`
    - `[img]res://.../star_icon.png[/img]` (星のアイコン) → `☆`
    - それ以外の `[img]res://...[/img]` → 削除
  - 戦闘文脈依存で解決不能な `{...}` placeholder → `XX` にフォールバック (灰色)
    - 例: `{CalculatedDamage}ダメージ` → `XXダメージ`
  - 条件で文が変わる `{名前:...:A|B}` (中に `{...}` を含むこともある) → 最後の選択肢 `B` を出す。
    ゲームが戦闘の外でカードを表示するときと同じ文になる。
    - 例: `{TargetType:choose(AllEnemies):すべての敵に|}XXダメージ` → `XXダメージ`
- バッジ (「プレイヤーの最終状態 (ラン全体の画面)」) の説明も同じ変換を通して出す
- カードの description は `is_upgraded` に応じて base / upgraded を出し分け
- カードの description は、ゲームがカードに表示する文と同じく、説明文の前後にキーワード (プレイ不可・エセリアル・廃棄など) の行を含む。キーワードだけのカード (呪いの多く) は、キーワードの行だけになる
- カタログに該当 id が無い場合は tooltip を出さない (chip だけ表示)
- カタログ未ロード中 (`catalog == null`) も tooltip 出さない

---

## データ集計 (`runOverview.ts`)

データの出処は [`data-sources.md`](./data-sources.md) の「ラン全体 (`runOverview.ts`)」。

### 入力

- `events: EventRecord[]` — セッション全 event
- `playerId: string` — 表示するプレイヤー (単一プレイでもその人の ID を渡す)

### 出力

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
  shop_purchases:    { kind: 'card' | 'relic' | 'potion', id, name, rarity?, is_upgraded?, gold_spent? }[]  // gold_spent は同じ品物の item_purchased があるときだけ
  event_choices:     { title }[]
  card_choices:      { picked_card_id, choices: { card_id, card_name, card_rarity?, is_upgraded?, was_picked }[] }[]  // 0 か 1 件
}
```

### floor 列の構築ロジック

1. `floor_snapshot` を階ごとに集め、**各階で最後に受け取ったもの** だけを使う
2. 階の並び = snapshot のある階を番号順に (1 階 = ネオウも記録から必ず来る)
3. 各階の `players[]` から `playerId` の分を取り出して `FloorSummary` を作る
4. `room_type` / `encounter_name` は `rooms[]` の最後の部屋 (「？」マスから戦闘になった場合は戦闘の部屋)。
   `combat_index` / `victory` は同じ階の `combat_start` / `combat_end` (最後の `combat_start` 以降) から

### HP / ゴールド

- `hp_out` / `max_hp_out` / `gold_out` = その階の `hp.current` / `hp.max` / `gold.current`
- `hp_in` / `max_hp_in` / `gold_in` = 1 つ前の階の退出値。1 階は `run_start` (そのプレイヤー) の `hp` / `max_hp` / `gold`
- **勝利・放棄のランの最後の階**: ゲームがラン終了処理で全員を倒すため記録の HP は 0。`run_end.final_hp[playerId]` を `hp_out` に使う
- 全滅のランの最後の階は記録どおり 0

### ショップ

- ショップの階 (`room_type == Shop`) で入手したもの (`cards_gained` / `relic_choices` の選んだもの / `potion_choices` の選んだもの)
  は **すべて購入品** として「ショップ購入」欄に出し、「入手」欄には出さない (CLAUDE.md §6「ショップで買ったカードが二重表示されない」)。
  カードの除去サービスは「デッキ改造 / 除去」に出る。
- 値段: その階に同じ品物の `item_purchased` (同じプレイヤー) があれば `NNNG` を付ける。
  ゲームの購入処理は全員の PC で実行されるので (v0.113.0)、マルチプレイの相手の購入品にも値段が付く。
  同じ品物の `item_purchased` が無い購入品は値段なしで出す。

---

### プレイヤーの最終状態 (ラン全体の画面)

- デッキ・レリック・ポーション: 最後の `floor_snapshot` の `players[].deck` / `relics` / `potions` (その階を出た時点。ラン終了時は終了時点)。セーブデータの履歴から取り込んだゲームは、最後の階の記録にだけデッキ・レリック・ポーションがある (data-sources.md「セーブデータの履歴の取り込み」)
- 最終 HP / ゴールド: 「HP / ゴールド」 の最後の階の `hp_out` / `gold_out`
- バッジ: `run_end.badges[player_id]`。`run_end` が無い (ラン中) ときは出さない

## 既知の制約

- 階ごとの記録 (`floor_snapshot`) が無いゲームは表示できない。そうした古いゲームは、マイグレーション 005 でサーバから消した
- **Event の選択肢**はタイトルだけ。結果 (HP・カード・レリック等) は同じ階の各欄に出る
- **ライブ表示**: 今いる階の内容は送信の区切り (ターン終了・戦闘終了・報酬・購入・休憩所・イベント選択) ごとに更新される
