# syntax=docker/dockerfile:1
# Multi-stage build:
#   1) web: Svelte/Vite SPA を生成
#   2) go : web/dist を embed して単一バイナリ化（CGO 不要、modernc.org/sqlite）
#   3) ランタイムは distroless static、nonroot ユーザで実行
# データ DB は /data/data.db に保存。デプロイ先で /data をボリューム化する想定。

# ===== Stage 1: web build =====
FROM node:22-bookworm-slim AS web

WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

# ===== Stage 2: go build =====
FROM golang:1.26-bookworm AS go

WORKDIR /app

# 依存だけ先に解決してキャッシュ効かせる
COPY backend/go.mod backend/go.sum ./
RUN go mod download

# backend ソース → embed 元のディレクトリへ web/dist 配置 → ビルド
COPY backend/ ./
COPY --from=web /web/dist ./internal/static/dist

RUN CGO_ENABLED=0 GOOS=linux \
    go build -trimpath -ldflags="-s -w" -o /out/server ./cmd/server

# ===== Stage 3: runtime =====
FROM gcr.io/distroless/static-debian12:nonroot

# /data は SQLite ファイルの置き場。ボリュームを推奨。
VOLUME ["/data"]
ENV DATABASE_PATH=/data/data.db
ENV PORT=8080

EXPOSE 8080
COPY --from=go /out/server /server

USER nonroot
ENTRYPOINT ["/server"]
