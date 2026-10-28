#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

echo "=================================================================="
echo " Legend Platform — Phase A (M3) NPC Shop / Quests"
echo "=================================================================="

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION || true)"
if [[ "$PROTO_VER" != "1.5.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.5.0, got '$PROTO_VER'"
  exit 1
fi

DOCKER_OK=0
if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then
    DOCKER_OK=1
  fi
fi

fuser -k 17310/tcp 17311/tcp 2>/dev/null || true
sleep 0.2

echo "[1/6] Building Solution..."
dotnet build server/LegendPlatform.sln -c Release --nologo

echo ""
echo "[2/6] Map validation CLI..."
dotnet run --project tools/Legend.MapValidate/Legend.MapValidate.csproj -c Release --no-build -- \
  "$ROOT_DIR/version-packs/sample-classic/manifest.json"

echo ""
echo "[3/6] Running xUnit M1+M2+M3 tests..."
if [[ "$DOCKER_OK" -eq 1 ]]; then
  echo "Docker available — bringing up Postgres+Redis..."
  (cd ops && docker compose up -d postgres redis) || true
  export LEGEND_PG="${LEGEND_PG:-Host=127.0.0.1;Port=5432;Database=legend_zone_01;Username=legend;Password=legend_dev_password}"
  export LEGEND_REDIS="${LEGEND_REDIS:-127.0.0.1:6379}"
  sleep 2
fi
dotnet test server/Legend.Tests/Legend.Tests.csproj -c Release --no-build --logger "console;verbosity=normal"

SQLITE_DB="$ROOT_DIR/data/verify-m3.db"
rm -f "$SQLITE_DB"
mkdir -p "$(dirname "$SQLITE_DB")"

echo ""
echo "[4/6] Starting Legend.Host (Sqlite) on 17310..."
HOST_LOG="/tmp/legend-host-m3.log"
rm -f "$HOST_LOG"
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --manifest "$ROOT_DIR/version-packs/sample-classic/manifest.json" \
  --port 17310 \
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

echo "[5/6] CliClient M3 full-chain (talk/buy/quest/kill/turn-in)..."
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port 17310

echo ""
echo "[6/6] Restart Host — gold/quests/inventory must survive..."
kill "$HOST_PID" 2>/dev/null || true
wait "$HOST_PID" 2>/dev/null || true
HOST_PID=""

HOST_LOG2="/tmp/legend-host-m3-re.log"
rm -f "$HOST_LOG2"
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --manifest "$ROOT_DIR/version-packs/sample-classic/manifest.json" \
  --port 17311 \
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

dotnet exec server/Legend.CliClient/bin/Release/net8.0/Legend.CliClient.dll --host 127.0.0.1 --port 17311

echo ""
echo "--- Host log (first run) ---"
tail -n 40 "$HOST_LOG"
echo "--- Host log (restart) ---"
tail -n 40 "$HOST_LOG2"
echo "-----------------------------------------"
echo "✅ Phase A (M3) Verification PASSED"
echo "Protocol: 1.5.0 | Storage: Sqlite at $SQLITE_DB"
echo "Manual: talk NPC at (12,12), buy potion, accept quest_slime_hunt, kill 2 green slimes ~ (25,10), turn in"
