MOD_DIR   := mod
TEST_DIR  := mod.tests
LOG_PATH  := $(HOME)/Library/Application Support/SlayTheSpire2/sts_stats.jsonl
LOG_FALLBACK := /tmp/sts_stats.jsonl

.PHONY: all test build install clean log

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
