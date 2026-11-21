# ロードマップ

将来構想と段階的拡張計画。Phase 2 は最小（戦闘データの統計表示のみ）に絞り、roadmap の項目はそれ以降で順次着手する。

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
- バックエンド scaffolding（4テーブル + 認証 + 冪等性 + ETag）
- mod の HTTP送信化（turns + events）
- WebUI の最小実装（戦闘単位のグラフ・テーブル）
- 送信される event_type: **`run_start`** / **`run_end`** / **`combat_start`** / **`combat_end`**

到達点: 共有URLを開くと、当該run内の全戦闘の per-turn 統計が見える。

### Phase 3 — WebUI 本実装

`spec/combat-stats.md` の表示要件を満たす:
- ターン推移折れ線（与ダメ）
- 戦闘単位棒グラフ（与ダメ・被ダメ・シールド）
- カード別ダメージテーブル
- デバフ付与テーブル
- ランキング（最大単発・カード別累計等）

> 貢献スコア（rDPS 相当）は集計値ベースでは正確な算定ができないため、Phase 3.5 の API 改修後に追加する。

### Phase 3.x — 間接ダメージ（オーブ・毒等）のソース帰属 ✅ 実装済み

実装完了内容:
- `DamageSourceContext` (AsyncLocal) で間接ダメ source を伝搬
- Poison / Doom / Lightning (Evoke 手動・自動・Passive) を Harmony patch で帰属
- Thorns / Flame Barrier の反射ダメも source 識別
- パワー由来ブロック (Rampart / BlockNextTurn) は `BlockSourceContext` で帰属
- 複数 applier の stacks 加重配分（rDPS / rMit）

以下、当初の設計メモは履歴として残す。



**現状の問題**: `AfterDamageGiven` は `cardSource: CardModel?` しか持たないため、
- Defect の Lightning Orb の Evoke / Passive ダメージ
- Poison Power の tick ダメージ
- Burn の自傷ダメージ

これらが全て `(indirect)` という一つのバケツに入る。WebUI のカード別テーブルで「Lightning Orb で何ダメ」「Poison で何ダメ」が見えない。

**解決方針**: STS1 の DamageTracker mod 等で使われている **AsyncLocal によるソースコンテキスト伝搬**を実装する。

- `OrbModel.Evoke()` / `OrbModel.Passive()` を Harmony Prefix/Postfix patch
- `AsyncLocal<OrbModel?>` に「現在トリガ中のオーブ」を一時設定
- `AfterDamageGiven` で `cardSource == null` のとき AsyncLocal を見て「Lightning Orb (Evoke)」等の擬似 CardInfo を作る
- 同様に `PoisonPower.OnSideTurnStart` 等を patch して poison ソースを伝搬

**注意**: 非同期 Task メソッドへの patch は `__state` での対応が必要。Burn / Doom 等の他間接ソースも同パターン。

参考:
- [BAIGUANGMEI/STS2-DamageTracker](https://github.com/BAIGUANGMEI/STS2-DamageTracker) — Poison/Doom 帰属に AsyncLocal を使うパターンが mod 設計時の参考になる

### Phase 3.5 — ターン送信のイベント列化（rDPS 対応への布石）

**動機**: 「貢献度スコア（rDPS 相当）」を正確に算出するには、各ダメージイベントの発生時に「適用されていたバフ・デバフと、それぞれを誰が付与したか」のコンテキストが必要。現在の `POST /sessions/{id}/turns` は **ターン単位の集計値** しか送っておらず、ダメージ単位の attribution は不可能。

**方針**: 既存の events テーブルに新 event_type（`damage_dealt` 等）を追加するのではなく、**ターン投稿の API 設計自体を「ターン中に発生したイベント列を送る」形に変える**。Phase 2 で確立する集計済み `turn` payload は廃止または併存させ、生イベント列（`card_played`, `damage_dealt`, `block_gained`, `power_changed`, `energy_spent`, `card_drawn`, `potion_used` 等）を1ターンぶん bulk で送る。

集計はサーバ側 SQL（or 受信時計算）で導出する形に切り替え、WebUI は集計済みデータと raw event の両方にアクセス可能になる。

**対応する作業**:
- mod 側: `StatsCollector` をイベントバッファに置き換え、ターン終了時に `POST /sessions/{id}/turns/{n}/events` 等で bulk 送信
- backend: events テーブルを拡張、または turn-events 用のテーブルを別途作る
- WebUI: 戦闘単位サマリは集計クエリ経由で取得、貢献度（rDPS）が初めて正確に算出可能になる
- api.md / 関連 spec / roadmap の整合更新

**rDPS の算定**: 各 `damage_dealt` イベントに発火時の active power と applier を埋め込めば、Skada 系 mod や FFXIV FFLogs と同じ二段階（additive + multiplicative）の貢献度計算ができる。Phase 2 段階で諸々妥協していた「貢献度スコア」はここで初めて実装する。

参考:
- [Skada Damage Meter for STS2](https://www.nexusmods.com/slaythespire2/mods/33) — closed-source だが rDPS 二段階アルゴリズム
- [FFLogs rDPS Guide](https://www.fflogs.com/help/rdps) — 元ネタの定式化

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
- 詳細は `operations.md` 参照

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

- `spec/combat-stats.md` — 戦闘統計画面の表示仕様
- `spec/run-overview.md` — ラン全体統計画面の表示仕様
- `spec/data-sources.md` — UI ↔ event_type ↔ mod hook の正規経路マップ
- `api.md` — 現行 API 仕様
- `architecture.md` — システムアーキテクチャ
- `operations.md` — 運用・配布方針
- `archive/phase2-plan.md` — 旧 Phase 2 実装計画 (歴史的経緯)
