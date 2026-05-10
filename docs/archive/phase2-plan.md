# Phase 2 + 3 実装計画（バックエンド・WebUI）

mod 側の Phase 1.5 完了後の作業計画。バックエンドサーバ・Web閲覧UI・mod側のHTTP送信化をまとめて扱う。

---

## 1. 確定事項

| 項目 | 決定 |
|------|------|
| バックエンド言語 | Go |
| DB | SQLite（`modernc.org/sqlite` — cgo不要） |
| HTTPルータ | `chi` |
| フロント | Vite + Svelte + TypeScript |
| グラフライブラリ | Chart.js（`svelte-chartjs`） |
| 更新方式 | 10秒 setInterval ポーリング（v1） |
| 同梱方式 | フロント `dist/` を Go の `embed` で単一バイナリ化 |
| デプロイ | 未決（実装完了後に決定） |
| セッション粒度 | 1ラン = 1セッション |
| 書き込み保護 | `write_token` Bearer 認証 |
| ホスト判定 | しない（mod を入れるのは1人前提） |
| API構成 | **単一エンドポイント** `POST /sessions/{id}/turns`（delta + 累計を同梱） |

### API単一化の根拠

`card_stats` `debuffs_applied` 等の戦闘単位データは元々「累計」。任意のターン時点でのスナップショットが取れる。よって combat_end 専用エンドポイントは不要。最後のターンが送られない問題は `AfterCombatEnd` でも同じエンドポイントに `is_final: true` で送ることで解決する。

---

## 2. ディレクトリ構成

```
sts2-multiplayer-statistics/
├── backend/                       # 新規
│   ├── go.mod / go.sum
│   ├── cmd/server/main.go
│   ├── internal/
│   │   ├── api/                   # ハンドラ
│   │   ├── store/                 # SQLite アクセス
│   │   ├── auth/                  # write_token 検証ミドルウェア
│   │   └── model/                 # DTO
│   ├── migrations/001_init.sql
│   ├── web/dist/                  # Vite build 成果物（gitignore、embed対象）
│   ├── Dockerfile
│   └── README.md
├── web/                           # 新規（フロント）
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   ├── index.html
│   └── src/
│       ├── main.ts
│       ├── App.svelte
│       ├── routes/Session.svelte
│       ├── lib/{api.ts, types.ts, poll.ts}
│       └── components/*.svelte
├── mod/                           # 既存（修正あり）
├── mod.tests/                     # 既存（修正あり）
└── Makefile                       # backend + frontend ビルド統合
```

---

## 3. API契約

API仕様は **[`API.md`](./API.md)** に分離している。実装変更時はそちらを先に更新すること。

サマリ:
- `POST /sessions` — セッション作成、run メタ（character/ascension/seed）も同梱可能
- `POST /sessions/{id}/turns` `[Auth]` — ターン終了/戦闘終了時に delta + ターン内カード別 + 戦闘累計 を一括投稿
- `POST /sessions/{id}/events` `[Auth]` — 戦闘外 discrete イベント（run_start, run_end 等）の bulk 投稿
- `GET /api/sessions/{id}` — WebUI 用の全データ取得
- `GET /s/{id}` / `GET /assets/*` / `GET /healthz`

---

## 4. DBスキーマ

```sql
CREATE TABLE sessions (
  id              TEXT PRIMARY KEY,        -- UUID v4
  created_at      TEXT NOT NULL,
  host_name       TEXT,
  host_steam_id   TEXT,                    -- run のホストプレイヤー
  character_id    TEXT,                    -- "IRONCLAD" 等
  ascension       INTEGER,
  seed            TEXT,
  outcome         TEXT,                    -- 'victory' / 'death' / 'abandoned' / NULL
  final_floor     INTEGER,
  finished_at     TEXT,
  write_token     TEXT NOT NULL
);

CREATE TABLE players (
  steam_id      TEXT PRIMARY KEY,
  display_name  TEXT NOT NULL,
  first_seen_at TEXT NOT NULL,
  last_seen_at  TEXT NOT NULL
);

CREATE TABLE turns (
  session_id    TEXT NOT NULL,
  combat_index  INTEGER NOT NULL,
  turn_number   INTEGER NOT NULL,
  received_at   TEXT NOT NULL,
  is_final      INTEGER NOT NULL DEFAULT 0,
  payload_json  TEXT NOT NULL,
  PRIMARY KEY (session_id, combat_index, turn_number),
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX idx_turns_session ON turns(session_id, combat_index, turn_number);

CREATE TABLE events (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  event_uuid   TEXT NOT NULL UNIQUE,       -- mod 生成、冪等性キー
  session_id   TEXT NOT NULL,
  player_id    TEXT,
  event_type   TEXT NOT NULL,
  occurred_at  TEXT NOT NULL,
  received_at  TEXT NOT NULL,
  floor        INTEGER,
  payload_json TEXT NOT NULL,
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX idx_events_session ON events(session_id, id);
CREATE INDEX idx_events_player  ON events(player_id, event_type);
CREATE INDEX idx_events_type    ON events(event_type);
```

### 設計方針

- **`turns`**: 戦闘中の高頻度・高密度な集計データ。per-turn snapshot（delta + ターン内カード別 + 戦闘累計）として保存。
- **`events`**: 戦闘外で発生する discrete event。`event_type` + `payload_json` の柔軟な形式で、新統計を追加する際もスキーマ変更不要。
- **`players`**: turns/events 投稿時に upsert。「last_seen を最新に・display_name を最新に」のシンプルなロジック。
- **`sessions`**: run 開始時にメタデータを記録、終了時に `outcome`/`final_floor`/`finished_at` を更新。

サーバは生 payload を保存・配信が基本。集計はクライアント or 集計用エンドポイント（Phase 4 以降）で行う。

---

## 5. mod 側の変更

### 5-1. 新規ファイル

- **`SessionManager.cs`**
  - mod 起動時に `POST /sessions` でセッション作成（character/ascension/seed が取れていれば同梱）
  - `session_id` / `write_token` / `share_url` を保持
  - 起動時に `OS.Clipboard` へ URL コピー、`GD.Print` でログ出力
  - 失敗してもゲームは止めない（`StatsLogger` のみで継続）

- **`HttpSender.cs`**
  - `HttpClient` 保持、`SendTurn(payload, isFinal)` / `SendEvents(events[])` を非同期実行
  - 失敗時はインメモリキュー（最大100件）に積み、次回送信前に flush
  - `Authorization: Bearer <write_token>` ヘッダ付与

- **`EventEmitter.cs`**
  - 戦闘外イベントの生成・バッファリング
  - Phase 2 で送るのは `run_start` / `run_end` のみ。それ以外の event_type は Phase 4 以降に hook を追加して順次対応
  - `Guid.NewGuid()` で `event_uuid` を生成して冪等性確保
  - 一定間隔 or `run_end` 到達時に `HttpSender.SendEvents` で flush

### 5-2. StatsCollector の変更

現在 `TurnSnapshot` は per-turn delta のみ、`MutableTurnData` はカウンタしか持っていない。サーバには「**ターン内カード別集計** + **delta合計** + **戦闘累計**」を一括で送るので、それに合わせて型と内部状態を拡張する。

#### 内部状態の追加

- `MutableTurnData` に `Dictionary<string, MutableCardStats> CardStats` を追加（戦闘側と同じ shape を流用）
- `MutableCardStats` は既存型をそのまま再利用（`PlayCount` / `DamageDealt` / `BlockProvided` / `DebuffsApplied` / `MaxSingleHit`）

#### Recordメソッドの修正

`_currentCombat` だけに書いていた箇所で、カード情報を持つものは `_currentTurn` 側にも同じ集計を書く:

- `RecordDamageDealt(...,card)`: `_currentTurn[player].CardStats[card.id]` の `DamageDealt` / `MaxSingleHit` を更新
- `RecordCardPlayed(...,card)`: 同 `PlayCount++`
- `RecordBlockGainedSelf(...,card)` / `RecordBlockGivenToAlly(...,card)`: 同 `BlockProvided`
- `RecordDebuffApplied(...,card)`: 同 `DebuffsApplied[powerId]`

`(indirect)` ダメージ（毒等）も同じく `_currentTurn` 側に積む。

#### 新型

```csharp
internal record TurnPayload(
    int CombatIndex,
    int TurnNumber,
    bool IsFinal,
    DateTime Timestamp,
    Dictionary<string, PlayerTurnAndCombat> Players
);

internal record PlayerTurnAndCombat(
    string PlayerName,
    PlayerTurnStats     Turn,    // delta + ターン内カード別
    PlayerCombatSummary Combat   // 戦闘累計（既存）
);

// PlayerTurnStats を拡張: cards フィールドを追加
internal record PlayerTurnStats(
    string PlayerId,
    string PlayerName,
    int DamageDealt,
    int DamageReceived,
    int BlockGainedSelf,
    int BlockGivenToAllies,
    int EnergyUsed,
    int CardsPlayed,
    int CardsDrawn,
    List<CardStatsSummary> Cards   // ★ 追加
);
```

`CardStatsSummary` は既存のまま（戦闘累計と同じ型を再利用）。

#### 集計関数の修正

- `FinalizeCurrentTurn()`: `_currentTurn` から `PlayerTurnStats`（cards 込み）を作り、同時に `_currentCombat` のスナップショットを `PlayerCombatSummary` に変換して `TurnPayload` を組み立てて返す
- `FinalizeCurrentCombat()` は廃止。`AfterCombatEnd` 時は `FinalizeCurrentTurn(isFinal:true)` 相当を呼ぶ形に統合

### 5-3. HookPatches の変更

- `AfterPlayerTurnStartPostfix`:
  - `FinalizeCurrentTurn()` → `TurnPayload` を取得 → `HttpSender.SendTurn(payload, isFinal:false)` + `StatsLogger.LogTurnEnd(payload)`

- `AfterCombatEndPostfix`:
  - 残っている current turn を `is_final:true` で送信
  - `StatsLogger.LogCombatEnd` は廃止（`LogTurnEnd` で兼用）

### 5-4. ModEntry / 設定

- `.env` に追加: `STS_STATS_BACKEND_URL=http://localhost:8080`
- `ModEntry.Initialize` で env を読み、`SessionManager` → `HttpSender` 初期化

### 5-5. テスト更新

- `StatsCollectorTests` を新DTO（`TurnPayload`）対応に書き換え
- 累計が turn snapshot に含まれることのテスト追加

---

## 6. フロント構成（Phase 3寄り、Phase 2では最小実装）

```
web/src/
├── main.ts
├── App.svelte
├── routes/Session.svelte           # /s/{id}
├── lib/
│   ├── api.ts                       # GET /api/sessions/{id}
│   ├── types.ts                     # サーバJSONと一致するTS型
│   └── poll.ts
└── components/
    ├── PlayerSelector.svelte
    ├── CombatTabs.svelte
    ├── DamagePerTurnChart.svelte
    ├── CombatSummaryBars.svelte
    ├── CardStatsTable.svelte
    ├── DebuffTable.svelte
    └── RankingsPanel.svelte
```

ルーティングは `window.location.pathname` から `session_id` を取り出すだけで済む（専用ルータ不要）。

### dev時のプロキシ設定

```ts
// vite.config.ts
export default {
  server: {
    proxy: { '/api': 'http://localhost:8080' }
  }
}
```

### prodビルド

```bash
cd web && npm run build           # → ../backend/web/dist/
cd backend && go build -o server ./cmd/server
./server                          # 単一バイナリ、:8080
```

---

## 7. ステップ別タスクと完了条件

### ステップ1: バックエンド scaffolding（半日〜1日）
- `go.mod` 初期化、`chi` `modernc.org/sqlite` `google/uuid` 導入
- migrations 適用（`embed.FS` で `001_init.sql` 読込）
- `POST /sessions` / `POST /sessions/{id}/turns` / `GET /api/sessions/{id}` / `GET /healthz` 実装
- write_token Bearer 認証ミドルウェア
- 冪等INSERT（`ON CONFLICT(...) DO UPDATE`）
- **完了条件**: `curl` で全エンドポイント動作。同payload2回POSTで重複なし

### ステップ2: バックエンドテスト（半日）
- `internal/api` ハンドラテスト（`httptest`）
- `internal/store` SQLiteテスト（`:memory:`）
- mod 実出力JSONを `testdata/` に置き fixture 化
- **完了条件**: `go test ./...` 全緑

### ステップ3: mod 側 HttpSender 実装（1日）
- `MutableTurnData` にカード別集計を追加し、各 Record メソッドを turn 側にも反映
- `StatsCollector` の `TurnPayload` 型導入と `FinalizeCurrentTurn` 改造
- `HookPatches` を `TurnPayload` ベースに変更（`AfterCombatEnd` も同エンドポイント）
- `SessionManager` / `HttpSender` / `EventEmitter` 実装
- run start / run end の hook 調査と `events` 投稿（Phase 2 ではこの2種のみ）
- env 経由で `STS_STATS_BACKEND_URL` 切替
- mod テスト更新（ターン内カード集計のテスト追加）
- **完了条件**: ローカル backend に turns が届く。クリップボードに URL がコピーされる。失敗時にゲームが止まらない

### ステップ4: 簡易閲覧ページ（半日）
- Svelte プロジェクト初期化（`npm create vite@latest -- --template svelte-ts`）
- `/s/{id}` ページで `GET /api/sessions/{id}` を fetch、JSONダンプ表示だけ
- 10秒ポーリング
- Go embed で `dist/` 同梱
- **完了条件**: ブラウザで共有URLを開くと最新データが10秒ごとに更新される

### ステップ5: WebUI 本実装（Phase 3、別計画）
- 各種グラフ・テーブル・ランキングの作成
- 優先順位や仕様は STATS_DESIGN.md の表示設計に従う
- 範囲が大きいので別ドキュメント `docs/PHASE3_PLAN.md` を起こして詳細化する

### ステップ6: デプロイ（半日）
- Dockerfile / fly.toml（or 別候補）作成
- volume マウントで SQLite 永続化、HTTPS強制
- `STS_STATS_BACKEND_URL` を本番URLに切替
- **完了条件**: 共有URLが外部から開ける

---

## 8. 開発ワークフロー

### dev時（2プロセス）
```bash
# Terminal 1: backend
cd backend && go run ./cmd/server          # :8080

# Terminal 2: frontend
cd web && npm run dev                      # :5173, /api → :8080 にプロキシ
```
backend は dev 時 `web/dist` 不在でもエラーにしない（embed をオプショナル化）。

### prod ビルド
```bash
make build                                  # web → backend/web/dist/、go build
make run                                    # 単一バイナリ起動
```

Makefile に `make backend-dev` `make web-dev` `make build` `make run` `make test` を追加。

---

## 9. 残論点

1. **デプロイ先**: ステップ6で決定。Fly.io / Railway / Render / Cloud Run / 自前VPSなど候補
2. **WebUIの詳細仕様**: ステップ5を着手する段で `docs/PHASE3_PLAN.md` に分離
3. **失敗時のmod側ログ**: バックエンドが落ちた時の挙動（ログのみで継続でOKだが、UX的にどう通知するか）
4. **将来のセキュリティ強化**: write_token 漏洩時のローテーション機構（v1ではスコープ外）

---

## 10. 進捗チェックリスト

- [ ] ステップ1: バックエンド scaffolding
- [ ] ステップ2: バックエンドテスト
- [ ] ステップ3: mod 側 HttpSender 実装
- [ ] ステップ4: 簡易閲覧ページ
- [ ] ステップ5: WebUI 本実装（別計画）
- [ ] ステップ6: デプロイ
