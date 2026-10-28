#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

echo "=================================================================="
echo " Legend Platform — Phase A (M2) Combat / Loot / Inventory"
echo "=================================================================="

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION || true)"
if [[ "$PROTO_VER" != "1.4.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.4.0, got '$PROTO_VER'"
  exit 1
fi

DOCKER_OK=0
if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then
    DOCKER_OK=1
  fi
fi

# Clear stale listeners from prior runs
fuser -k 17300/tcp 17301/tcp 2>/dev/null || true
sleep 0.2

echo "[1/6] Building Solution..."
dotnet build server/LegendPlatform.sln -c Release --nologo

echo ""
echo "[2/6] Map validation CLI..."
dotnet run --project tools/Legend.MapValidate/Legend.MapValidate.csproj -c Release --no-build -- \
  "$ROOT_DIR/version-packs/sample-classic/manifest.json"

echo ""
echo "[3/6] Running xUnit M1+M2 tests (InMemory + Sqlite; Postgres if LEGEND_PG set)..."
if [[ "$DOCKER_OK" -eq 1 ]]; then
  echo "Docker available — bringing up Postgres+Redis for integration path..."
  (cd ops && docker compose up -d postgres redis) || true
  export LEGEND_PG="${LEGEND_PG:-Host=127.0.0.1;Port=5432;Database=legend_zone_01;Username=legend;Password=legend_dev_password}"
  export LEGEND_REDIS="${LEGEND_REDIS:-127.0.0.1:6379}"
  sleep 2
fi
dotnet test server/Legend.Tests/Legend.Tests.csproj -c Release --no-build --logger "console;verbosity=normal"

SQLITE_DB="$ROOT_DIR/data/verify-m2.db"
rm -f "$SQLITE_DB"
mkdir -p "$(dirname "$SQLITE_DB")"

echo ""
echo "[4/6] Starting Legend.Host (Sqlite) on 17300..."
HOST_LOG="/tmp/legend-host-m2.log"
rm -f "$HOST_LOG"
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --manifest "$ROOT_DIR/version-packs/sample-classic/manifest.json" \
  --port 17300 \
  --bind 127.0.0.1 \
  --sqlite "$SQLITE_DB" > "$HOST_LOG" 2>&1 &
HOST_PID=$!

cleanup() {
  if [[ -n "${HOST_PID:-}" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

for i in $(seq 1 50); do
  if grep -q "Gate server listening" "$HOST_LOG" 2>/dev/null; then
    break
  fi
  sleep 0.1
done
if ! grep -q "Gate server listening" "$HOST_LOG"; then
  echo "ERROR: Host failed to start. Log:"
  cat "$HOST_LOG"
  exit 1
fi

echo "[5/6] CliClient M2 full-chain (AOI + combat kill/loot/pickup)..."
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port 17300

echo ""
echo "[6/6] Restart Host — inventory must survive Sqlite reopen..."
kill "$HOST_PID" 2>/dev/null || true
wait "$HOST_PID" 2>/dev/null || true
HOST_PID=""

HOST_LOG2="/tmp/legend-host-m2-re.log"
rm -f "$HOST_LOG2"
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --manifest "$ROOT_DIR/version-packs/sample-classic/manifest.json" \
  --port 17301 \
  --bind 127.0.0.1 \
  --sqlite "$SQLITE_DB" > "$HOST_LOG2" 2>&1 &
HOST_PID=$!

for i in $(seq 1 50); do
  if grep -q "Gate server listening" "$HOST_LOG2" 2>/dev/null; then
    break
  fi
  sleep 0.1
done
if ! grep -q "Gate server listening" "$HOST_LOG2"; then
  echo "ERROR: Host restart failed. Log:"
  cat "$HOST_LOG2"
  exit 1
fi

# Second smoke: register may fail (taken) but login+combat path still runs
dotnet exec server/Legend.CliClient/bin/Release/net8.0/Legend.CliClient.dll --host 127.0.0.1 --port 17301

echo ""
echo "--- Host log (first run) ---"
tail -n 40 "$HOST_LOG"
echo "--- Host log (restart) ---"
tail -n 40 "$HOST_LOG2"
echo "-----------------------------------------"
echo "✅ Phase A (M2) Verification PASSED"
echo "Protocol: 1.4.0 | Storage: Sqlite at $SQLITE_DB"
echo "Rules: safe-zone bans ALL attacks; killer loot lock 15s then FFA; pickup range≤2"
echo "Manual fight: leave safe (5..18), walk to slime ~ (25,10), Attack power_strike, Pickup"
