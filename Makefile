MOD_DIR     := mod
TEST_DIR    := mod.tests
BACKEND_DIR := backend
LOG_PATH    := $(HOME)/Library/Application Support/SlayTheSpire2/sts_stats.jsonl
LOG_FALLBACK := /tmp/sts_stats.jsonl

.PHONY: all test build install clean log \
        backend-dev backend-test backend-build backend-clean

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
	rm -rf $(BACKEND_DIR)/bin $(BACKEND_DIR)/data.db
