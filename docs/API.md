# API契約

mod ⇄ バックエンド ⇄ WebUI の HTTP API 仕様。**現在実装されている API のみを記述する**。将来追加予定のエンドポイント・event_type は `ROADMAP.md` 側に記載し、このドキュメントには含めない。

実装変更時はまずこのドキュメントを更新し、それから mod / backend / WebUI のコードを揃える。

---

## 認証モデル

| 操作 | 認可 |
|------|------|
| セッション作成 (`POST /sessions`) | 不要 |
| 書き込み（events 投稿） | `Authorization: Bearer <write_token>` 必須 |
| 読み取り（GET 系） | 不要（共有URLを知っている人は閲覧可） |

`write_token` はセッション作成時に1回だけ返却される。mod が保持する。共有URLには含めない。

---

## データモデル概観

サーバ側のテーブル構成は3つ（Phase 3.5 で `turns` テーブルを廃止し `events` に統合）:

| テーブル | 内容 | 量／run（目安） |
|---------|------|----------------|
| `sessions` | run単位のメタ（character, ascension, seed, outcome 等） | 1 |
| `players` | プレイヤーマスタ（steam_id, name） | 累積 |
| `events` | run中に発生したすべてのイベント（戦闘内・外いずれも） | 1〜10万 |

**設計方針**:
- すべての出来事を時系列の event 列として記録（戦闘中の damage_dealt も、戦闘外の card_picked も同じテーブル）
- 戦闘内 event は `(combat_index, turn_number, sequence)` で ordering、戦闘外は NULL
- 集計（戦闘単位サマリ・rDPS・カード別統計）は **クライアント側** または専用集計エンドポイントで導出
- 新統計の追加は基本的にスキーマ変更不要、`event_type` を増やすだけ

---

## エンドポイント一覧

| Method | Path | 認証 | 用途 |
|--------|------|------|------|
| `POST` | `/sessions` | — | セッション作成 |
| `POST` | `/sessions/{id}/events` | Bearer | イベント bulk 投稿（戦闘内・外いずれも） |
| `POST` | `/sessions/{id}/turns` | — | **410 Gone**（旧形式、廃止済） |
| `GET`  | `/api/sessions/{id}` | — | セッション全データ取得（WebUI用） |
| `GET`  | `/s/{id}` | — | SPA HTML 配信 |
| `GET`  | `/assets/*` | — | Vite ビルド成果物 |
| `GET`  | `/healthz` | — | ヘルスチェック |

---

## `POST /sessions`

セッションを作成し、書き込みトークンと共有URLを返す。run 開始メタデータも同時に渡せる。

**Request**
```json
{
  "host_name":     "Nobuhiro",          // optional
  "host_steam_id": "76561199204788207", // optional（mod が起動時に解決可能）
  "character_id":  "IRONCLAD",          // optional（後で run_start イベントで送ってもよい）
  "ascension":     5,                    // optional
  "seed":          "1234567890"          // optional
}
```

**Response 201**
```json
{
  "session_id":  "550e8400-e29b-41d4-a716-446655440000",
  "write_token": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "share_url":   "https://<host>/s/550e8400-e29b-41d4-a716-446655440000"
}
```

---

## `POST /sessions/{id}/events` `[Auth]`

run 中に発生したイベントを **bulk 投稿** する。戦闘内（`card_played`, `damage_dealt`, ...）も戦闘外（`run_start`, `combat_start`, `card_picked`, ...）も同じエンドポイント・同じ shape で送る。

**Headers**
```
Authorization: Bearer <write_token>
Content-Type: application/json
```

**Body**
```json
[
  {
    "event_uuid":   "0190f8c1-a1a1-7c4a-9d1d-aaaaaaaaaaaa",
    "event_type":   "run_start",
    "occurred_at":  "2026-05-05T00:00:00Z",
    "player_id":    "76561199204788207",
    "floor":        0,
    "payload":      { "character_id": "IRONCLAD", "ascension": 5, "seed": "..." }
  },
  {
    "event_uuid":   "0190f8c1-b2b2-7c4a-9d1d-bbbbbbbbbbbb",
    "event_type":   "combat_start",
    "occurred_at":  "2026-05-05T00:00:30Z",
    "floor":        1,
    "combat_index": 1,
    "payload":      { "encounter_id": "CULTIST", "encounter_name": "...", "room_type": "Monster" }
  },
  {
    "event_uuid":   "0190f8c1-c3c3-7c4a-9d1d-cccccccccccc",
    "event_type":   "card_played",
    "occurred_at":  "2026-05-05T00:00:40Z",
    "player_id":    "76561199204788207",
    "floor":        1,
    "combat_index": 1,
    "turn_number":  1,
    "sequence":     0,
    "payload":      { "card_id": "BASH", "card_name": "バッシュ", "card_type": "Attack" }
  },
  {
    "event_uuid":   "0190f8c1-d4d4-7c4a-9d1d-dddddddddddd",
    "event_type":   "damage_dealt",
    "occurred_at":  "2026-05-05T00:00:40Z",
    "player_id":    "76561199204788207",
    "floor":        1,
    "combat_index": 1,
    "turn_number":  1,
    "sequence":     1,
    "payload": {
      "amount":             18,
      "target_creature_id": "monster:1",
      "source_card_id":     "BASH",
      "hit_index":          0,
      "active_on_target":   [{"power_id":"VULNERABLE_POWER","stacks":2,"applier":"76561...B"}],
      "active_on_dealer":   []
    }
  }
]
```

### フィールド定義

#### トップレベル（共通）

| フィールド | 型 | 必須 | 説明 |
|-----------|----|------|------|
| `event_uuid` | string (UUID v4) | ✓ | mod 側で生成、UNIQUE で重複弾く |
| `event_type` | string | ✓ | 後述の event_type 一覧 |
| `occurred_at` | ISO8601 | ✓ | mod ローカル時刻 |
| `player_id` | string | — | イベント主体（システム由来なら省略 or NULL） |
| `floor` | int | — | 発生階層 |
| `combat_index` | int | — | 戦闘番号（戦闘内 event のみ。1始まり） |
| `turn_number` | int | — | ターン番号（戦闘内 event のみ。1始まり） |
| `sequence` | int | — | 同一ターン内の発生順序（0始まり） |
| `payload` | object | ✓ | event_type 別の固有データ |

戦闘内 event は `combat_index` / `turn_number` / `sequence` をすべて set。戦闘外 event はそれらが NULL。

### event_type カタログ（現行実装）

#### run / combat ライフサイクル

| event_type | payload | context |
|-----------|---------|---------|
| `run_start` | `character_id`, `ascension`, `seed` | floor のみ |
| `run_end` | `outcome` (`victory`/`death`/`abandoned`), `final_floor` | floor のみ |
| `combat_start` | `combat_index`, `encounter_id`, `encounter_name`, `room_type` (`Monster`/`Elite`/`Boss`) | floor + combat_index |
| `combat_end` | `combat_index`, `victory` (bool) | floor + combat_index |

#### 戦闘内（turn-scoped）

すべて `combat_index` / `turn_number` / `sequence` を持つ。

| event_type | payload | player_id |
|-----------|---------|-----------|
| `card_played` | `card_id`, `card_name`, `card_type`, `target_creature_id?`, `energy_cost?` | dealer |
| `card_drawn` | `card_id`, `from_hand_draw?` | drawer |
| `damage_dealt` | `amount`, `target_creature_id`, `target_player_id?`, `source_card_id?`, `hit_index`, `active_on_target[]`, `active_on_dealer[]` | dealer |
| `damage_received` | `amount`, `source_creature_id`, `source_card_id?`, `active_on_target[]` | target |
| `block_gained` | `amount`, `source_card_id?`, `from_player?` | receiver |
| `power_changed` | `power_id`, `delta`, `target_creature_id?`, `target_player_id?`, `source_card_id?` | applier |
| `energy_spent` | `amount`, `source_card_id?` | spender |
| `potion_used` | `potion_id`, `target_creature_id?` | user |

#### power snapshot（`active_on_target` / `active_on_dealer` の中身）

```json
[ { "power_id": "VULNERABLE_POWER", "stacks": 2, "applier": "76561...B" } ]
```

`applier` は power を付与したプレイヤーの steam_id。passive / 不明な場合は `null`。Phase 3.5 v1 では `VULNERABLE_POWER` / `POISON_POWER` / `DOOM_POWER` のみを whitelist として記録。

### 冪等性

`event_uuid` UNIQUE。重複 POST は無視（既存レコードは変更しない）。

### Response
- `204 No Content` — 成功
- `401 Unauthorized` — write_token 不正
- `404 Not Found` — session_id 不在
- `400 Bad Request` — payload 不正

---

## `POST /sessions/{id}/turns` （廃止）

旧形式（ターン集計済 payload）。Phase 3.5 で廃止、常に **`410 Gone`** を返す。古い mod クライアントには「mod を更新してください」のメッセージを返す。

---

## `GET /api/sessions/{id}`

WebUI 用の閲覧エンドポイント。セッション全データを返す。

### ETag によるポーリング効率化

サーバはレスポンスに `ETag` ヘッダを付与する。クライアントは次回リクエストで `If-None-Match: <etag>` を送ることで、データに変化がない場合 `304 Not Modified` を受け取れる。

```
GET /api/sessions/abc
→ 200 OK, ETag: "v-7f3a..."

GET /api/sessions/abc, If-None-Match: "v-7f3a..."
→ 304 Not Modified
```

ETag は `(session_id, events の最新 received_at, events 件数, session.outcome, session.finished_at)` から計算される。

### Response 200

**Body**:
```json
{
  "session": {
    "id":            "550e8400-...",
    "created_at":    "2026-05-05T00:00:00Z",
    "host_name":     "Nobuhiro",
    "host_steam_id": "76561199204788207",
    "character_id":  "IRONCLAD",
    "ascension":     5,
    "seed":          "1234567890",
    "outcome":       null,
    "final_floor":   null,
    "finished_at":   null
  },
  "players": [
    { "steam_id": "76561199204788207", "display_name": "Nobuhiro" }
  ],
  "events": [ /* POST /events の各要素 + received_at */ ]
}
```

`events` は `(combat_index, turn_number, sequence, occurred_at, id)` 順（NULL の combat_index は最後）。WebUI はクライアント側で集計・rDPS 算出・タイムライン構築を行う。

---

## `GET /healthz`

**Response 200** `ok`

---

## バージョニング方針

このドキュメントは常に「現在実装されている API」を表す。Phase 3.5 で `POST /turns` を破壊的に廃止した。今後は後方互換な追加（フィールド追加・新エンドポイント・新 event_type）を基本とし、破壊的変更時は本セクションに履歴を追加する。

### バージョン履歴

| 時期 | 変更 |
|------|------|
| Phase 3.5 | `POST /turns` 廃止（410 Gone）、`turns` テーブル削除、`events` テーブルに combat_index / turn_number / sequence 追加。すべての出来事を `events` 1 表に統合 |
| Phase 2 | 初版 |

---

## 関連ドキュメント

- `STATS_DESIGN.md` — 集めている統計項目の設計（表示要件起点）
- `PHASE35_PLAN.md` — このAPIを実装する具体的な実装計画
- `ROADMAP.md` — events カタログの将来追加予定と派生統計のアイデア
