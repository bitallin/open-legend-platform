#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT:-17167}"
DB_FILE="/tmp/legend-verify-m7-$$.db"
HOST_LOG_1="/tmp/legend-host-m7-1-$$.log"
HOST_LOG_2="/tmp/legend-host-m7-2-$$.log"
rm -f "$DB_FILE" "$HOST_LOG_1" "$HOST_LOG_2"

cleanup() {
  if [[ -n "${HOST_PID:-}" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
  rm -f "$DB_FILE"
}
trap cleanup EXIT

echo "=== [1/6] Building Solution (Release) ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q

echo "=== [2/6] Running Unit & Integration Tests (M0 + M1 + M2 + M3 + M4 + M5 + M6 + M7) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

echo "=== [3/6] Starting Host #1 with SQLite ($DB_FILE) on port $PORT ==="
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --port "$PORT" \
  --pack "version-packs/sample-classic/manifest.json" \
  --storage Sqlite \
  --sqlite-path "$DB_FILE" >"$HOST_LOG_1" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 40); do
  if grep -q "Listening on" "$HOST_LOG_1" 2>/dev/null; then
    break
  fi
  sleep 0.1
done

echo "=== [4/6] Running CLI M7 Stall/Trade Verification (Run #1: SafeZone + List + Cancel + Buy + Leave 1 Active) ==="
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --trade

echo "=== [5/6] Restarting Host #2 on same SQLite DB (Verify Active Trade Listing Persists) ==="
kill "$HOST_PID" 2>/dev/null || true
wait "$HOST_PID" 2>/dev/null || true
unset HOST_PID

dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --port "$PORT" \
  --pack "version-packs/sample-classic/manifest.json" \
  --storage Sqlite \
  --sqlite-path "$DB_FILE" >"$HOST_LOG_2" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 40); do
  if grep -q "Listening on" "$HOST_LOG_2" 2>/dev/null; then
    break
  fi
  sleep 0.1
done

echo "=== [6/6] Running CLI M7 Stall/Trade Verification (Run #2: Post-Restart Browse + Buy Persisted Listing) ==="
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --trade

if ! grep -q "stall_list" "$HOST_LOG_1"; then
  echo "ERROR: Expected Lua on_stall_list log in Host #1 log"
  cat "$HOST_LOG_1"
  exit 1
fi

if ! grep -q "stall_buy" "$HOST_LOG_1"; then
  echo "ERROR: Expected Lua on_stall_buy log in Host #1 log"
  cat "$HOST_LOG_1"
  exit 1
fi

echo "=== M7 VERIFICATION PASSED ==="
