# API契約

mod ⇄ バックエンド ⇄ WebUI の HTTP API 仕様。**現在実装されている API のみを記述する**。

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
- すべての出来事を時系列の event 列として記録（戦闘中の damage_dealt も、戦闘外の floor_snapshot も同じテーブル）
- 戦闘内 event は `(combat_index, turn_number, sequence)` で ordering、戦闘外は NULL
- 集計（戦闘単位サマリ・rDPS・カード別統計）は **クライアント側** または専用集計エンドポイントで導出
- 新統計の追加は基本的にスキーマ変更不要、`event_type` を増やすだけ

---

## エンドポイント一覧

| Method | Path | 認証 | 用途 |
|--------|------|------|------|
| `POST` | `/sessions` | — | セッション作成 |
| `POST` | `/sessions/{id}/events` | Bearer | イベント bulk 投稿（戦闘内・外いずれも） |
| `GET`  | `/api/sessions/{id}` | — (非公開のランは記録した人のログインが要る) | セッション全データ取得（WebUI用） |
| `GET`  | `/auth/steam/login?return={path}` | — | Steam のログイン画面へ移る |
| `GET`  | `/auth/steam/callback` | — | Steam から戻る。ログインの cookie を付けて `return` へ移る |
| `POST` | `/auth/logout` | — | ログインの cookie を消す |
| `GET`  | `/api/me` | cookie | ログインしている人 |
| `PATCH`| `/api/me` | cookie | ユーザーページの公開・非公開 |
| `GET`  | `/api/users/{steam_id}` | — / cookie | ユーザーページ (参加したランの一覧) |
| `GET`  | `/api/users/{steam_id}/stats` | — / cookie | プレイヤーの統計に使う、ゲームごとの集計 |
| `GET`  | `/api/sessions/{id}/me` | cookie | ログインしている人とそのランの関係 (参加者か・記録した人か・公開か・ページに載せるか) |
| `PATCH`| `/api/sessions/{id}` | cookie (記録した人) | ランの公開・非公開 |
| `PATCH`| `/api/sessions/{id}/listing` | cookie (参加者) | ランを自分のユーザーページに載せるか |
| `DELETE`| `/api/sessions/{id}` | cookie (記録した人) | ランを削除する |
| `GET`  | `/`・`/settings`・`/s/{id}`・`/u/{steam_id}`・`/u/{steam_id}/stats` 以下 | — | SPA HTML 配信 (画面は web が URL で決める) |
| `GET`  | `/assets/*` | — | Vite ビルド成果物 |
| `GET`  | `/catalog.{lang}.json` | — | STS2 カード/レリック/ポーション/エンチャント definitions（静的） |
| `GET`  | `/favicon.ico` | — | ファビコン |
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
  "seed":          "1234567890",         // optional
  "game_version":  "v0.113.1"            // optional（ゲームのフォルダの release_info.json の version）
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

run 中に発生したイベントを **bulk 投稿** する。戦闘内（`card_played`, `damage_dealt`, ...）も戦闘外（`run_start`, `combat_start`, `floor_snapshot`, ...）も同じエンドポイント・同じ shape で送る。

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

### event_type カタログ

`player_id` は全 event で **正規化済み** (シングルプレイ・LAN ホストの `NetId=1` はローカルの Steam ID に置換)。
プレイヤーに属さない event (`combat_start` / `combat_end` / `floor_snapshot` 等) だけ空。

#### run / 階 / combat ライフサイクル

| event_type | payload | player_id | context |
|-----------|---------|-----------|---------|
| `run_start` | `character_id`, `character_name` (表示名), `ascension`, `seed`, `game_mode`, `player_name`, `hp`, `max_hp`, `gold` (ラン開始時点) | 各プレイヤー (人数分送る) | floor のみ |
| `floor_snapshot` | 「floor_snapshot」 | 空 | floor のみ |
| `item_purchased` | `item_kind`, `card_id?`, `card_name?`, `card_rarity?`, `is_upgraded?`, `relic_id?`, `relic_name?`, `potion_id?`, `potion_name?`, `gold_spent` | 購入者 | floor のみ |
| `run_end` | `outcome` (`victory`/`death`/`abandoned`), `final_floor`, `final_hp` (`{ player_id: hp }`), `badges` (`{ player_id: [{ id, name, description, rarity }] }`) | 空 | floor のみ |
| `combat_start` | `combat_index`, `encounter_id`, `encounter_name`, `room_type` (`Monster`/`Elite`/`Boss`) | 空 | floor + combat_index |
| `combat_end` | `combat_index`, `victory` (bool) | 空 | floor + combat_index |

- `combat_index` = 戦闘の階番号。中断→再開で同じ `combat_index` の `combat_start` が再び来たら、
  それより前の同じ `combat_index` の event は無効 (web が捨てる)。
- `run_end` はラン 1 回につき 1 件。`final_hp` は勝利・放棄ではラン終了処理 (全員を倒す) の直前の HP、全滅では 0。
- `badges`: ゲームオーバー画面のバッジ。ゲームと同じ判定 (`ScoreUtility.GetBadges(run, playerId, won)`) をラン終了時に行う。`rarity` は `Bronze` / `Silver` / `Gold`。

#### floor_snapshot

ゲーム自身の階ごとの記録 (`MapPointHistoryEntry`) の写し。同じ `floor` の snapshot は何度でも届く。
**web は階ごとに最後に受け取ったものだけを使う。**

```json
{
  "floor": 2, "act_index": 0, "is_final": true,
  "rooms": [{ "room_type": "Monster", "model_id": "CORPSE_SLUGS_WEAK", "model_name": "屍ナメクジ",
              "monster_ids": ["CORPSE_SLUG", "CORPSE_SLUG"], "turns_taken": 6 }],
  "players": [{
    "player_id": "76561199204788207",
    "hp":   { "current": 44, "max": 70, "damage_taken": 12, "healed": 0, "max_gained": 0, "max_lost": 0 },
    "gold": { "current": 113, "gained": 14, "spent": 0, "lost": 0, "stolen": 0 },
    "cards_gained":      [card],
    "cards_removed":     [card],
    "cards_transformed": [{ "from": card, "to": card }],
    "cards_upgraded":    [card],
    "cards_downgraded":  [card],
    "cards_enchanted":   [{ "card": card, "enchantment_id": "...", "enchantment_name": "..." }],
    "card_choices":      [{ "card": card, "was_picked": true }],
    "relic_choices":     [{ "id": "BOOMING_CONCH", "name": "轟音のほら貝", "rarity": "Ancient", "was_picked": true }],
    "potion_choices":    [{ "id": "ENERGY_POTION", "name": "エナジーポーション", "was_picked": true }],
    "potions_used":      [model], "potions_discarded": [model], "relics_removed": [model],
    "event_choices":     ["轟音のほら貝"],
    "ancient_choices":   [{ "title": "轟音のほら貝", "was_chosen": true }],
    "rest_site_choices": ["SMITH"],
    "bought":            { "relics": [model], "potions": [model], "colorless": [model] },
    "completed_quests":  [model],
    "deck":    [card],                  // その時点のデッキ (Player.Deck)
    "relics":  [{ "id", "name", "rarity" }],
    "potions": [model]
  }]
}
```

- `card` = `{ "id", "name", "rarity", "type", "upgrade_level", "enchantment_id"? }`、`model` = `{ "id", "name" }`。
  名前・レアリティは mod が送信時に `ModelDb` から引く (ゲーム側の記録は ID のみ)。
- `relic_choices` / `potion_choices` の `was_picked: true` が入手、`false` が報酬のスキップ。
- `card_choices` は階ごとに 1 本のリスト (報酬ごとの区切りは無い)。ショップの階では買わなかった商品カードが入る。
- `is_final`: その階を出て確定したもの (`true`) か、ライブ表示用に途中で送ったもの (`false`) か。

#### 戦闘内（turn-scoped）

すべて `combat_index` / `turn_number` / `sequence` を持つ。戦闘中 (`CombatManager.IsInProgress`) だけ送る。

| event_type | payload | player_id |
|-----------|---------|-----------|
| `card_played` | `card_id`, `card_name`, `card_type`, `target_creature_id?` | 使用者 |
| `card_drawn` | `card_id`, `card_name?`, `from_hand_draw?` | ドローした人 |
| `damage_dealt` | `amount` (敵HPに通った分), `total_damage` (ブロック込み), `blocked_damage`, `overkill_damage`, `was_target_killed`, `is_doom_kill`, `source_appliers?`, `target_creature_id`, `source_card_id?`, `source_card_name?`, `source_card_type?`, `source_kind`, `active_on_target[]`, `active_on_dealer[]`, `modifications[]` | 与えた人 (ペットは持ち主。攻撃者が空なら出どころパワーの付与者) |
| `damage_received` | `amount` (自HPに受けた分), `total_damage`, `blocked_damage` (=有効ブロック), `source_creature_id`, `source_card_id?`, `active_on_target[]`, `active_on_dealer[]`, `modifications`, `block_sources[]` | 受けた人 (**致死の一撃を含む**) |
| `block_gained` | `amount`, `source_card_id?`, `source_card_name?`, `source_card_type?`, `source_kind`, `from_player?` | 受けた人 |
| `power_changed` | `power_id`, `power_name?`, `delta`, `target_creature_id?`, `target_player_id?`, `source_card_id?` | 付与者 |
| `energy_spent` | `amount`, `source_card_id?` | 使った人 |
| `potion_used` | `potion_id`, `target_creature_id?` | 使った人 |

`damage_dealt` / `damage_received` はどちらも `Hook.AfterDamageGiven` の 1 か所から作る (受けた側が敵なら前者、プレイヤーなら後者)。

#### 出どころ (`source_card_id` にカードが無いとき)

カード以外 (パワー・レリック・オーブ・エンチャント) が起こしたダメージ・ブロックは、その **モデルの ID** が
`source_card_id` に入る (例: `POISON_POWER`, `DOOM_POWER`, `THORNS_POWER`, `LIGHTNING_ORB`, `BURNING_BLOOD`)。
`source_card_type` にはモデルの種類 (`Power` / `Relic` / `Orb` / `Enchantment` / `Potion`)、`source_card_name` には表示名が入る。
カードかどうかは `source_kind` (`card` / `power` / `relic` / `orb` / `enchantment` / `potion` / `unknown`) で判定する
(`source_card_type` の `Power` はカードの種類「パワー」と同じ文字列なので判定に使わない)。`damage_dealt` / `block_gained` に付く。
mod が起動時に対象メソッドを自動で列挙して追跡する (spec data-sources.md「カードの無いダメージ」)。

#### `modifications` (damage_dealt / damage_received。貢献スコア用、spec combat-stats.md「貢献スコア」)

ゲームの補正計算で値を変えたモデルを、計算順に 1 つずつ並べたもの。

```json
{
  "base": 6,                 // 補正前のダメージ (カードの基礎値)
  "final": 13.5,             // ダメージ補正後 (ブロック前・HP 上限前)
  "steps": [
    { "phase": "additive",       "model_id": "STRENGTH_POWER",   "model_name": "筋力", "kind": "power", "value": 3,
      "appliers": [{ "player_id": "765...A", "stacks": 3 }] },
    { "phase": "multiplicative", "model_id": "VULNERABLE_POWER", "model_name": "弱体", "kind": "power", "value": 1.5,
      "appliers": [{ "player_id": "765...B", "stacks": 2 }] },
    { "phase": "multiplicative", "model_id": "PEN_NIB",          "model_name": "ペン先", "kind": "relic", "value": 2, "owner": "765...A" }
  ]
}
```

- `phase`: `enchant` / `additive` (value = 増減量) / `multiplicative` (value = 倍率) / `cap` (value = 上限で下がった量、負) /
  `hp_lost` (ブロック後の HP 減少補正。value = 変化量。damage_received のみ)
- `appliers`: パワーの付与者ごとのスタック数 (全パワー)。`owner`: レリック・エンチャント等の持ち主
- 補正計算を通らないダメージ (破滅による撃破等) は `modifications` が無い (null)

#### `block_sources` (damage_received)

`[{ "player_id", "amount" }]`: このヒットで防いだブロック量を、ブロックを付けた人ごとに分けたもの (残っているブロックの比で按分)。

`source_appliers` (`[{ "player_id", "stacks", "origin"? }]`): 出どころが **ダメージを受けた敵自身に付いているパワー** (毒・Doom・絞殺など) のとき、
そのパワーのスタックを「誰が・何で」付けたかの内訳。同じ人が別のカードで付けた分は別の行になる。
web は与ダメージ・カード別の表・rDPS をこの比で按分する。`player_id` は最大スタックの人。

`origin` (`{ "id", "name", "type", "kind" }`): そのスタックを付けたカード・レリック・ポーション。`kind` は `card` / `relic` / `potion` / `orb` / `enchantment` / `power`。
パワーが付けた場合 (例: 有毒ガスのパワーが毎ターン毒を付ける) は、そのパワーを付けたカード等までさかのぼる (1 段まで)。分からなければ無し。

`triggered_by` (`{ "player_id", "id", "name", "type" }`, damage_dealt / block_gained): カード以外が出どころのダメージ・ブロックを、
**カードの効果が直接発動させた** ときの、そのカードと使った人。判定: カードのプレイ中 (`CardModel.OnPlayWrapper` の実行中) で、
出どころの処理が Hook の上書き (ゲームのイベントへの反応) の中からではなく、オーブの生成 (`OrbCmd.Channel`) で押し出された解放でもないこと。例: 感染爆発の毒の発動、デュアルキャストのオーブの解放。
web はカード別の表でだけ使う (combat-stats.md「カード別 (CardTable)」)。

`source_origin` (`{ "id", "name", "type", "kind" }`, damage_dealt / block_gained): 出どころが **自分側に付いているパワー** (トゲ・プレート等) のとき、
そのパワーを付けたカード・レリック・ポーション。カード別の表はこれで集計する。分からなければ無し。

#### power snapshot（`active_on_target` / `active_on_dealer` の中身）

```json
[
  {
    "power_id":   "VULNERABLE_POWER",
    "power_name": "脆弱",
    "stacks":     5,
    "appliers":   [ { "player_id": "76561...A", "stacks": 2 },
                    { "player_id": "76561...B", "stacks": 3 } ]
  }
]
```

- 戦闘詳細のタイムラインに、攻撃した側・受けた側に付いていたパワーを出すためのもの。貢献スコアの計算には使わない (`modifications` を使う)。
- `appliers` はプレイヤーごとの付与スタック数。
- 対象のパワー: `VULNERABLE_POWER` / `POISON_POWER` / `DOOM_POWER` / `WEAK_POWER` / `STRENGTH_POWER`。

### 冪等性

`event_uuid` UNIQUE。重複 POST は無視（既存レコードは変更しない）。

### Response
- `204 No Content` — 成功
- `401 Unauthorized` — write_token 不正
- `404 Not Found` — session_id 不在
- `400 Bad Request` — payload 不正

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
    "game_version":  "v0.113.1",
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

## ログイン

画面の挙動は [`user-pages.md`](./user-pages.md)。Steam の公式のログイン (OpenID 2.0) で、Steam ID だけを受け取る。

- `GET /auth/steam/login?return={path}`: Steam のログイン画面へ 302。`return` は自分のサイトの中のパス (`/` で始まり `//` で始まらないもの) だけを受け付け、それ以外は `/` にする。
- `GET /auth/steam/callback`: Steam へ `check_authentication` を問い合わせ、`is_valid:true` なら cookie `sts2stats_auth` (HttpOnly・Secure・SameSite=Lax、30 日) を付けて `return` へ 302。検証できなければ 401。
- cookie の中身は `{steam_id}.{有効期限の UNIX 時刻}.{HMAC-SHA256 の署名}`。署名の鍵は環境変数 `AUTH_COOKIE_KEY` (32 バイト以上の hex)。
- ログインが要る変更 (`PATCH` / `DELETE`) は、cookie が無ければ 401、`Origin` が自分のサイトでなければ 403。

## ユーザー

### `GET /api/users/{steam_id}`

本人がログインして開いたときは全部のランを返す。本人以外は、ユーザーページが全体に公開のとき、または Steam のフレンドに公開で見に来た人がフレンドのときだけ、公開で本人がページに載せているランを返す (user-pages.md「Steam のフレンドかどうか」)。非公開なら 403、知らない Steam ID なら 404。

```json
{
  "user": { "steam_id": "765...", "display_name": "Nobu", "profile_visibility": "private" },
  "is_me": true,
  "runs": [{
    "session_id": "uuid", "created_at": "2026-11-22T00:00:00Z",
    "character_id": "FOOL", "character_name": "フール", "game_mode": "Blitz", "ascension": 3, "game_version": "v0.113.1",
    "outcome": "victory", "final_floor": 48,
    "visibility": "public", "hidden": false, "is_recorder": true,
    "participants": [{ "steam_id": "765...", "display_name": "Nobu" }]
  }]
}
```

`character_*` はそのユーザーのキャラクター、`game_mode` はゲームモード (`Standard` / `Blitz` / `Daily` / `Dread`)。どちらも `run_start` から。`is_recorder` はそのユーザーが記録した人か。`game_version` はゲームのバージョンで、mod が送らなかったゲームでは `null`。

### `GET /api/users/{steam_id}/stats`

プレイヤーの統計 ([`player-stats.md`](./player-stats.md)) に使う、ゲームごとの集計を返す。見られる人と、403・404 を返す条件は `GET /api/users/{steam_id}` と同じ。
返すのは、そのユーザーが参加したゲームのうち結果が `victory` か `death` のもので、本人以外には、公開で本人がページに載せているものだけ。キャラクターと絞り込みでの集計は web が行う。

```json
{
  "user": { "steam_id": "765...", "display_name": "Nobu", "profile_visibility": "public" },
  "is_me": true,
  "games": [{
    "session_id": "uuid", "created_at": "2026-11-22T00:00:00Z",
    "character_id": "FOOL", "character_name": "フール", "game_mode": "Standard", "ascension": 3, "game_version": "v0.113.1",
    "outcome": "victory", "final_floor": 48, "max_act_index": 2,
    "partners": [{ "steam_id": "765...", "display_name": "agapants" }],
    "damage_share": 0.42,
    "card_offers": [{ "id": "WAR_TOUCH", "name": "ワー・タッチ", "offered": 2, "picked": 1, "act_index": 0 }],
    "relics": [{ "id": "WAR_PAINT", "name": "戦化粧" }],
    "fights": [{ "model_id": "CULTIST_BOSS", "model_name": "儀式の獣", "room_type": "Boss", "damage_taken": 31, "died": false }],
    "rest_choices": ["SMITH", "HEAL"],
    "ancients": [{ "model_id": "NEOW", "model_name": "ネオー", "options": [{ "title": "絹の髪束", "chosen": true }] }],
    "events": [{ "model_id": "...", "model_name": "...", "choices": ["..."] }]
  }]
}
```

- `partners`: 一緒に遊んだ人 (そのユーザーを除く参加者)。ソロなら空。
- `damage_share`: そのゲームで全員の `damage_dealt` の `amount` の合計のうち、そのユーザーの分の割合。ソロか、だれもダメージを与えていなければ `null`。
- `max_act_index`: そのゲームの `floor_snapshot` の `act_index` の最大 (0 始まり)。
- `card_offers`: カードと Act の組ごとに 1 つ。`act_index` はそのカードが提示された階の `act_index`。同じカードが複数の Act で提示されたら、Act ごとに分ける。
- `card_offers`・`relics`・`fights`・`rest_choices`・`ancients`・`events` の中身は player-stats.md「数字の出どころ」の記録から作る。`fights` の `died` は、死んだゲームの最後の戦闘なら `true`。`events` には、部屋が `NEOW` の階を入れない。
- backend はゲームと人の組ごとに集計を保存しておき、そのゲームに新しい記録が届いていたら作り直す。

### `GET /api/sessions/{id}/me`

`{ "is_participant": true, "is_recorder": false, "visibility": "public", "hidden": false }`。ログインしていなければ 401。非公開のランを記録した人以外が問い合わせると 404 (ランがあることを明かさない)。

### 変更

| リクエスト | body | 権限が無いとき |
|---|---|---|
| `PATCH /api/me` | `{ "profile_visibility": "public" \| "friends" \| "private" }` | — |
| `PATCH /api/sessions/{id}` | `{ "visibility": "public" \| "private" }` | 記録した人でなければ 403 |
| `PATCH /api/sessions/{id}/listing` | `{ "hidden": true }` | 参加者でなければ 403 |
| `DELETE /api/sessions/{id}` | — | 記録した人でなければ 403。消すと記録・参加者もまとめて消える |

成功は 204。

## `GET /healthz`

**Response 200** `ok`

---

## `GET /catalog.{lang}.json`

STS2 ゲーム本体の **カード / レリック / ポーション / エンチャント** definitions を返す静的 JSON。
WebUI が tooltip 表示用に 1 度だけ fetch する。

- `lang` は ISO 639-1 言語コード (現状 `ja` のみ存在)
- 内容は `web/public/catalog.{lang}.json` に commit 済の静的ファイル
- backend は embed FS で同梱配信（`/assets/*` と同じ embed ソース）
- 更新は STS2 アップデート時のみ手動: `make dump-catalog LANG=ja`
- 詳細運用フロー / 構造 / mod 側 dumper 実装は [`architecture.md`](../architecture.md) の「カタログ」の節 参照
- web 側ローダ仕様は [`run-overview.md`](./run-overview.md) の「chip ホバー tooltip (description)」参照

### Response 200 (Content-Type: application/json)

```jsonc
{
  "schema_version": 1,
  "lang": "ja",
  "generated_at": "2026-05-10T11:02:18.1525000Z",
  "cards": [
    {
      "id": "STRIKE_IRONCLAD",
      "name_base": "ストライク",
      "name_upgraded": "ストライク+",
      "description_base": "6ダメージを与える。",
      "description_upgraded": "[green]9[/green]ダメージを与える。",
      "rarity": "Basic",          // None / Basic / Common / Uncommon / Rare / Ancient / Event / Token / Status / Curse / Quest
      "card_type": "Attack",
      "cost": 1,
      "max_upgrade": 1
    }
  ],
  "relics":       [{ "id", "name", "description", "rarity" }],
  "potions":      [{ "id", "name", "description", "rarity" }],
  "enchantments": [{ "id", "name", "description" }]
}
```

### description のテキスト形式

STS2 内部 LocString の出力をそのまま含む。WebUI 側で render する:

| 装飾タグ | 意味 | web 変換 |
|---|---|---|
| `[gold]X[/gold]` | キーワード強調 | `<span class="text-yellow-300">` |
| `[blue]X[/blue]` | 数値強調 | `<span class="text-sky-400">` |
| `[green]X[/green]` | アップグレード差分強調 | `<span class="text-lime-300">` |
| `[red]X[/red]` | 警告 | `<span class="text-red-400">` |
| `[purple]X[/purple]` | 紫 | `<span class="text-purple-400">` |
| `[img]res://...[/img]` | アイコン (画像 path) | 削除 (web に対応画像無し) |

戦闘文脈依存で解決不能な placeholder (`{CalculatedDamage:diff()}`, `{InCombat:show:A|}`,
`{IsTargeting:show:...}` 等) は LocString のまま残る。WebUI 側で `XX` (灰色) にフォールバック表示。

### サイズ目安

raw 約 300KB / gzip 後 約 40KB。fetch は 1 ラン / 1 ブラウザセッションあたり 1 回。
`fetch` は HTTP デフォルトの cache 戦略 (Cache-Control / ETag) に従う。
**`cache: 'force-cache'` は使わない**こと: 一度 404 を喰らうとブラウザがそれをキャッシュし
回復不能になる嵌り経験あり。

---

## バージョニング方針

このドキュメントは常に「現在実装されている API」を表す。破壊的変更をしたときは、下の表に履歴を足す。

### バージョン履歴

| 時期 | 変更 |
|------|------|
| 2026-11 | `POST /sessions` と、セッション・ユーザーページ・統計の各ゲームに `game_version`。統計の `card_offers` をカードと Act の組ごとに分け、`act_index` を足す |
| 2026-11 | ログインとユーザーページ (`/auth/steam/*`、`/api/me`、`/api/users/{steam_id}`、ランの公開・非公開・削除)。`GET /api/sessions/{id}` の `session` に `visibility`。`POST /sessions/{id}/events` の body の上限を 2MB に上げる (ほかは 256KB) |
| 2026-11 | `POST /sessions/{id}/turns` (410 を返すだけだった) を削除。`combat_start` を受けたときに同じ階の別の戦闘の記録を消す処理を削除 (戦闘の番号は階の番号と同じなので働いていなかった。やり直した戦闘は web が最後の `combat_start` より後だけを使う)。power snapshot の `applier`、`modifications` の v1 形式を削除 |
| 2026-10 | v2: ラン全体の event を `floor_snapshot` に統合 (`room_entered` / `hp_changed` / `gold_changed` / `act_entered` / `rest_action` / `reward_taken` / `card_obtained` / `card_upgraded` / `card_enchanted` / `card_removed` / `relic_obtained` / `potion_obtained` / `potion_discarded` / `event_choice` を廃止)。`player_id` を全 event で正規化。`run_end` に `final_hp`。出どころをモデル ID に変更。`hit_index` 廃止 |
| 2026-05 | `GET /catalog.{lang}.json` 追加（chip tooltip 用 STS2 definitions、静的） |
| Phase 3.5 | `POST /turns` 廃止（410 Gone）、`turns` テーブル削除、`events` テーブルに combat_index / turn_number / sequence 追加。すべての出来事を `events` 1 表に統合 |
| Phase 2 | 初版 |

---

## 関連ドキュメント

- `combat-stats.md` — 戦闘の画面に出す値と集計方法
- `run-overview.md` — ラン全体・階の画面に出す値と集計方法
- `data-sources.md` — UI ↔ event_type ↔ mod hook の正規経路マップ
