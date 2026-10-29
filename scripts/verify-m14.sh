#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT:-17174}"
DB_FILE="/tmp/legend-verify-m14-$$.db"
HOST_LOG="/tmp/legend-host-m14-$$.log"
rm -f "$DB_FILE" "$HOST_LOG"

cleanup() {
  if [[ -n "${HOST_PID:-}" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
  rm -f "$DB_FILE"
}
trap cleanup EXIT

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.17.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.17.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/5] Building Solution (Release) ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q

echo "=== [2/5] Sync Unity LegendProtocol.cs from Contracts ==="
GEN="$(find server/Legend.Contracts/obj/Release -name LegendProtocol.cs | head -1)"
if [[ -z "$GEN" ]]; then
  echo "ERROR: generated LegendProtocol.cs not found"
  exit 1
fi
cp "$GEN" client/unity-sdk/Runtime/LegendProtocol.cs

echo "=== [3/5] Running Unit & Integration Tests (M0–M14) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

fuser -k "${PORT}/tcp" 2>/dev/null || true
sleep 0.3

echo "=== [4/5] Starting Host with SQLite ($DB_FILE) on port $PORT ==="
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --port "$PORT" \
  --manifest "version-packs/sample-classic/manifest.json" \
  --storage Sqlite \
  --sqlite "$DB_FILE" >"$HOST_LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 40); do
  if grep -q "Listening on" "$HOST_LOG" 2>/dev/null; then
    break
  fi
  sleep 0.1
done
sleep 1

if ! kill -0 "$HOST_PID" 2>/dev/null; then
  echo "ERROR: Host failed to start"
  cat "$HOST_LOG" || true
  exit 1
fi

echo "=== [5/5] Running CLI M14 World Boss Verification ==="
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --boss

echo "=== M14 VERIFICATION PASSED ==="
