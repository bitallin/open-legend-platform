#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

echo "=================================================================="
echo " Legend Platform — Phase A (M0) Automated Build & Verification"
echo "=================================================================="

echo "[1/4] Building Solution (LegendPlatform.sln)..."
dotnet build server/LegendPlatform.sln -c Release --nologo

echo ""
echo "[2/4] Running Automated xUnit Integration Tests (Legend.Tests)..."
dotnet test server/Legend.Tests/Legend.Tests.csproj -c Release --no-build --logger "console;verbosity=normal"

echo ""
echo "[3/4] Starting Legend.Host on TCP port 17100 with version-packs/sample-classic..."
HOST_LOG="/tmp/legend-host-m0.log"
rm -f "$HOST_LOG"
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --manifest "$ROOT_DIR/version-packs/sample-classic/manifest.json" \
  --port 17100 \
  --bind 127.0.0.1 > "$HOST_LOG" 2>&1 &
HOST_PID=$!

cleanup() {
  if kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

for i in $(seq 1 30); do
  if grep -q "Gate server listening" "$HOST_LOG" 2>/dev/null; then
    break
  fi
  sleep 0.1
done

if ! grep -q "Gate server listening" "$HOST_LOG"; then
  echo "ERROR: Legend.Host failed to start within timeout. Log output:"
  cat "$HOST_LOG"
  exit 1
fi

echo "Legend.Host started (PID=$HOST_PID). Running Legend.CliClient end-to-end smoke test..."
echo ""
echo "[4/4] Running Legend.CliClient multi-client smoke test against 127.0.0.1:17100..."
dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 \
  --port 17100

echo ""
echo "--- Legend.Host Structured Log Output ---"
cat "$HOST_LOG"
echo "-----------------------------------------"
echo "✅ Phase A (M0) Verification PASSED 100%!"
