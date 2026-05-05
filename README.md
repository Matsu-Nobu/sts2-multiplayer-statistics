# sts2-multiplayer-statistics

Slay the Spire 2 のマルチプレイ／シングルプレイのラン統計を mod で収集し、ブラウザから共有 URL で閲覧できるツール。

公開バックエンド: **https://sts2stats.fly.dev**

## 使う側

1. mod (`mod/dist/StsStats.dll` と `StsStats.json`) を `Slay the Spire 2/.../mods/StsStats/` に置く
2. ゲームを起動してラン開始 → 共有 URL がクリップボードにコピーされる
3. ブラウザで開くと統計画面（戦闘中は 10秒間隔で自動更新）

ホスト 1 人だけが mod を入れていれば、マルチプレイの全員ぶんの統計が記録される。

## 自前バックエンドを使う

公開サーバを使わず自分で立てたい場合:

```bash
make docker-run        # ローカルで :8080 起動
make mod-use-local     # mod を localhost に向ける
```

公開バックエンドに戻す:

```bash
make mod-use-public
```

切替の中身は `mods/StsStats/config.json` の有無だけ。再ビルドは不要。

## 開発

```bash
cp .env.example .env       # STS2 のインストールパスを設定
make help                  # ターゲット一覧
make all                   # mod のテスト → ビルド → インストール
make backend-dev           # backend を :8080 で起動
make web-dev               # frontend を :5173 で起動（HMR）
make app-build             # web を backend に同梱した単一バイナリ
make fly-deploy            # 公開バックエンドにデプロイ
```

必要ツール: dotnet / Go / Node.js / Docker (orbstack 等)

## 構成

```
mod/        STS2 用 C# mod
mod.tests/  mod のユニットテスト
backend/    Go API サーバ（SQLite + embed.FS）
web/        Svelte SPA
docs/       設計ドキュメント
```

詳細は [`docs/`](docs/) 参照。最低限の入り口:

- [`docs/API.md`](docs/API.md) — HTTP API 仕様（現行実装のみ）
- [`docs/DESIGN.md`](docs/DESIGN.md) — アーキテクチャ
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — 拡張計画
- [`docs/OPERATIONS.md`](docs/OPERATIONS.md) — 運用方針

## ライセンス

未定。
