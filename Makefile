# =============================================================================
#  sts2-multiplayer-statistics — タスクランナー
#  ターゲット一覧は `make help`
# =============================================================================

MOD_DIR        := mod
TEST_DIR       := mod.tests
BACKEND_DIR    := backend
WEB_DIR        := web
EMBED_DIR      := $(BACKEND_DIR)/internal/static/dist

# .env から STS2_MODS_DIR を取り出す（mod-use-local/public で使う）
ENV_FILE       := .env
STS2_MODS_DIR  := $(shell test -f $(ENV_FILE) && \
                  grep -E '^export STS2_MODS_DIR=' $(ENV_FILE) | sed -E 's/^[^"]*"([^"]*)".*/\1/' | \
                  sed -E "s|\\\$$HOME|$(HOME)|g")
CONFIG_FILE     := $(STS2_MODS_DIR)/StsStats/config.json
LOCAL_BACKEND_URL := http://localhost:8080

LOG_PATH       := $(HOME)/Library/Application Support/SlayTheSpire2/sts_stats.jsonl
DOCKER_IMAGE   := sts2stats
DOCKER_DATA_DIR := $(PWD)/.docker-data

.PHONY: help all test build install log \
        mod-use-local mod-use-public \
        backend-dev backend-test \
        web-dev \
        app-build app-run \
        docker-build docker-run \
        dump-catalog

# メンテナ専用ターゲット（fly-deploy 等）は Makefile.local に置いて gitignore する。
-include Makefile.local

help:
	@echo "make all              mod テスト → ビルド → インストール"
	@echo "make test             mod のユニットテスト"
	@echo "make log              JSONL ログを tail -f"
	@echo ""
	@echo "make mod-use-local    mod を localhost:8080 に向ける"
	@echo "make mod-use-public   mod を公開バックエンドに戻す"
	@echo ""
	@echo "make backend-dev      Go 開発サーバ起動 (:8080)"
	@echo "make backend-test     Go ユニットテスト"
	@echo "make web-dev          Vite dev server (:5173)"
	@echo ""
	@echo "make app-build        web → backend embed → 単一バイナリ"
	@echo "make app-run          上記 + 起動"
	@echo ""
	@echo "make docker-build     コンテナイメージ生成"
	@echo "make docker-run       :8080 で起動 (./.docker-data に永続化)"
	@echo ""
	@echo "make dump-catalog [LANG=ja]  STS2 アップデート時にカタログ更新"
	@echo "                              → STS2 を起動してランを 1 つ開始 → mod が dump"
	@echo "                              → web/public/catalog.\$$LANG.json にコピー"

# --- mod ----------------------------------------------------------------------

all: test build install

test:
	cd $(TEST_DIR) && dotnet test --logger "console;verbosity=minimal"

build:
	cd $(MOD_DIR) && bash build.sh

install: build
	cd $(MOD_DIR) && bash install.sh

log:
	tail -f "$(LOG_PATH)"

# --- mod 接続先切替（config.json の有無で制御） ------------------------------

mod-use-local:
	@test -n "$(STS2_MODS_DIR)" || (echo "STS2_MODS_DIR not set in .env" && exit 1)
	@echo '{ "backend_url": "$(LOCAL_BACKEND_URL)" }' > "$(CONFIG_FILE)"
	@echo "→ $(LOCAL_BACKEND_URL)（次回ゲーム起動時から反映）"

mod-use-public:
	@test -n "$(STS2_MODS_DIR)" || (echo "STS2_MODS_DIR not set in .env" && exit 1)
	@rm -f "$(CONFIG_FILE)"
	@echo "→ 公開バックエンド（次回ゲーム起動時から反映）"

# --- backend / web ------------------------------------------------------------

backend-dev:
	cd $(BACKEND_DIR) && go run ./cmd/server

backend-test:
	cd $(BACKEND_DIR) && go test ./...

web-dev:
	cd $(WEB_DIR) && npm run dev

# --- 統合バイナリ -------------------------------------------------------------
# web をビルドして backend の embed 配下に同期、go build で単一バイナリ生成。

app-build:
	cd $(WEB_DIR) && npm run build
	@find $(EMBED_DIR) -mindepth 1 ! -name '.gitignore' -exec rm -rf {} + 2>/dev/null || true
	@cp -R $(WEB_DIR)/dist/. $(EMBED_DIR)/
	cd $(BACKEND_DIR) && go build -o bin/server ./cmd/server

app-run: app-build
	$(BACKEND_DIR)/bin/server

# --- Docker -------------------------------------------------------------------

docker-build:
	docker build -t $(DOCKER_IMAGE):dev .

docker-run: docker-build
	mkdir -p $(DOCKER_DATA_DIR)
	docker run --rm --name $(DOCKER_IMAGE) \
		-p 8080:8080 \
		-v $(DOCKER_DATA_DIR):/data \
		$(DOCKER_IMAGE):dev

# --- catalog dump (STS2 アップデート時のみ) -----------------------------------
# 手順:
#   1. STS2 のゲーム内設定で言語を LANG (デフォルト ja) にしておく
#   2. `make dump-catalog [LANG=ja]` を実行
#   3. プロンプト指示通り STS2 を起動してランを 1 階だけでも開始
#   4. STS2 を閉じる
#   5. このターゲットが mod の dump 出力を web/public/catalog.$LANG.json にコピー
#   6. `git diff web/public/catalog.$LANG.json` で内容確認 → commit

LANG ?= ja
DUMP_SRC := $(STS2_MODS_DIR)/StsStats/catalog-dump.json
DUMP_DST := $(WEB_DIR)/public/catalog.$(LANG).json

dump-catalog:
	@test -n "$(STS2_MODS_DIR)" || (echo "STS2_MODS_DIR not set in .env" && exit 1)
	@echo "=== カタログ更新ワークフロー (lang=$(LANG)) ==="
	@echo ""
	@echo "1. STS2 のゲーム内言語を [$(LANG)] に設定済か確認してください"
	@echo "2. 環境変数 STS_STATS_DUMP_CATALOG=1 を立てて STS2 を起動してください:"
	@echo ""
	@echo "     STS_STATS_DUMP_CATALOG=1 open -a 'Slay the Spire 2'"
	@echo ""
	@echo "   (CatalogDumper はこの env var が立ってるときだけ動く)"
	@echo "3. 新規ランを 1 階 (Neow まで) 進めてください"
	@echo "4. STS2 を閉じてください"
	@echo "5. ここで Enter を押してください"
	@read _
	@test -f "$(DUMP_SRC)" || (echo "❌ dump file not found: $(DUMP_SRC)" && echo "   → STS_STATS_DUMP_CATALOG=1 を設定したか / mod が install されてるか / Neow まで進めたか確認" && exit 1)
	@mkdir -p $(WEB_DIR)/public
	@cp "$(DUMP_SRC)" "$(DUMP_DST)"
	@echo ""
	@echo "✅ コピー完了: $(DUMP_DST)"
	@echo ""
	@wc -c "$(DUMP_DST)" | awk '{printf "  raw size:  %s bytes\n", $$1}'
	@gzip -c "$(DUMP_DST)" | wc -c | awk '{printf "  gzip size: %s bytes\n", $$1}'
	@echo ""
	@echo "次:"
	@echo "  git diff $(DUMP_DST)"
	@echo "  git add $(DUMP_DST) && git commit -m 'chore: update catalog ($(LANG)) for STS2 v...'"
	@echo "  make deploy"

