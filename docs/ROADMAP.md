# ロードマップ

将来構想と段階的拡張計画。Phase 2 は最小（戦闘データの統計表示のみ）に絞り、ROADMAP の項目はそれ以降で順次着手する。

---

## 全体方針

「ゲーム中で発生するあらゆるイベントを記録すれば、後から自然に統計が導出できる」設計を Phase 2 から仕込む。

- **戦闘中の高頻度データ**: `turns` テーブル（per-turn 集計済み）
- **戦闘外の discrete event**: `events` テーブル（raw event ログ）
- **集計**: SQLite JSON1 関数による SQL クエリで導出。新しい統計の追加でスキーマ変更が要らない

新しい event_type を増やすには:
1. mod に新 hook 追加 → イベント emit
2. WebUI に表示パネル追加（必要なら）
3. サーバ・DBスキーマは無変更

---

## Phase 別スコープ

### Phase 2（直近）— 戦闘データの統計表示まで

**目的**: 1ラン中の戦闘統計をブラウザから閲覧できるようにする。

実装範囲:
- バックエンド scaffolding（4テーブル + 認証 + 冪等性）
- mod の HTTP送信化（turns + events最小）
- WebUI の最小実装（戦闘単位のグラフ・テーブル）
- 送信される event_type: **`run_start`** / **`run_end`** のみ

到達点: 共有URLを開くと、当該run内の全戦闘の per-turn 統計が見える。

### Phase 3 — WebUI 本実装

`STATS_DESIGN.md` の表示要件を満たす:
- ターン推移折れ線（与ダメ）
- 戦闘単位棒グラフ（与ダメ・被ダメ・シールド）
- カード別ダメージテーブル
- デバフ付与テーブル
- 貢献スコアグラフ（換算係数 UI 調整）
- ランキング（最大単発・カード別累計等）

### Phase 4 — クロスセッション統計（プレイヤー単位）

mod 側で event 収集を拡張:

| event_type | 必要な hook（要調査） |
|-----------|--------------------|
| `card_picked` | カード報酬選択時 |
| `card_skipped` | スキップ時 |
| `card_added` | デッキ追加（報酬/ショップ/イベント） |
| `card_removed` | デッキ除去 |
| `card_upgraded` | アップグレード |
| `card_transformed` | 変容 |
| `relic_gained` | レリック獲得 |
| `potion_obtained` | ポーション獲得 |
| `node_entered` | マップノード到達 |
| `event_chosen` | イベント選択肢 |
| `shop_purchase` | ショップ購入 |
| `gold_changed` | ゴールド増減 |
| `combat_start` | 戦闘開始（encounter情報付き） |

サーバ側で集計エンドポイント追加:
- `GET /api/players/{steam_id}` — プレイヤー横断統計
- `GET /api/players/{steam_id}/runs` — ラン履歴
- `GET /api/cards/{card_id}` — カード横断（pick率・勝率）
- `GET /api/leaderboard` — 集計ランキング

WebUI 拡張:
- プレイヤー詳細ページ（勝率・キャラ別成績・カード採用傾向）
- カード詳細ページ（pick率・勝率・採用デッキ例）

### Phase 5 — 公開・運用

- 独自ドメイン取得・本番デプロイ
- mod デフォルト URL を本番に切替
- README にセルフホスト手順
- rate limit / データ保持期限実装
- 詳細は `OPERATIONS.md` 参照

---

## 派生統計のアイデア（events から SQL で導出可能）

これらは `events`/`turns`/`sessions` テーブルが揃えば SQL で書ける。具体実装は WebUI 設計時に詰める。

### プレイヤー単位
- ラン勝率（全体・キャラ別・Ascension別）
- 平均到達階層
- 平均与ダメージ／戦闘
- 平均ターン数／戦闘
- ボス戦勝率
- 最大単発ダメージ歴代記録
- お気に入りカード（プレイ回数累計）
- お気に入りレリック（採用率）

### カード単位
- pick 率（提示された回数中の選択率）
- 採用デッキ勝率
- カード別平均ダメージ／プレイ
- アップグレード率
- 戦闘終盤での生存率

### レリック単位
- 入手率（提示中の選択率）
- 採用runの勝率

### キャラクター単位
- Ascension別勝率
- 平均クリアターン数
- ボス別勝率

### マップ・ルート単位
- ノード種別別の選択率
- イベント選択肢別の結果（勝率影響）

### マルチプレイ
- ペア・トリオの勝率
- プレイヤー間ダメージ比率

---

## 関連ドキュメント

- `STATS_DESIGN.md` — 表示要件と統計指標の定義
- `API.md` — 現行 API 仕様
- `PHASE2_PLAN.md` — Phase 2 実装計画
- `OPERATIONS.md` — 運用・配布方針
