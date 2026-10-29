#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT:-17173}"
DB_FILE="/tmp/legend-verify-m13-$$.db"
HOST_LOG_1="/tmp/legend-host-m13-1-$$.log"
HOST_LOG_2="/tmp/legend-host-m13-2-$$.log"
MARKER="/tmp/legend-m13-dungeon-marker.txt"
rm -f "$DB_FILE" "$HOST_LOG_1" "$HOST_LOG_2" "$MARKER"

cleanup() {
  if [[ -n "${HOST_PID:-}" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
  rm -f "$DB_FILE"
}
trap cleanup EXIT

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.15.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.15.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/6] Building Solution (Release) ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q

echo "=== [2/6] Sync Unity LegendProtocol.cs from Contracts ==="
GEN="$(find server/Legend.Contracts/obj/Release -name LegendProtocol.cs | head -1)"
if [[ -z "$GEN" ]]; then
  echo "ERROR: generated LegendProtocol.cs not found"
  exit 1
fi
cp "$GEN" client/unity-sdk/Runtime/LegendProtocol.cs

echo "=== [3/6] Running Unit & Integration Tests (M0–M13) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

fuser -k "${PORT}/tcp" 2>/dev/null || true
sleep 0.3

echo "=== [4/6] Starting Host #1 with SQLite ($DB_FILE) on port $PORT ==="
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --port "$PORT" \
  --manifest "version-packs/sample-classic/manifest.json" \
  --storage Sqlite \
  --sqlite "$DB_FILE" >"$HOST_LOG_1" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 40); do
  if grep -q "Listening on" "$HOST_LOG_1" 2>/dev/null; then
    break
  fi
  sleep 0.1
done
sleep 1

echo "=== [5/6] Running CLI M13 Dungeon Party/Daily Verification (Run #1) ==="
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --dungeon-party

echo "=== [6/6] Restarting Host #2 on same SQLite DB (usedToday persistence) ==="
kill "$HOST_PID" 2>/dev/null || true
wait "$HOST_PID" 2>/dev/null || true
unset HOST_PID

dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --port "$PORT" \
  --manifest "version-packs/sample-classic/manifest.json" \
  --storage Sqlite \
  --sqlite "$DB_FILE" >"$HOST_LOG_2" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 40); do
  if grep -q "Listening on" "$HOST_LOG_2" 2>/dev/null; then
    break
  fi
  sleep 0.1
done
sleep 1

dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --dungeon-party

echo "=== M13 VERIFICATION PASSED ==="
echo "Protocol: 1.15.0 | Storage: Sqlite at $DB_FILE"
