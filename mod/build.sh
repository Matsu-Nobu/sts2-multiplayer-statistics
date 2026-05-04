#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$ROOT_DIR/.env"

if [ ! -f "$ENV_FILE" ]; then
    echo "[build] ERROR: .env not found. Copy .env.example to .env and configure it."
    exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

if [ -z "$STS2_DATA_DIR" ]; then
    echo "[build] ERROR: STS2_DATA_DIR is not set in .env"
    exit 1
fi

echo "[build] STS2_DATA_DIR=$STS2_DATA_DIR"

cd "$SCRIPT_DIR"
dotnet build -c Debug 2>&1

rm -rf dist
mkdir -p dist

DLL_PATH=".godot/mono/temp/bin/Debug/StsStats.dll"
if [ ! -f "$DLL_PATH" ]; then
    DLL_PATH="bin/Debug/net9.0/StsStats.dll"
fi
if [ ! -f "$DLL_PATH" ]; then
    echo "[build] ERROR: DLL not found. Build may have failed."
    exit 1
fi

cp "$DLL_PATH" dist/StsStats.dll
cp StsStats.json dist/StsStats.json

echo "[build] Done:"
ls -lh dist/
