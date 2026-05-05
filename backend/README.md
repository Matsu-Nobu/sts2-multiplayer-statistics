# backend

Go 製の API サーバ。mod から POST されてくる戦闘ターン・イベントを SQLite に保存し、WebUI / 共有URL から閲覧できるよう配信する。

仕様は repo 直下の `docs/API.md`、設計判断は `docs/PHASE2_PLAN.md` を参照。

## 要件

- Go 1.22+

## 開発

```bash
# 依存取得（初回のみ）
go mod tidy

# 起動（デフォルト :8080、./data.db）
go run ./cmd/server

# テスト
go test ./...
```

ルート Makefile から:

```bash
make backend-dev      # サーバ起動
make backend-test     # ユニットテスト
make backend-build    # bin/server をビルド
make backend-clean    # bin/ と data.db を削除
```

## 環境変数

| 変数 | デフォルト | 用途 |
|------|------------|------|
| `PORT` | `8080` | リスンポート |
| `DATABASE_PATH` | `./data.db` | SQLite ファイルパス（`:memory:` 可） |
| `BASE_URL` | `http://localhost:8080` | `share_url` 組み立て用（末尾スラッシュ無し） |
| `CORS_ALLOWED_ORIGINS` | `*` | カンマ区切り |
| `LOG_LEVEL` | `info` | `debug` / `info` / `warn` / `error` |

## 動作確認用 curl

```bash
# セッション作成
curl -sS -X POST http://localhost:8080/sessions \
  -H "Content-Type: application/json" \
  -d '{"host_name":"Nobu","host_steam_id":"76561199000000000","character_id":"IRONCLAD","ascension":5}'
# => {"session_id":"...","write_token":"...","share_url":"..."}

# ターン投稿
curl -sS -X POST http://localhost:8080/sessions/<id>/turns \
  -H "Authorization: Bearer <write_token>" \
  -H "Content-Type: application/json" \
  -d @turn.json

# イベント投稿（bulk）
curl -sS -X POST http://localhost:8080/sessions/<id>/events \
  -H "Authorization: Bearer <write_token>" \
  -H "Content-Type: application/json" \
  -d @events.json

# セッション全データ取得
curl -sS http://localhost:8080/api/sessions/<id> | jq .
```

## 構成

```
backend/
├── cmd/server/main.go        エントリポイント
├── internal/
│   ├── api/                  HTTP ハンドラ・ルータ・認証ミドルウェア
│   └── store/                SQLite アクセス層 + マイグレーション
│       └── migrations/       SQL マイグレーション（embed.FS）
└── testdata/                 mod 実出力 JSON のフィクスチャ置き場
```
