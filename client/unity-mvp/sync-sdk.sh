#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$ROOT/../.." && pwd)"
SRC="$REPO/client/unity-sdk/Runtime"
DST="$ROOT/Assets/LegendSdk/Runtime"
mkdir -p "$DST"
# Clear previous synced cs (keep .meta if Unity created any)
find "$DST" -maxdepth 1 -name '*.cs' -delete 2>/dev/null || true
cp -f "$SRC"/*.cs "$DST/"
cat > "$ROOT/Assets/LegendSdk/README_IMPORT.md" << 'MD'
# LegendSdk

Synced from `client/unity-sdk/Runtime/` via `./sync-sdk.sh`.

Do **not** edit copies here — edit `client/unity-sdk/` then re-run sync.
Protocol version: see `ProtocolConstants.CurrentVersion` (must match `protocol/VERSION`).
MD

# Keep ClientCore FSM copies in sync too
CORE_SRC="$REPO/client/Legend.ClientCore"
CORE_DST="$ROOT/Assets/LegendClient/Scripts/ClientCore"
mkdir -p "$CORE_DST"
find "$CORE_DST" -maxdepth 1 -name '*.cs' -delete 2>/dev/null || true
cp -f "$CORE_SRC"/*.cs "$CORE_DST/"

echo "Synced SDK → Assets/LegendSdk/Runtime"
echo "Synced ClientCore → Assets/LegendClient/Scripts/ClientCore"
