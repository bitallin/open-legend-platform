#!/usr/bin/env bash
# Publish the non-Unity playable client as a self-contained Windows x64 app.
# Output: dist/windows-player/LegendClient.exe
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet publish client/Legend.Playable/Legend.Playable.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o dist/windows-player

test -f dist/windows-player/LegendClient.exe
echo "OK: dist/windows-player/LegendClient.exe"
echo "Run: dist/windows-player/LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01"
