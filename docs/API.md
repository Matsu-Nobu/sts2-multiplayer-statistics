# API契約

mod ⇄ バックエンド ⇄ WebUI の HTTP API 仕様。**この文書は常に最新の状態を保つ**。実装変更時はまずこのドキュメントを更新し、それから mod / backend / WebUI のコードを揃える。

---

## 認証モデル

| 操作 | 認可 |
|------|------|
| セッション作成 (`POST /sessions`) | 不要 |
| 書き込み（turns 投稿） | `Authorization: Bearer <write_token>` 必須 |
| 読み取り（GET 系） | 不要（共有URLを知っている人は閲覧可） |

`write_token` はセッション作成時に1回だけ返却される。mod が保持する。共有URLには含めない。

---

## エンドポイント一覧

| Method | Path | 認証 | 用途 |
|--------|------|------|------|
| `POST` | `/sessions` | — | セッション作成 |
| `POST` | `/sessions/{id}/turns` | Bearer | ターンデータ投稿（戦闘終了時も同じ） |
| `GET`  | `/api/sessions/{id}` | — | セッション全データ取得（WebUI用） |
| `GET`  | `/s/{id}` | — | SPA HTML 配信 |
| `GET`  | `/assets/*` | — | Vite ビルド成果物 |
| `GET`  | `/healthz` | — | ヘルスチェック |

---

## `POST /sessions`

セッションを作成し、書き込みトークンと共有URLを返す。

**Request**
```json
{
  "host_name": "Nobuhiro"     // optional
}
```

**Response 201**
```json
{
  "session_id": "550e8400-e29b-41d4-a716-446655440000",
  "write_token": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "share_url":   "https://<host>/s/550e8400-e29b-41d4-a716-446655440000"
}
```

---

## `POST /sessions/{id}/turns` `[Auth]`

ターン終了時／戦闘終了時に mod が送信する。1ペイロードに **そのターンの delta**・**ターン内カード別集計**・**戦闘開始からの累計** をすべて含む。

**Headers**
```
Authorization: Bearer <write_token>
Content-Type: application/json
```

**Body**
```json
{
  "combat_index": 2,
  "turn_number":  5,
  "is_final":     false,
  "timestamp":    "2026-05-05T00:00:00Z",
  "players": {
    "76561199204788207": {
      "player_name": "Nobuhiro",

      "turn": {
        "damage_dealt":       30,
        "damage_received":    8,
        "block_gained_self":  0,
        "block_given_allies": 0,
        "energy_used":        3,
        "cards_played":       3,
        "cards_drawn":        5,
        "cards": [
          {
            "card_id":         "SQUASH",
            "card_name":       "踏み潰し",
            "card_type":       "Attack",
            "play_count":      2,
            "damage_dealt":    18,
            "block_provided":  0,
            "debuffs_applied": {},
            "max_single_hit":  10
          },
          {
            "card_id":         "DAGGER_THROW",
            "card_name":       "ダガースロー",
            "card_type":       "Attack",
            "play_count":      1,
            "damage_dealt":    9,
            "block_provided":  0,
            "debuffs_applied": {},
            "max_single_hit":  9
          }
        ]
      },

      "combat": {
        "damage_dealt":       180,
        "damage_received":    45,
        "block_gained_self":  110,
        "block_given_allies": 15,
        "energy_used":        15,
        "cards_played":       24,
        "cards_drawn":        38,
        "potions_used":       1,
        "max_single_hit":     24,
        "debuffs_applied":    { "Poison": 18, "Vulnerable": 4 },
        "card_stats": [
          {
            "card_id":         "SQUASH",
            "card_name":       "踏み潰し",
            "card_type":       "Attack",
            "play_count":      4,
            "damage_dealt":    36,
            "block_provided":  0,
            "debuffs_applied": {},
            "max_single_hit":  10
          }
        ]
      }
    }
  }
}
```

### フィールド定義

#### トップレベル
| フィールド | 型 | 説明 |
|-----------|----|------|
| `combat_index` | int | 1始まり。run中の何戦目か |
| `turn_number`  | int | 1始まり。combat内のターン番号 |
| `is_final`     | bool | 戦闘終了に伴う最終送信なら `true` |
| `timestamp`    | ISO8601 string | mod 側のローカル送信時刻 |
| `players`      | map<player_id, PlayerData> | プレイヤーIDは Steam ID（NetId）。シングル時はローカルSteam ID |

#### `players[id].turn`（このターンの delta）
| フィールド | 型 | 単位 |
|-----------|----|------|
| `damage_dealt` | int | このターンに与えたダメージ合計 |
| `damage_received` | int | このターンに受けたダメージ（ブロック貫通分） |
| `block_gained_self` | int | このターンに自分が獲得したブロック |
| `block_given_allies` | int | このターンに味方に付与したブロック |
| `energy_used` | int | このターンに使ったエナジー |
| `cards_played` | int | プレイ枚数 |
| `cards_drawn` | int | ドロー枚数 |
| `cards` | CardStats[] | このターン内のカード別集計（後述） |

#### `players[id].combat`（戦闘開始からの累計）
| フィールド | 型 | 説明 |
|-----------|----|------|
| 上記 turn と同名のスカラー | int | 戦闘開始から現在までの累計 |
| `potions_used` | int | このコンバットで使ったポーション数 |
| `max_single_hit` | int | この戦闘で出した最大単発ダメージ |
| `debuffs_applied` | map<power_id, int> | 種別別デバフ累計付与スタック数 |
| `card_stats` | CardStats[] | 戦闘累計のカード別集計 |

#### CardStats
| フィールド | 型 |
|-----------|----|
| `card_id` | string（`(indirect)` は毒等の間接ダメージを表す予約値） |
| `card_name` | string（mod 側でローカライズ済み） |
| `card_type` | string (`Attack` / `Skill` / `Power` / `Status` / `Curse` / `(indirect)`) |
| `play_count` | int |
| `damage_dealt` | int |
| `block_provided` | int |
| `debuffs_applied` | map<power_id, int> |
| `max_single_hit` | int |

### 冪等性

`(session_id, combat_index, turn_number)` を主キーとして上書き保存する。同一キーで複数回POSTされても破壊しない（mod 側のリトライ・誤送信対策）。

### Response
- `204 No Content` — 成功
- `401 Unauthorized` — write_token 不正
- `404 Not Found` — session_id 不在
- `400 Bad Request` — payload 不正

---

## `GET /api/sessions/{id}`

WebUI 用の閲覧エンドポイント。セッション全データを返す。集計はクライアント側で行う。

**Response 200**
```json
{
  "session": {
    "id":         "550e8400-...",
    "created_at": "2026-05-05T00:00:00Z",
    "host_name":  "Nobuhiro"
  },
  "turns": [
    { /* POST /sessions/{id}/turns の Body と同じ形 */ },
    ...
  ]
}
```

`turns` は `(combat_index, turn_number)` 昇順。

---

## `GET /healthz`

**Response 200** `ok`

---

## バージョニング方針

破壊的変更が必要になった時点でこのドキュメントの先頭に `## バージョン履歴` を追加し、互換性のあるフィールドは追加で対応する（クライアントは未知フィールドを無視する想定）。当面は v1 暗黙、ヘッダ等でのバージョン明示はしない。

---

## 関連ドキュメント

- `STATS_DESIGN.md` — 集めている統計項目の設計（表示要件起点）
- `PHASE2_PLAN.md` — このAPIを実装する具体的な実装計画
