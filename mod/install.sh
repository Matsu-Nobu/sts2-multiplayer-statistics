#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$ROOT_DIR/.env"

if [ ! -f "$ENV_FILE" ]; then
    echo "[install] ERROR: .env not found. Copy .env.example to .env and configure it."
    exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

if [ -z "$STS2_MODS_DIR" ]; then
    echo "[install] ERROR: STS2_MODS_DIR is not set in .env"
    exit 1
fi

# ゲームの起動中は入れ替えない。起動中に StsStats.dll を上書きすると、まだ読み込まれていない処理が
# 壊れた中身で読み込まれ (BadImageFormatException: Bad IL range)、ゲームが止まる (2026-10-03、クリア時にフリーズ)。
if pgrep -f "SlayTheSpire2.app/Contents/MacOS" >/dev/null 2>&1; then
    echo "[install] ERROR: Slay the Spire 2 が起動中です。ゲームを閉じてから install してください (build は済んでいます)。"
    exit 1
fi

if [ ! -d "$SCRIPT_DIR/dist" ] || [ -z "$(ls -A "$SCRIPT_DIR/dist")" ]; then
    echo "[install] ERROR: dist/ is empty. Run build.sh first."
    exit 1
fi

TARGET="$STS2_MODS_DIR/StsStats"
mkdir -p "$TARGET"
cp -r "$SCRIPT_DIR/dist/"* "$TARGET/"

echo "[install] Installed to: $TARGET"
ls -lh "$TARGET"
