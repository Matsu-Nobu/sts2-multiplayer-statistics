# CLAUDE.md — このリポジトリでの作業ルール

このファイルは Claude (および将来の自分) が改修するときに **必ず守るルール**を定義する。
逸脱した実装は即マージしない。逸脱を検知したらすぐ仕様 / 実装どちらかを直す。

---

## 0. 大前提

このリポジトリは「mod (`mod/`) → backend (`backend/`) → web (`web/`)」の 3 層構成。
ユーザに見える挙動は基本的に web で表現される。実装のどこをいじっても、
最終的に **ユーザに表示される spec を満たしているか**で良し悪しを判定する。

仕様と実装の二重管理を避けるため、**spec を一次情報** として扱う:

- `docs/spec/combat-stats.md` — 戦闘統計画面 UI の期待挙動
- `docs/spec/run-overview.md` — ラン全体統計画面 UI の期待挙動
- `docs/spec/data-sources.md` — UI ↔ event_type ↔ mod hook の正規経路マップ
- `docs/api.md` — HTTP / event_type 契約
- `docs/architecture.md` — アーキテクチャ全体像

---

## 1. 改修フロー (必須)

ユーザに何か改修を依頼されたら、以下の順序を **必ず**踏む。

### Step 1: 該当 spec を読む
- 改修対象の挙動が `docs/spec/*.md` のどこに書いてあるか特定する。
- 書いてなければ「spec に無い挙動」を新規追加するということ。Step 2 で先に書く。

### Step 2: 仕様を更新する (実装より先)
- 期待挙動を `docs/spec/*.md` に **コードを書く前に** 反映する。
- 「何を出すか」「どの canonical path から取るか」「rarity / upgraded 等の表示ルール」を明文化。
- API スキーマが変わるなら同時に `docs/api.md` を更新。
- 影響を受ける `docs/spec/data-sources.md` (canonical path) も更新。

### Step 3: 実装する
- spec に書いた通りに mod / backend / web を実装。
- spec と実装の差分を作らない。実装中に「やっぱ spec のこの部分を変えたい」と思ったら、
  実装を進める前に Step 2 に戻って spec を更新する。

### Step 4: 自分で検証する (ユーザに見せる前に)
- spec の各 bullet を頭から順に **実データで** 確認:
  - 開発サーバ (`make web-dev` 等) で実際にブラウザで開く、または
  - 既存の本番セッション URL を `curl` で叩いて events を `python3` で集計して期待値と一致するか確認
- 「ビルド通った」「テスト緑」だけでは検証完了とみなさない。**ユーザの目に映る画面 / 値**を見るところまでがセット。
- 検証で漏れに気付いたら Step 3 に戻って直す。**ユーザに見せる前に。**

### Step 5: 報告する
- 何を変えたかと、Step 4 で何を確認したかを 1〜2 文で。
- 確認しきれてない部分があるなら正直に明示する (「○○は実機で再起動が要るので未確認」等)。

---

## 2. mod 側の絶対ルール

### 2.1 reflection で patch するときは必ず**デコンパイルでシグネチャを確認する**

過去にこの過ちで何度もユーザに迷惑をかけた:
- `MapPointHistoryEntry.GetEntry(string)` だと思って patch → 実際は `(ulong)` で常に null → silent に空文字を返してた
- `Hook.OnUpgrade` を信用 → +1 カード報酬の生成時にも発火する偽陽性

手順:
1. STS2 の DLL をデコンパイル (例: `~/.dotnet/tools/ilspycmd <STS2.dll> -o /tmp/sts2_dec`)
2. patch 対象メソッドの本物のシグネチャ / 引数型 / 呼び出し元 / 内部実装を **読む**
3. それを踏まえて mod 側コードを書く
4. reflection の `?.GetMethod()` 結果を検証せずに silent fallback で `""` を返すような書き方をしない
   (silent failure はユーザを最も怒らせる)

### 2.2 Canonical path 厳守

`docs/spec/data-sources.md` の §1.4 (Canonical) に列挙したものが各データの **唯一の正規経路**。
他の経路から二重に拾うと dedup が必要になり、データ抜けと誤計上の温床になる:

| 何 | canonical |
|---|---|
| カード追加 | `CardModel.FloorAddedToDeck` setter |
| カードアップグレード | `CardCmd.Upgrade(IEnumerable<CardModel>, CardPreviewStyle)` |
| レリック取得 | `RelicCmd.Obtain(RelicModel, Player, int)` |
| 鍛治の存在 | `Hook.AfterRestSiteSmith` (option=smith のフラグだけ。card 自体は CardCmd.Upgrade) |

新しい canonical path を確立したら **`docs/spec/data-sources.md` を更新する**。
canonical 化に伴って廃止した patch も §4「削除済み / 廃止された経路」に履歴として残す。

### 2.3 mod を書き換えたら build + install を一度に

```bash
(cd mod && ./build.sh && ./install.sh)
```

ユーザに「STS2 を再起動して」と言うときは、本当に install まで完了している場合に限る。
build しただけ / install パス間違いの状態で再起動を依頼しない。

### 2.4 ユーザは何度も再起動している

ユーザが「再起動した」と言っているのに同じ症状が出ているなら、それは mod 側 patch の不具合。
「再起動してください」を 2 回以上言ってはいけない。デコンパイルに戻ってシグネチャから再確認する。

---

## 3. web 側の絶対ルール

### 3.1 同じデータを 2 経路から表示しない

`reward_taken` の card_id と `card_obtained` event の両方から `cards_obtained` に push、のような
書き方は **必ず**重複表示を生む。spec の `data-sources.md` のマッピング表に従って 1 経路に絞る。

二重登録を避けられない構造的事情がある場合 (例: shop 購入の card は `item_purchased` と
`card_obtained` 両方で観測される) は、**最終 pass で dedup**する。dedup ロジックは
`runOverview.ts` の最終 for ループのような明示的な集約フェーズに集める。

### 3.2 UI の見た目ルールに従う

`docs/spec/run-overview.md` §2.5 の chip 表記ルール (rarity 色 / upgrade 文字色 / skip 表記)
は守る。勝手に独自 UI 表現 (g 略号、右寄せ tabular カラム、勝手な日本語ラベル等) を
混ぜない。spec に無い表示は spec を先に更新してから入れる。

### 3.3 spec 上「セクションが空のときは非表示」が原則

`hasAcq / hasDeckMod / hasShop / hasChoice` のような `$derived` で空 group は丸ごと出さない。
全部空のときは「変化なし」プレースホルダ。

### 3.4 「テスト書く」より先に「自分で動かす」

build エラーを潰した = 動作確認、ではない。`pnpm dev` か `make` で開発サーバ起動して、
実際にブラウザで開いて、開発者ツールでエラーが出てないか / spec 通りの DOM が出てるか見る。

---

## 4. backend 側のルール

### 4.1 SQL を書き換えたら必ず手で叩いて確認

`backend/internal/store/*.go` を変えたら、新クエリを `sqlite3` で本番 DB スナップショットや
ローカル fixture に対して実行して、想定通りの行が返るか確認する。

### 4.2 後方互換は基本不要

ユーザが「古いセッションのデータは消していい」と何度も明言している。
スキーマ変更や event_type 構造変更で、互換シムを残す必要はない。
ただし破壊的変更は `docs/api.md` を更新してから入れる。

---

## 5. ドキュメント更新ルール

### 5.1 spec を更新した時に同時に直すべきもの

- `docs/api.md` (event_type の payload に項目追加 / 削除した時)
- `docs/spec/data-sources.md` (canonical path や section 対応の更新)
- `docs/architecture.md` (アーキテクチャ的な変更)
- 関連ファイルのコードコメント中の `docs/...md` への参照

### 5.2 古い記述を残さない

実装が変わったら spec も同じコミットで直す。
「後で直す」コメントは残さない。直さないなら spec を更新しない理由がない。

### 5.3 archive/ に置いたものは更新しない

歴史的経緯を保存する場所。新しい情報を書き足さない。
新規ドキュメントは `docs/` 直下か `docs/spec/` に書く。

---

## 6. リグレッション検出 (今後整備予定)

現時点では未整備。spec を更新する都度、**少なくとも以下を手動で確認**する:

| spec 項目 | 検証方法 |
|---|---|
| run-overview の HP グラフが floor 1 から始まる | 実セッション URL を開いて目視 |
| カード rarity の色分け | 同上、Common / Uncommon / Rare のカードがあるセッションで |
| 鍛治アップグレードが反映 | rest_action(smith) を含むセッションで |
| 宝箱レリックが表示 | Treasure floor を含むセッションで |
| ショップで買ったカードが二重表示されない | shop_purchases あり / 同じカードが cards_obtained にも乗っているセッションで |
| MP で host 自身の events が単一プレイヤーに統合 | host_steam_id != "1" の MP セッションで |
| カード chip ホバーで description tooltip 表示 | カード / レリック / ポーション / エンチャント chip 全部 |
| catalog の `[gold]X[/gold]` 等タグが色付け span に変換される | tooltip 描画見て確認 |
| 解決不能な `{...}` placeholder が `XX` 表示になる | Stomp / Body Slam 等の chip ホバーで確認 |

将来 E2E (playwright + fixture sessions) を入れたら、ここに mapping して
`make verify` 一発で全部確認できるようにする。

---

## 7. ユーザとのコミュニケーション

- 「直しました」「OK です」と言う前に、Step 4 (自分で検証) を済ませる
- 検証できない部分は明示する (例: 「mod は build + install 済、ゲーム再起動後に確認可能」)
- 「STS2 を再起動してください」は **canonical path に沿って正しく実装したと自信があるとき**だけ。
  推測で再起動を依頼しない
- 同じバグの修正を 2 回失敗したら、デコンパイル / ログ / 実データを腰を据えて読み直す
- ユーザが怒っているサインを見たら、その場しのぎの「とりあえず deploy」は止める。
  根本原因を仕様レベルで特定する
