#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT:-17171}"
DB_FILE="/tmp/legend-verify-m11-$$.db"
HOST_LOG_1="/tmp/legend-host-m11-1-$$.log"
HOST_LOG_2="/tmp/legend-host-m11-2-$$.log"
CLI_LOG_2="/tmp/legend-cli-m11-2-$$.log"
MARKER="/tmp/legend-m11-mail-marker.txt"
rm -f "$DB_FILE" "$HOST_LOG_1" "$HOST_LOG_2" "$CLI_LOG_2" "$MARKER"

cleanup() {
  if [[ -n "${HOST_PID:-}" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
  rm -f "$DB_FILE" "$CLI_LOG_2"
}
trap cleanup EXIT

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.11.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.11.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/6] Building Solution (Release) ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q

echo "=== [2/6] Running Unit & Integration Tests (M0–M11) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

fuser -k "${PORT}/tcp" 2>/dev/null || true
sleep 0.3

echo "=== [3/6] Starting Host #1 with SQLite ($DB_FILE) on port $PORT ==="
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

echo "=== [4/6] Running CLI M11 Mail Verification (Run #1) ==="
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --mail

echo "=== [5/6] Restarting Host #2 on same SQLite DB (mail persistence) ==="
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

echo "=== [6/6] Running CLI M11 again after restart (claim persisted mail) ==="
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --mail | tee "$CLI_LOG_2"

if ! grep -q "Claimed persisted mail after restart" "$CLI_LOG_2"; then
  echo "ERROR: Expected persisted mail claim in Run #2 CLI output"
  cat "$CLI_LOG_2"
  exit 1
fi

if ! grep -q "mail_send" "$HOST_LOG_1"; then
  echo "ERROR: Expected Lua on_mail_send log in Host #1 log"
  cat "$HOST_LOG_1"
  exit 1
fi

if ! grep -q "mail_claim" "$HOST_LOG_1"; then
  echo "ERROR: Expected Lua on_mail_claim log in Host #1 log"
  cat "$HOST_LOG_1"
  exit 1
fi

echo "=== M11 VERIFICATION PASSED ==="
echo "Protocol: 1.11.0 | Storage: Sqlite at $DB_FILE"
