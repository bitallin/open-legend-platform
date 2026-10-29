#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT:-17179}"
ADMIN_PORT="${LEGEND_ADMIN_PORT:-18179}"
ADMIN_TOKEN="${LEGEND_ADMIN_TOKEN:-m19-verify-token}"
DB_FILE="/tmp/legend-verify-m19-$$.db"
HOST_LOG="/tmp/legend-host-m19-$$.log"
SEED_LOG="/tmp/legend-seed-m19-$$.log"
rm -f "$DB_FILE" "$HOST_LOG" "$SEED_LOG"

cleanup() {
  if [[ -n "${SEED_PID:-}" ]] && kill -0 "$SEED_PID" 2>/dev/null; then
    kill "$SEED_PID" 2>/dev/null || true
    wait "$SEED_PID" 2>/dev/null || true
  fi
  if [[ -n "${HOST_PID:-}" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
  rm -f "$DB_FILE"
}
trap cleanup EXIT

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/6] Building Solution (Release) ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q
dotnet build tools/m19-admin-seed/m19-admin-seed.csproj -c Release --nologo -v q

echo "=== [2/6] Sync Unity LegendProtocol.cs from Contracts ==="
GEN="$(find server/Legend.Contracts/obj/Release -name LegendProtocol.cs | head -1)"
if [[ -z "$GEN" ]]; then
  echo "ERROR: generated LegendProtocol.cs not found"
  exit 1
fi
cp "$GEN" client/unity-sdk/Runtime/LegendProtocol.cs

echo "=== [3/6] Running Unit & Integration Tests (M0–M19) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

fuser -k "${PORT}/tcp" 2>/dev/null || true
fuser -k "${ADMIN_PORT}/tcp" 2>/dev/null || true
sleep 0.3

echo "=== [4/6] Starting Host with Admin API on $ADMIN_PORT ==="
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --port "$PORT" \
  --manifest "version-packs/sample-classic/manifest.json" \
  --storage Sqlite \
  --sqlite "$DB_FILE" \
  --admin-port "$ADMIN_PORT" \
  --admin-token "$ADMIN_TOKEN" \
  --admin-bind "127.0.0.1" >"$HOST_LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 50); do
  if grep -q "Admin API listening" "$HOST_LOG" 2>/dev/null; then
    break
  fi
  sleep 0.1
done
sleep 0.5

if ! kill -0 "$HOST_PID" 2>/dev/null; then
  echo "ERROR: Host failed to start"
  cat "$HOST_LOG" || true
  exit 1
fi

AUTH=(-H "Authorization: Bearer ${ADMIN_TOKEN}")

echo "=== [5/6] curl Admin API smoke ==="
CODE="$(curl -s -o /tmp/m19-unauth.json -w '%{http_code}' "http://127.0.0.1:${ADMIN_PORT}/admin/online" || true)"
if [[ "$CODE" != "401" ]]; then
  echo "ERROR: expected 401 without token, got $CODE"
  cat /tmp/m19-unauth.json || true
  exit 1
fi

curl -sf "${AUTH[@]}" "http://127.0.0.1:${ADMIN_PORT}/admin/online" | tee /tmp/m19-online.json
grep -q '"ok":true' /tmp/m19-online.json

curl -sf -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d '{"action":"start"}' "http://127.0.0.1:${ADMIN_PORT}/admin/siege" | tee /tmp/m19-siege-start.json
grep -q '"ok":true' /tmp/m19-siege-start.json
grep -q '"isActive":true' /tmp/m19-siege-start.json

curl -sf -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d '{"action":"stop"}' "http://127.0.0.1:${ADMIN_PORT}/admin/siege" | tee /tmp/m19-siege-stop.json
grep -q '"ok":true' /tmp/m19-siege-stop.json
grep -q '"isActive":false' /tmp/m19-siege-stop.json

curl -sf -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d '{}' "http://127.0.0.1:${ADMIN_PORT}/admin/reload-pack" | tee /tmp/m19-reload.json
grep -q '"ok":true' /tmp/m19-reload.json

CODE="$(curl -s -o /tmp/m19-ban-miss.json -w '%{http_code}' -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d '{"username":"nobody_m19_xyz","banned":true}' "http://127.0.0.1:${ADMIN_PORT}/admin/ban" || true)"
if [[ "$CODE" != "400" ]]; then
  echo "ERROR: expected 400 for missing account ban, got $CODE"
  cat /tmp/m19-ban-miss.json || true
  exit 1
fi

echo "=== [6/6] Seed account + curl online/mute/ban ==="
dotnet run --project tools/m19-admin-seed/m19-admin-seed.csproj -c Release --no-build -- \
  127.0.0.1 "$PORT" m19curluser --stay >"$SEED_LOG" 2>&1 &
SEED_PID=$!

for _ in $(seq 1 60); do
  if grep -q '^OK ' "$SEED_LOG" 2>/dev/null; then
    break
  fi
  if ! kill -0 "$SEED_PID" 2>/dev/null; then
    echo "ERROR: seed exited early"
    cat "$SEED_LOG" || true
    exit 1
  fi
  sleep 0.1
done
if ! grep -q '^OK ' "$SEED_LOG"; then
  echo "ERROR: seed did not become ready"
  cat "$SEED_LOG" || true
  exit 1
fi
cat "$SEED_LOG"

curl -sf "${AUTH[@]}" "http://127.0.0.1:${ADMIN_PORT}/admin/online" | tee /tmp/m19-online2.json
python3 -c 'import json; d=json.load(open("/tmp/m19-online2.json")); assert d.get("ok") is True; assert int(d.get("online",0))>=1, d; print("online ok", d["online"])'

curl -sf -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d '{"username":"m19curluser","muted":true,"reason":"verify"}' \
  "http://127.0.0.1:${ADMIN_PORT}/admin/mute" | tee /tmp/m19-mute.json
grep -q '"muted":true' /tmp/m19-mute.json

curl -sf -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d '{"username":"m19curluser","banned":true,"reason":"verify"}' \
  "http://127.0.0.1:${ADMIN_PORT}/admin/ban" | tee /tmp/m19-ban.json
grep -q '"banned":true' /tmp/m19-ban.json

echo "=== M19 VERIFICATION PASSED ==="
