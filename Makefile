# =============================================================================
#  sts2-multiplayer-statistics — タスクランナー
#  各セクションは make help で一覧できる。
# =============================================================================

# --- 設定 ---------------------------------------------------------------------

MOD_DIR        := mod
TEST_DIR       := mod.tests
BACKEND_DIR    := backend
WEB_DIR        := web
EMBED_DIR      := $(BACKEND_DIR)/internal/static/dist

# .env から STS2_MODS_DIR を読み出す（mod-use-local 等で使う）
ENV_FILE       := .env
STS2_MODS_DIR  := $(shell test -f $(ENV_FILE) && \
                  grep -E '^export STS2_MODS_DIR=' $(ENV_FILE) | sed -E 's/^[^"]*"([^"]*)".*/\1/' | \
                  sed -E "s|\\\$$HOME|$(HOME)|g")
MOD_INSTALL_DIR := $(STS2_MODS_DIR)/StsStats
CONFIG_FILE     := $(MOD_INSTALL_DIR)/config.json

# ローカル開発時の backend URL（mod-use-local で書き込む）
LOCAL_BACKEND_URL := http://localhost:8080

# JSONL ログ
LOG_PATH       := $(HOME)/Library/Application Support/SlayTheSpire2/sts_stats.jsonl
LOG_FALLBACK   := /tmp/sts_stats.jsonl

# Docker
DOCKER_IMAGE   := sts2stats
DOCKER_TAG     := dev
DOCKER_DATA_DIR := $(PWD)/.docker-data

.PHONY: help \
        all test build install clean log \
        mod-use-local mod-use-public mod-status \
        backend-dev backend-test backend-build backend-clean \
        web-install web-dev web-build \
        app-build app-run \
        docker-build docker-run docker-stop \
        fly-deploy fly-logs fly-status fly-ssh

# --- ヘルプ -------------------------------------------------------------------

help:
	@echo "sts2-multiplayer-statistics — make ターゲット一覧"
	@echo ""
	@echo "  --- mod ---"
	@echo "  make all              テスト → ビルド → インストール"
	@echo "  make test             mod のユニットテスト"
	@echo "  make build            mod のビルドのみ"
	@echo "  make install          mod を STS2 の mods/ にコピー"
	@echo "  make clean            mod のビルド成果物を削除"
	@echo "  make log              JSONL ログを tail -f"
	@echo ""
	@echo "  --- mod 接続先切替 ---"
	@echo "  make mod-use-local    ローカル backend ($(LOCAL_BACKEND_URL)) に向ける"
	@echo "  make mod-use-public   公開 backend (DLL の既定値) に戻す"
	@echo "  make mod-status       現在の接続先を表示"
	@echo ""
	@echo "  --- backend ---"
	@echo "  make backend-dev      :8080 で開発サーバ起動"
	@echo "  make backend-test     Go ユニットテスト"
	@echo "  make backend-build    Go バイナリのみビルド"
	@echo "  make backend-clean    Go の bin / SQLite を削除"
	@echo ""
	@echo "  --- web ---"
	@echo "  make web-install      npm install"
	@echo "  make web-dev          Vite dev server (:5173)"
	@echo "  make web-build        本番ビルド (web/dist/)"
	@echo ""
	@echo "  --- 統合 ---"
	@echo "  make app-build        web → backend embed → 単一バイナリ"
	@echo "  make app-run          上記＋起動"
	@echo ""
	@echo "  --- Docker ---"
	@echo "  make docker-build     コンテナイメージ生成"
	@echo "  make docker-run       :8080 で起動 (./.docker-data に永続化)"
	@echo "  make docker-stop      停止"
	@echo ""
	@echo "  --- Fly.io ---"
	@echo "  make fly-deploy       本番デプロイ"
	@echo "  make fly-logs         リモートログ"
	@echo "  make fly-status       アプリ状態"
	@echo "  make fly-ssh          コンテナに SSH"

# =============================================================================
#  mod
# =============================================================================

# デフォルト: テスト → ビルド → インストール
all: test build install

test:
	cd $(TEST_DIR) && dotnet test --logger "console;verbosity=minimal"

build:
	cd $(MOD_DIR) && bash build.sh

install: build
	cd $(MOD_DIR) && bash install.sh

clean:
	rm -rf $(MOD_DIR)/dist $(MOD_DIR)/bin $(MOD_DIR)/obj $(MOD_DIR)/.godot
	rm -rf $(TEST_DIR)/bin $(TEST_DIR)/obj

# JSONL ログをリアルタイム表示
log:
	@if [ -f "$(LOG_PATH)" ]; then \
		tail -f "$(LOG_PATH)"; \
	elif [ -f "$(LOG_FALLBACK)" ]; then \
		tail -f "$(LOG_FALLBACK)"; \
	else \
		echo "Log file not found. Start the game first."; \
		echo "Expected: $(LOG_PATH)"; \
	fi

# --- mod 接続先切替 ----------------------------------------------------------
# config.json は mod の DLL と同じディレクトリに置くだけで効く（ゲーム再起動要）。

mod-use-local:
	@if [ -z "$(STS2_MODS_DIR)" ]; then \
		echo "ERROR: STS2_MODS_DIR が .env で設定されていません"; exit 1; \
	fi
	@if [ ! -d "$(MOD_INSTALL_DIR)" ]; then \
		echo "ERROR: mod 未インストール。先に 'make install' してください"; \
		echo "  expected: $(MOD_INSTALL_DIR)"; exit 1; \
	fi
	@echo '{ "backend_url": "$(LOCAL_BACKEND_URL)" }' > "$(CONFIG_FILE)"
	@echo "[mod-use-local] $(CONFIG_FILE) を作成 → $(LOCAL_BACKEND_URL)"
	@echo "次回ゲーム起動時から反映されます。"

mod-use-public:
	@if [ -z "$(STS2_MODS_DIR)" ]; then \
		echo "ERROR: STS2_MODS_DIR が .env で設定されていません"; exit 1; \
	fi
	@rm -f "$(CONFIG_FILE)"
	@echo "[mod-use-public] $(CONFIG_FILE) を削除 → DLL 既定値に戻す"
	@echo "次回ゲーム起動時から反映されます。"

mod-status:
	@if [ -z "$(STS2_MODS_DIR)" ]; then \
		echo "STS2_MODS_DIR が .env で設定されていません"; exit 1; \
	fi
	@echo "mod インストール先: $(MOD_INSTALL_DIR)"
	@if [ -f "$(CONFIG_FILE)" ]; then \
		echo "config.json: あり"; \
		echo "  $$(cat "$(CONFIG_FILE)")"; \
	else \
		echo "config.json: なし → DLL 既定の公開バックエンド (https://sts2stats.fly.dev) を使用"; \
	fi
	@echo ""
	@echo "直近のセッション URL:"
	@grep session_created "$(LOG_PATH)" 2>/dev/null | tail -1 || echo "  ログ未生成"

# =============================================================================
#  backend (Go)
# =============================================================================

backend-dev:
	cd $(BACKEND_DIR) && go run ./cmd/server

backend-test:
	cd $(BACKEND_DIR) && go test ./...

backend-build:
	cd $(BACKEND_DIR) && go build -o bin/server ./cmd/server

backend-clean:
	rm -rf $(BACKEND_DIR)/bin $(BACKEND_DIR)/data.db $(BACKEND_DIR)/data.db-shm $(BACKEND_DIR)/data.db-wal

# =============================================================================
#  web (Svelte + Vite)
# =============================================================================

web-install:
	cd $(WEB_DIR) && npm install

web-dev:
	cd $(WEB_DIR) && npm run dev

web-build:
	cd $(WEB_DIR) && npm run build

# =============================================================================
#  統合バイナリ (web を backend に embed)
# =============================================================================
# 1. web をビルド
# 2. dist を backend の embed 元に同期（既存ファイルを掃除してから上書き）
# 3. go build で単一バイナリ生成

app-build: web-build
	@echo "[app-build] syncing $(WEB_DIR)/dist → $(EMBED_DIR)"
	@find $(EMBED_DIR) -mindepth 1 ! -name '.gitignore' -exec rm -rf {} + 2>/dev/null || true
	@cp -R $(WEB_DIR)/dist/. $(EMBED_DIR)/
	cd $(BACKEND_DIR) && go build -o bin/server ./cmd/server
	@echo "[app-build] → $(BACKEND_DIR)/bin/server"

app-run: app-build
	$(BACKEND_DIR)/bin/server

# =============================================================================
#  Docker
# =============================================================================

docker-build:
	docker build -t $(DOCKER_IMAGE):$(DOCKER_TAG) .

# /data をホストの ./.docker-data にマウントして永続化、:8080 で公開
docker-run: docker-build
	mkdir -p $(DOCKER_DATA_DIR)
	docker run --rm --name $(DOCKER_IMAGE) \
		-p 8080:8080 \
		-v $(DOCKER_DATA_DIR):/data \
		$(DOCKER_IMAGE):$(DOCKER_TAG)

docker-stop:
	-docker stop $(DOCKER_IMAGE)

# =============================================================================
#  Fly.io
# =============================================================================

fly-deploy:
	fly deploy

fly-logs:
	fly logs

fly-status:
	fly status

fly-ssh:
	fly ssh console
