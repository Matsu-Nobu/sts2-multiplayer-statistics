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

if [ ! -d "$SCRIPT_DIR/dist" ] || [ -z "$(ls -A "$SCRIPT_DIR/dist")" ]; then
    echo "[install] ERROR: dist/ is empty. Run build.sh first."
    exit 1
fi

TARGET="$STS2_MODS_DIR/StsStats"
mkdir -p "$TARGET"
cp -r "$SCRIPT_DIR/dist/"* "$TARGET/"

echo "[install] Installed to: $TARGET"
ls -lh "$TARGET"
