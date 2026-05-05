MOD_DIR     := mod
TEST_DIR    := mod.tests
BACKEND_DIR := backend
WEB_DIR     := web
EMBED_DIR   := $(BACKEND_DIR)/internal/static/dist
LOG_PATH    := $(HOME)/Library/Application Support/SlayTheSpire2/sts_stats.jsonl
LOG_FALLBACK := /tmp/sts_stats.jsonl

.PHONY: all test build install clean log \
        backend-dev backend-test backend-build backend-clean \
        web-dev web-install web-build \
        app-build app-run

# デフォルト: mod テスト → ビルド → インストール
all: test build install

# --- mod ---

test:
	cd $(TEST_DIR) && dotnet test --logger "console;verbosity=minimal"

build:
	cd $(MOD_DIR) && bash build.sh

install: build
	cd $(MOD_DIR) && bash install.sh

clean:
	rm -rf $(MOD_DIR)/dist $(MOD_DIR)/bin $(MOD_DIR)/obj $(MOD_DIR)/.godot
	rm -rf $(TEST_DIR)/bin $(TEST_DIR)/obj

# ゲームプレイ中のログをリアルタイム表示
log:
	@if [ -f "$(LOG_PATH)" ]; then \
		tail -f "$(LOG_PATH)"; \
	elif [ -f "$(LOG_FALLBACK)" ]; then \
		tail -f "$(LOG_FALLBACK)"; \
	else \
		echo "Log file not found. Start the game first."; \
		echo "Expected: $(LOG_PATH)"; \
	fi

# --- backend ---

backend-dev:
	cd $(BACKEND_DIR) && go run ./cmd/server

backend-test:
	cd $(BACKEND_DIR) && go test ./...

backend-build:
	cd $(BACKEND_DIR) && go build -o bin/server ./cmd/server

backend-clean:
	rm -rf $(BACKEND_DIR)/bin $(BACKEND_DIR)/data.db $(BACKEND_DIR)/data.db-shm $(BACKEND_DIR)/data.db-wal

# --- web (Svelte + Vite) ---

web-install:
	cd $(WEB_DIR) && npm install

web-dev:
	cd $(WEB_DIR) && npm run dev

web-build:
	cd $(WEB_DIR) && npm run build

# --- 統合: web + backend を1バイナリに ---
# 1. web をビルド
# 2. dist を backend の embed 元に同期（既存ファイルを掃除してから上書き）
# 3. go build で単一バイナリ生成
app-build: web-build
	@echo "[app-build] syncing $(WEB_DIR)/dist → $(EMBED_DIR)"
	@find $(EMBED_DIR) -mindepth 1 ! -name '.gitignore' -exec rm -rf {} + 2>/dev/null || true
	@cp -R $(WEB_DIR)/dist/. $(EMBED_DIR)/
	cd $(BACKEND_DIR) && go build -o bin/server ./cmd/server
	@echo "[app-build] → $(BACKEND_DIR)/bin/server"

# 統合バイナリで起動
app-run: app-build
	$(BACKEND_DIR)/bin/server
