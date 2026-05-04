# STS2 Multiplayer Statistics — 設計ドキュメント

## 概要

Slay the Spire 2 のマルチプレイヤーセッションにおける統計情報（与ダメージ・被ダメージ・ブロックなど）を、
仲間と共有できるようにする mod + バックエンドシステム。

URLを共有するだけで、同じゲームの統計をブラウザから閲覧できる。

---

## アーキテクチャ

```
[STS2 mod (C#/Godot)]
        |
        | HTTP POST（ターン終了時）
        v
[バックエンドサーバー (REST API)]
        |
        | HTTP GET
        v
[ブラウザ / 共有URL]
```

### 選択の理由

- **Option A（外部サーバー）を採用**: インターネット越しで共有可能、modはシンプルに保てる
- **ターン終了時に一括送信**: リアルタイム更新よりも実装が単純で、ターンごとの統計が見たいというユースケースに合致
- **WebSocketは不採用**: ターン終了ごとのポーリングで十分。mod側の実装複雑度を下げる

---

## データフロー

### ゲーム開始時
1. mod がセッションID（UUID v4）を生成
2. `POST /sessions` でセッションを作成
3. セッションURL をゲーム内に表示（クリップボードコピー機能付き）

### ターン終了時（`AfterPlayerTurnEnd` hook）
1. そのターンの統計スナップショットを集計
2. `POST /sessions/{id}/turns` に送信

### 閲覧側
- ブラウザで `https://<server>/s/{sessionId}` にアクセス
- ページ自体がポーリング（10秒間隔）で最新データを取得
- ゲーム進行中はターンが追加されていく

---

## mod 側の実装

### 前提: 1クライアントで全プレイヤーの情報が取れる（調査済み・確定）

STS2のマルチプレイヤーは**決定論的ロックステップ方式**で、全クライアントが同一の完全なゲームシミュレーションを実行している。
`AfterDamageGiven` hookは自分のダメージだけでなく**全プレイヤーの全ダメージイベントで発火**する。
`dealer` パラメータで誰が与えたかを識別できる（プレイヤーかモンスターかも含む）。

**結論: ホスト1人のmodだけがサーバーに送信すれば全プレイヤーの統計を網羅できる。**

### 送信者の決定

```csharp
// ホストだけがサーバーに送信する
if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
```

ソロプレイ時も同様に送信する（NetGameTypeがHostと同等になる）。

### 使用するゲームHook（調査済み）

```csharp
Hook.BeforeCombatStart    // 戦闘カウンタ初期化、セッション未作成なら作成
Hook.AfterDamageGiven     // 全プレイヤー分のダメージ集計（dealer, results, target, cardSource）
Hook.AfterPlayerTurnStart // 「次のターン開始」= 「前ターン終了」として前ターンデータを送信
Hook.AfterCombatEnd       // 最終集計・送信
```

> **注意**: ターン終了専用のhookが存在すれば `AfterPlayerTurnStart` の代用より優先する。

### 特殊ケース（既存modで解決済みパターン）

- **毒ダメージ**: `PoisonPower.AfterSideTurnStart` を Harmony でパッチ、`AsyncLocal<T>` でコンテキスト保持
- **Doomダメージ**: `DoomPower.DoomKill` の prefix で pending ダメージを記録
- **モンスターダメージの除外**: `dealer` がプレイヤークリーチャーかどうかを reflection で判定してスキップ

### HTTP送信

`System.Net.Http.HttpClient` を使用（STS2MCP / STS2-MenuControl で動作確認済み）。
バックグラウンドスレッドで非同期送信し、ゲームループをブロックしない。

---

## バックエンド設計

### エンドポイント

| Method | Path | 概要 |
|--------|------|------|
| `POST` | `/sessions` | セッション作成 |
| `GET` | `/sessions/{id}` | セッション全統計取得 |
| `POST` | `/sessions/{id}/turns` | ターンデータ追加 |
| `GET` | `/sessions/{id}/turns` | ターン一覧取得 |

### データモデル

```json
// Session
{
  "id": "uuid",
  "created_at": "ISO8601",
  "players": ["PlayerA", "PlayerB"],
  "combat_count": 3
}

// Turn
{
  "session_id": "uuid",
  "combat_index": 1,
  "turn_number": 4,
  "submitted_at": "ISO8601",
  "stats": {
    "PlayerA": {
      "damage_dealt": 45,
      "damage_received": 12,
      "block_gained": 20,
      "cards_played": 5,
      "poison_damage": 0,
      "overkill": 0
    },
    "PlayerB": {
      "damage_dealt": 30,
      "damage_received": 8,
      "block_gained": 15,
      "cards_played": 4,
      "poison_damage": 10,
      "overkill": 5
    }
  }
}
```

### 技術スタック候補

| レイヤー | 候補 | 採用理由 |
|----------|------|----------|
| APIサーバー | Go (net/http) or Node.js (Hono/Fastify) | 軽量・デプロイが容易 |
| DB | SQLite (単一ファイル) | セットアップ不要、無料tier内で収まる |
| フロントエンド | HTML + vanilla JS (同一サーバーから配信) | 依存関係なし、シンプル |
| デプロイ | Fly.io (無料tier) | 常時起動、無料で3VM、SQLiteと相性が良い |

### Fly.io 無料枠について

- Machines: 2台まで常時起動（shared-cpu-1x, 256MB）
- Storage: volume 3GB まで無料（SQLite に使用）
- 帯域: 月160GB まで無料
- このユースケースには十分

---

## セッションURL設計

```
https://<fly-app-name>.fly.dev/s/{sessionId}
```

- セッションIDは mod 側で UUID v4 を生成
- mod は戦闘開始時に URL を **クリップボードに自動コピー**（`OS.Clipboard = url` の1行のみ）
- ゲーム内UIオーバーレイは**不要**。統計の閲覧はすべてブラウザ側に委ねる
- URLの有効期限: 設定しない（ゲーム終了後も閲覧可能）

---

## mod の責務（確定・シンプル版）

ゲーム内UIを持たないため、modの責務は以下のみ:

1. hookでイベントを受け取る
2. ターンごとに統計を集計する
3. ターン終了時にバックエンドへ HTTP POST する
4. セッション開始時にURLをクリップボードにコピーする（1行）

PCKファイル（Godotリソース）は**不要**。`has_pck: false` で確定。

---

## フェーズ計画

### Phase 1: mod PoC（hookとログ出力の検証）
- [ ] mod プロジェクト作成（C# / DLLのみ）
- [ ] `AfterDamageGiven` hookで与ダメージ収集・ローカルJSONLファイルに出力
- [ ] hookが正しく発火することを確認

### Phase 2: バックエンド + HTTP送信
- [ ] バックエンドAPI（Go）実装・Fly.ioにデプロイ
- [ ] SQLiteスキーマ作成
- [ ] mod側のStatsLoggerをHttpSenderに置き換え
- [ ] セッション開始時にURLをクリップボードコピー

### Phase 3: 閲覧WebUI
- [ ] セッション統計閲覧ページ（HTML + vanilla JS）
- [ ] プレイヤー別ダメージ表示
- [ ] ターンごとのグラフ表示
- [ ] 10秒ポーリングで更新

### Phase 4: 拡張統計（後回し）
- [ ] 毒・Doomダメージの帰属
- [ ] カード別ダメージ内訳
- [ ] ボス戦・通常戦の区別

---

## クロスプラットフォーム対応（将来対応）

mod本体のコード（`HttpClient`・`OS.Clipboard`・`OS.GetUserDataDir()`）はすべてクロスプラットフォームで追加変更不要。
ビルドフローだけ変えれば対応できる。

| 差異 | macOS arm64 | macOS x86_64 | Windows |
|------|-------------|--------------|---------|
| ゲームDLL | `data_sts2_macos_arm64/` | `data_sts2_macos_x86_64/` | `data_sts2_windows_x86_64\` |
| modsフォルダ | `.app/Contents/MacOS/mods/` | 同左 | `Slay the Spire 2\mods\` |
| ビルドスクリプト | `build.sh` | `build.sh` | `build.bat` or `build.ps1` |

対応方法: `.csproj` の MSBuild 条件分岐（`$([MSBuild]::IsOSPlatform('OSX'))` 等）でDLLパスを切り替えるのが最もシンプル。macOSはさらに `uname -m` でアーキテクチャを判定する。

---

## 未解決の問題

1. **ターン終了hookの正確な名前**: `AfterPlayerTurnStart` を代用予定だが、専用hookの有無を確認する必要あり
2. ~~**マルチプレイヤー時の送信者**~~ → **解決**: ホストのみが送信する。全クライアントが同一シミュレーションを実行しているため、ホスト1台で全プレイヤーの統計を収集できる。
3. ~~**セッションの紐付け**~~ → **解決**: ホストのmodがセッションIDを生成してサーバーに登録し、URLをゲーム外（Discord等）で共有するだけでよい。仲間はmodすら不要。

---

## 参考リポジトリ

| リポジトリ | 参考にする点 |
|-----------|-------------|
| [BAIGUANGMEI/STS2-DamageTracker](https://github.com/BAIGUANGMEI/STS2-DamageTracker) | DamageGivenフック・毒/Doom処理パターン |
| [Gennadiyev/STS2MCP](https://github.com/Gennadiyev/STS2MCP) | バックグラウンドHTTPサーバー実装 |
| [L4ntern0/STS2-MenuControl](https://github.com/L4ntern0/STS2-MenuControl) | mod内HTTP通信パターン |
| [BAKAOLC/STS2-ViewedCardsStatistics](https://github.com/BAKAOLC/STS2-ViewedCardsStatistics) | 統計データ構造・永続化パターン |
