# sts2-multiplayer-statistics

Slay the Spire 2 のマルチプレイ／シングルプレイにおけるラン統計を収集・共有するためのプロジェクト。
mod でゲームから統計を収集し、バックエンドサーバに送信、ブラウザで共有 URL から閲覧できる。

```
[STS2 mod (C#/Godot)]
        │ HTTP POST（ターン終了時 / 戦闘外イベント）
        ▼
[バックエンドサーバ (Go + SQLite)]
        │ HTTP GET
        ▼
[ブラウザ / 共有URL（Svelte SPA）]
```

公開バックエンド: **https://sts2stats.fly.dev**

---

## 目次

- [エンドユーザ向け（mod を入れて使う）](#エンドユーザ向けmod-を入れて使う)
- [セルフホスト（自前バックエンド）](#セルフホスト自前バックエンド)
- [開発者向け](#開発者向け)
- [プロジェクト構成・ドキュメント](#プロジェクト構成ドキュメント)
- [設計の特徴](#設計の特徴)
- [ライセンス](#ライセンス)

---

## エンドユーザ向け（mod を入れて使う）

mod を入れて起動するだけで、自分のラン統計が **共有バックエンド (https://sts2stats.fly.dev)** に自動送信され、URL でブラウザから閲覧できる。

### 1. mod のインストール

リリース zip をダウンロードして `Slay the Spire 2/mods/StsStats/` に展開する（リリース未公開時はソースから `make all` でビルド）。

最終的に以下のファイル構成になる:
```
.../Slay the Spire 2/mods/StsStats/
  ├── StsStats.dll
  └── StsStats.json
```

### 2. ゲーム起動 → ラン開始

mod が自動で:
1. バックエンドにセッションを作成
2. **共有 URL をクリップボードにコピー**
3. JSONL ログ `~/Library/Application Support/SlayTheSpire2/sts_stats.jsonl` にも同じ URL を記録

### 3. ブラウザで開く

クリップボードの URL を貼り付けるか、ログから取得:
```bash
grep session_created "$HOME/Library/Application Support/SlayTheSpire2/sts_stats.jsonl" | tail -1
```

`https://sts2stats.fly.dev/s/<uuid>` を開くと統計画面。戦闘中はリアルタイム（10秒ポーリング）で更新される。

### マルチプレイ

**ホスト1人だけが mod を入れていれば全員ぶんの統計が記録される**（STS2 はロックステップ方式で全クライアントが同じ全プレイヤーのイベントを処理するため）。共有 URL を Discord 等で渡せば、仲間は mod 不要で閲覧できる。

### 何が送信されるか

- Steam ID（プレイヤー識別）と Steam 表示名
- run のメタ情報（キャラクター・Ascension・seed）
- 戦闘ごとのターン推移、与/被ダメ、カード使用、デバフ付与等
- 詳細仕様: [`docs/API.md`](docs/API.md)

送信を止めたい場合は mod を削除するか、後述のセルフホスト方式に切り替える。

---

## セルフホスト（自前バックエンド）

公開サーバを使いたくない／プライベートに使いたい場合、自前でバックエンドを建てて mod の接続先を切り替えられる。

### 方法 A: ローカル PC に直接建てる

最も簡単。mod と同じ Mac／Windows 上で動かす。

1. **Docker 経由で起動**（docker / OrbStack 等が必要）:
   ```bash
   git clone <this-repo>
   cd sts2-multiplayer-statistics
   make docker-build
   make docker-run     # :8080 で起動、SQLite は ./.docker-data/data.db に保存
   ```

2. **mod の接続先をローカルに切り替え**:

   `Slay the Spire 2/mods/StsStats/config.json` を作成（mod 再ビルド不要）:
   ```json
   { "backend_url": "http://localhost:8080" }
   ```

3. ゲーム起動 → クリップボードに `http://localhost:8080/s/<uuid>` がコピーされる

### 方法 B: 自分のサーバ／VPS に建てる

LAN 内の友達と共有したり、外向きに公開したりしたい場合。

1. Docker が動く Linux ホストを用意（VPS、自宅サーバ等）
2. リポジトリを clone して `make docker-build && make docker-run`、もしくは:
   ```bash
   docker run -d --name sts2stats \
     -p 8080:8080 \
     -v /path/to/data:/data \
     -e BASE_URL=https://your-host.example.com \
     your-image:tag
   ```
3. リバースプロキシ（Caddy / nginx）で TLS 終端を推奨
4. mod の `config.json` で `"backend_url": "https://your-host.example.com"`

### 方法 C: Fly.io に自分名義でデプロイ

公開バックエンドと同じ構成を自分のアカウントで再現したい場合:

```bash
fly auth login
fly apps create <your-app-name>
fly volume create sts2stats_data --region nrt --size 1
# fly.toml の app と BASE_URL を <your-app-name> に書き換える
fly deploy
```

mod 側の `config.json` で `"backend_url": "https://<your-app-name>.fly.dev"`。

### config.json の上書き優先順

mod は接続先 URL を以下の順で解決する:

1. `mods/StsStats/config.json` の `"backend_url"`
2. 環境変数 `STS_STATS_BACKEND_URL`（ターミナルからゲームを起動した場合のみ有効）
3. mod に焼き込まれているデフォルト（`https://sts2stats.fly.dev`）

`backend_url` を空文字 `""` にすると **HTTP 送信は無効化** され、JSONL ローカルログだけ動く（オフライン用途）。

---

## 開発者向け

### 必要なツール

- macOS（arm64 で開発中。Windows / Linux はビルドフロー差し替えで対応可、`docs/DESIGN.md` 参照）
- Slay the Spire 2（Steam 版）
- .NET SDK 9 以上 — `brew install dotnet`
- Go 1.22 以上 — `brew install go`
- Node.js 20 以上 — `brew install node`
- Docker 互換ランタイム — `brew install orbstack`（推奨）または Docker Desktop

### 初期セットアップ

```bash
git clone <this-repo>
cd sts2-multiplayer-statistics
cp .env.example .env
# .env を編集（STS2_DATA_DIR / STS2_MODS_DIR をローカル環境に合わせる）
```

### Makefile タスク

`make help` で一覧表示。

| カテゴリ | コマンド | 内容 |
|---------|---------|------|
| **mod** | `make all` | テスト → ビルド → インストール |
|         | `make test` | mod のユニットテスト |
|         | `make build` | mod のビルドのみ |
|         | `make install` | mod を STS2 の `mods/` に配置 |
|         | `make log` | mod の JSONL ログを tail |
| **mod 接続先** | `make mod-use-local` | mod を `http://localhost:8080` に向ける |
|              | `make mod-use-public` | mod を公開バックエンドに戻す |
|              | `make mod-status` | 現在の接続先を表示 |
| **backend** | `make backend-dev` | `:8080` で開発サーバ起動 |
|             | `make backend-test` | Go ユニットテスト |
|             | `make backend-build` | Go バイナリのみビルド |
|             | `make backend-clean` | bin / SQLite 削除 |
| **web** | `make web-install` | npm install |
|         | `make web-dev` | Vite dev server（`:5173`、`/api` はバックエンドへプロキシ） |
|         | `make web-build` | 本番ビルド（`web/dist/` 生成） |
| **統合** | `make app-build` | web ビルド → backend に embed → 単一バイナリ生成 |
|         | `make app-run` | 上記＋起動 |
| **Docker** | `make docker-build` | コンテナイメージ生成 |
|           | `make docker-run` | `:8080` で起動、`./.docker-data` に永続化 |
|           | `make docker-stop` | 停止 |
| **Fly.io** | `make fly-deploy` | デプロイ |
|           | `make fly-logs` | リモートログ |
|           | `make fly-status` | アプリ状態 |
|           | `make fly-ssh` | コンテナに SSH |

### 開発フロー

1. **mod を変更したら**: `make all`（再ビルド・インストール）してゲーム再起動
2. **backend を変更したら**: `make backend-dev`（自動 reload なし、再起動が必要）
3. **frontend を変更したら**: `make web-dev`（HMR で即座反映）

### mod の接続先を切り替える

開発中は **ローカル backend** に、普段は **公開 backend** に向けたい、という切り替えは Make 一発:

```bash
make mod-use-local      # http://localhost:8080 に向ける（config.json 作成）
make mod-use-public     # 公開バックエンドに戻す（config.json 削除）
make mod-status         # 現在どちらに向いているか確認
```

切替は **mod の DLL と同じディレクトリの `config.json` の有無**で制御している。再ビルド不要、ゲーム再起動だけで反映。`config.json` が無ければ DLL に焼き込まれた既定 URL（`https://sts2stats.fly.dev`）が使われる。

開発時の典型ワークフロー:
```bash
make mod-use-local        # ローカルに向ける
make backend-dev          # 別ターミナルでローカル backend 起動
# ゲーム再起動 → ローカルにデータが流れる
make mod-use-public       # 終わったら公開バックエンドに戻す
```

---

## プロジェクト構成・ドキュメント

```
sts2-multiplayer-statistics/
├── docs/         設計・計画ドキュメント（仕様の一次情報）
├── mod/          STS2 用 mod 本体（C# / Godot）
├── mod.tests/    mod のユニットテスト（xunit.v3）
├── backend/      Go 製 API サーバ
├── web/          Svelte + Vite + Tailwind による閲覧 UI
├── Dockerfile    multi-stage build（web → go → distroless）
├── fly.toml      Fly.io デプロイ設定
└── Makefile      タスクランナー
```

| ドキュメント | 内容 |
|--------------|------|
| [`docs/DESIGN.md`](docs/DESIGN.md) | 全体アーキテクチャと初期設計判断 |
| [`docs/STATS_DESIGN.md`](docs/STATS_DESIGN.md) | 収集している統計項目の定義（表示要件起点） |
| [`docs/API.md`](docs/API.md) | mod ⇄ backend ⇄ WebUI の HTTP API 仕様（**現在実装されているもののみ**） |
| [`docs/PHASE2_PLAN.md`](docs/PHASE2_PLAN.md) | Phase 2 実装計画 |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | 段階的拡張計画と派生統計のアイデア |
| [`docs/OPERATIONS.md`](docs/OPERATIONS.md) | 運用方針・配布モデル |
| [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) | mod 側の初期実装計画（Phase 1 時点） |

---

## 設計の特徴

- **単一クライアント収集**: STS2 のマルチプレイは決定論的ロックステップなので、ホスト 1 人の mod だけで全プレイヤーの統計を収集可能
- **イベントログ + ターン集計のハイブリッド**: 戦闘中の高頻度データは per-turn 集計、それ以外の discrete event は raw event ログに格納
- **将来の統計拡張に強い**: 新しい event_type を追加するだけで派生統計が増やせる（DB スキーマ変更不要）
- **失敗に強い**: バックエンド送信は best-effort。失敗してもゲームは止まらず JSONL ローカルログに残る
- **ラン継続の自動検出**: ゲーム再起動を跨いだセーブ再開でも、同じセッション ID が維持される
- **共有 URL**: ホストが mod を入れていれば、仲間は URL 1 つ受け取るだけで閲覧可能（mod 不要）
- **書き込み保護**: 共有 URL を渡された人は読めるが書けない（write_token は mod だけが保持）
- **単一バイナリ**: backend は Go の embed で SPA を内包。Docker イメージ 22MB、依存ゼロ

---

## ライセンス

未定。
