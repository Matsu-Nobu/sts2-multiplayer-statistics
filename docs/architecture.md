# STS2 Multiplayer Statistics — 設計ドキュメント

## 概要

Slay the Spire 2 のマルチプレイ／シングルプレイのラン統計を、URL を共有するだけで仲間とブラウザから閲覧できるようにする mod + バックエンドシステム。

ゲーム内UIは持たず、戦闘開始時に共有URLをクリップボードへコピーするだけ。閲覧はすべて WebUI 側で行う。

---

## アーキテクチャ

```
[STS2 mod (C# / Harmony)]
        │
        │ HTTP POST /sessions          （run 開始時）
        │ HTTP POST /sessions/{id}/events  （ターン終了 / combat 終了 / run 終了ごとに bulk）
        ▼
[backend (Go + SQLite, embed.FS で web を同梱)]
        ▲
        │ HTTP GET /api/sessions/{id}（ETag による If-None-Match ポーリング）
        │
[WebUI (Svelte SPA)]
```

### 設計上の主な判断

- **外部サーバ方式**: 共有 URL をネット越しに渡すユースケースを優先。mod 側は HTTP POST に集約。
- **イベント列モデル**: ターン集計済み payload ではなく、生イベント列（`damage_dealt` / `block_gained` / `power_changed` …）を bulk POST する。集計は WebUI 側でクライアント計算。これにより rDPS / rMit のような attribution 計算を後追いで実装できる。
- **WebSocket 不採用**: WebUI は 10 秒間隔の HTTP ポーリングで十分。サーバは ETag を返すので変化が無ければ 304 で返る。
- **mod 側 UI なし**: クリップボードに URL をコピーするのみ。Godot リソース（PCK）も不要。

---

## データフロー

### run 開始時
1. mod が `POST /sessions` で session を作成。`write_token` と `share_url` を受け取る。
2. share_url をクリップボードへコピー。
3. mod は `run_start` イベントを events バッファに積む。

### 戦闘・ターン進行中
1. Harmony patch した hook が逐次イベントを生成し、`EventBuffer` に蓄積する。
2. ターン終了 (`AfterTurnEnd(side=Player)`) / 戦闘終了 (`AfterCombatEnd`) / run 終了の節目で、バッファ内のイベント列を `POST /sessions/{id}/events` に bulk 送信。
3. 送信失敗時はバッファに残り次回再送。`event_uuid` UNIQUE 制約で重複は冪等に弾かれる。
4. mod は `sts_stats.jsonl` にローカルログも書き出す（バックエンド消滅時の保険）。

### 閲覧側
- ブラウザで `https://<host>/s/{sessionId}` にアクセス。SPA が `GET /api/sessions/{id}` で session + players + events を一括取得。
- 10 秒間隔ポーリング、`If-None-Match` で 304 を最大限利用。
- 戦闘単位サマリ・カード別統計・rDPS / rMit はすべてクライアント側で events から導出。

---

## mod 側の実装

### マルチプレイにおける送信責任

STS2 のマルチプレイは決定論的ロックステップ方式で、全クライアントが同じシミュレーションを実行している。`AfterDamageGiven` 等の hook は他プレイヤーの行動でも発火し、`dealer` で識別できる。
**ホスト 1 人の mod だけがサーバーへ送信する**（`RunManager.Instance.NetService.Type` で判定）。仲間は mod を入れる必要がない。

### Harmony patch している主な hook

| hook | 用途 |
|------|------|
| `BeforeCombatStart` | 戦闘カウンタ初期化、`combat_start` emit |
| `AfterCombatEnd` | `combat_end` emit、buffer flush |
| `AfterTurnEnd(side=Player)` | ターン終了の確定点。buffer flush |
| `AfterDamageGiven` | `damage_dealt` emit（dealer / target / 通った量・ブロック吸収・overkill） |
| `ModifyDamage` (post) | overkill / blocked_damage の最終確定（HP snapshot ベース） |
| `BeforeDamageReceived` / `AfterDamageReceived` | `damage_received` emit |
| `BeforeCardPlayed` / `AfterCardPlayed` | `card_played` emit、`CardPlayedScope` の出入り |
| `AfterCardDrawn` | `card_drawn` emit |
| `AfterBlockGained` | `block_gained` emit |
| `AfterEnergySpent` | `energy_spent` emit |
| `AfterPowerAmountChanged` | `power_changed` emit |
| `AfterPotionUsed` | `potion_used` emit |

### 間接ダメージ・パワー由来ブロックの帰属

`AfterDamageGiven` の `cardSource` は `CardModel?` のみで、Poison / Doom / Lightning Orb / Thorns / Flame Barrier / Rampart / BlockNextTurn 等は識別できない。これを補うため、AsyncLocal による source context 伝搬を実装している。

- `DamageSourceContext` (`AsyncLocal`) — 間接ダメージのソースを伝搬
- `BlockSourceContext` (`AsyncLocal`) — Power 由来ブロックのソースを伝搬
- `CardPlayedScope` — Lightning Orb の手動 Evoke / 自動 Evoke を区別するためのフラグ

これらが解決した cardSource は予約タグ（`(poison)` / `(doom)` / `(lightning_evoke)` / `(lightning_evoke_auto)` / `(lightning_passive)` / `(thorns)` / `(flame_barrier)` / `(rampart)` / `(block_next_turn)`）として `source_card_id` に入る。詳細は `api.md` の「予約 source_card_id」セクション参照。

複数プレイヤーが同じデバフ（Vulnerable / Poison 等）を撒いている場合、`active_on_target` の各 power エントリに `appliers[]`（player_id × stacks）を埋めて送る。WebUI 側は stacks 比で按分し rDPS / rMit を算定する。

### HTTP 送信

`System.Net.Http.HttpClient` を使用。バックグラウンドスレッドで非同期送信し、ゲームループをブロックしない。失敗時はバッファ保持＋次回再送、ローカル jsonl にも記録。

---

## バックエンド設計

### エンドポイント

エンドポイントの正規仕様は `api.md` を参照。要点のみ:

| Method | Path | 認証 | 用途 |
|--------|------|------|------|
| `POST` | `/sessions` | — | セッション作成（write_token 発行） |
| `POST` | `/sessions/{id}/events` | Bearer | イベント bulk 投稿 |
| `POST` | `/sessions/{id}/turns` | — | **410 Gone**（Phase 3.5 で廃止） |
| `GET`  | `/api/sessions/{id}` | — | WebUI 用 session 全データ取得（ETag 対応） |
| `GET`  | `/s/{id}` | — | SPA HTML 配信 |
| `GET`  | `/healthz` | — | ヘルスチェック |

### スキーマ

3 テーブル構成（`001_init.sql` + `002_unified_events.sql`）:

- `sessions` — run 単位のメタ（character / ascension / seed / outcome / finished_at / write_token …）
- `players` — プレイヤーマスタ（steam_id, display_name）
- `events` — 全イベント。カラム: `id, event_uuid (UNIQUE), session_id, player_id, event_type, occurred_at, received_at, floor, payload_json, combat_index, turn_number, sequence`

戦闘内 event は `(combat_index, turn_number, sequence)` を持ち、戦闘外 event は NULL。集計用の独立テーブル（旧 `turns`）は持たず、events に一本化。

### ETag

`SHA256(session_id | max(received_at) | events 件数 | outcome | finished_at)` の先頭 16 バイトを hex で返す。クライアントは `If-None-Match` で 304 を引き出せる。

### 技術スタック

| レイヤー | 採用 |
|----------|------|
| API サーバ | Go (`net/http` + chi) |
| DB | SQLite（単一ファイル） |
| WebUI | Svelte SPA（Vite ビルド） |
| 配布 | Go バイナリに `embed.FS` で web を同梱した単一バイナリ |
| デプロイ | Fly.io 無料枠（shared-cpu-1x × 2 / volume 3GB / 月 160GB） |

---

## WebUI

クライアント側でイベント列から集計を導出する。主なビュー / パネル:

- `SessionView` — トップビュー（戦闘セレクタ + 統計）
- `AllCombatsView` — ラン全体サマリ
- `CombatView` — 戦闘単位ビュー
- `TimelineView` — イベント列のタイムライン表示
- `RdpsPanel` / `RmitPanel` — 観測ベースの貢献度スコア
- `DamageChart` — ターン推移折れ線・戦闘単位棒グラフ
- `CardTable` — カード別ダメージ／ブロック
- `DebuffTable` — 状態異常付与

集計ロジックは `web/src/lib/aggregate.ts` / `rdps.ts` 等にまとまっている。

---

## セッション URL

```
https://<host>/s/{sessionId}
```

- `sessionId` は mod 側で UUID v4 を生成
- 戦闘開始時にクリップボードへ自動コピー
- 有効期限なし（運用上の保持期限は `operations.md` 参照）
- ドメイン切替に強くするため共有パスは `/s/<uuid>` 固定

---

## クロスプラットフォーム対応

mod のコード（`HttpClient` / `OS.Clipboard` / `OS.GetUserDataDir()`）はクロスプラットフォーム。`.csproj` の MSBuild 条件分岐で参照する STS2 DLL のパスを切替。macOS は `uname -m` でアーキテクチャを判定。

| 差異 | macOS arm64 | macOS x86_64 | Windows |
|------|-------------|--------------|---------|
| ゲーム DLL | `data_sts2_macos_arm64/` | `data_sts2_macos_x86_64/` | `data_sts2_windows_x86_64\` |
| mods フォルダ | `.app/Contents/MacOS/mods/` | 同左 | `Slay the Spire 2\mods\` |

---

## カタログ (cards / relics / potions / enchantments の definitions)

UI で chip にホバーした時に「ストライク: 6 ダメージを与える。」のような description を出すために、
STS2 内部の Card / Relic / Potion / Enchantment のメタデータを **静的 JSON ファイル**として
リポジトリにコミットしている。

### 配置

```
web/public/catalog.{lang}.json   ← リポジトリで version 管理
                                    vite が public/ をそのまま dist/ にコピー
                                    backend embed FS が dist/ をサーブ
                                    → 本番で /catalog.{lang}.json で取得可能
```

現状は `catalog.ja.json` のみ。英語版が必要になったら `catalog.en.json` を並列追加。

### 構造

```jsonc
{
  "schema_version": 1,
  "lang": "ja",
  "generated_at": "2026-...",
  "cards": [
    {
      "id": "STRIKE_IRONCLAD",
      "name_base": "ストライク",
      "name_upgraded": "ストライク+",
      "description_base": "6ダメージを与える。",
      "description_upgraded": "[green]9[/green]ダメージを与える。",
      "rarity": "Basic",
      "card_type": "Attack",
      "cost": 1,
      "max_upgrade": 1
    }, ...
  ],
  "relics":       [{ "id", "name", "description", "rarity" }, ...],
  "potions":      [{ "id", "name", "description", "rarity" }, ...],
  "enchantments": [{ "id", "name", "description" }, ...]
}
```

description には STS2 内部のタグ (`[gold]X[/gold]` `[blue]X[/blue]` `[green]X[/green]` 等) が
そのまま残る。WebUI 側 (`web/src/lib/catalog.ts` の `renderCardDescription`) で HTML span に変換。

戦闘文脈依存の placeholder (`{CalculatedDamage}` `{InCombat:show:...}` 等) は解決不能で
そのまま残るため、web 側で `XX` にフォールバック表示する。

### 更新ワークフロー

STS2 がアップデートされた時のみ。以下を実行:

```bash
make dump-catalog LANG=ja
```

ターゲットの内部:
1. STS2 のゲーム内言語が `LANG` (デフォルト ja) に設定されてるか確認を促す
2. **環境変数 `STS_STATS_DUMP_CATALOG=1` を立てて STS2 を起動**するよう指示
3. 新規ランを Neow まで進めると mod の `CatalogDumper` が
   `~/Library/.../mods/StsStats/catalog-dump.json` に dump する
4. STS2 終了後 Enter を押すとファイルを `web/public/catalog.{LANG}.json` にコピー
5. `git diff` で内容確認 → commit → deploy

### mod 側 dumper

実装: `mod/src/CatalogDumper.cs`

- 環境変数 `STS_STATS_DUMP_CATALOG=1` のときだけ動作 (一般プレイヤー無影響)
- `ModelDb.AllCards / AllRelics / AllPotions / _contentById` を reflection で iterate
- 各 model の Title / Description を `LocString.GetFormattedText()` で localize 済テキスト取得
- カード description は `MutableClone() → UpgradeInternal()` でアップグレード版も生成し、
  `description_base` / `description_upgraded` 両方を出力
- `AddExtraArgsToDescription` (private) を reflection で呼んでカード固有 DynamicVar を bind

### Web 側ロード

`web/src/lib/catalog.ts`:
- `loadCatalog(lang)` で 1 回 fetch、メモリキャッシュ
- `CatalogLookup.card(id, upgraded)` `relic(id)` `potion(id)` `enchantment(id)` で引く
- `renderCardDescription(text)` で HTML 化

`SessionView.svelte` でアプリマウント時に `loadCatalog('ja')` を呼び、各 chip に lookup を渡す。

---

## 関連ドキュメント

- `api.md` — HTTP API 仕様（正）
- `spec/combat-stats.md` — 戦闘統計画面の表示仕様
- `spec/run-overview.md` — ラン全体統計画面の表示仕様
- `spec/data-sources.md` — UI ↔ event_type ↔ mod hook の正規経路マップ
- `roadmap.md` — フェーズ別到達点と将来拡張
- `operations.md` — 配布・運用方針
