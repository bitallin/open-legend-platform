#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT1="${LEGEND_PORT1:-17190}"
ADMIN1="${LEGEND_ADMIN_PORT1:-17191}"
PORT2="${LEGEND_PORT2:-17192}"
ADMIN2="${LEGEND_ADMIN_PORT2:-17193}"
ADMIN_TOKEN="${LEGEND_ADMIN_TOKEN:-cd-verify-token}"
PAY_SECRET="${LEGEND_PAYMENT_SECRET:-cd-verify-pay-secret}"
DB1="/tmp/legend-verify-cd-r1-$$.db"
DB2="/tmp/legend-verify-cd-r2-$$.db"
MERGED="/tmp/legend-verify-cd-merged-$$.db"
LOG1="/tmp/legend-host-cd1-$$.log"
LOG2="/tmp/legend-host-cd2-$$.log"
SEED_LOG="/tmp/legend-cd-seed-$$.log"
CFG1="/tmp/legend-cd-cfg1-$$.json"
CFG2="/tmp/legend-cd-cfg2-$$.json"
rm -f "$DB1" "$DB2" "$MERGED" "$LOG1" "$LOG2" "$SEED_LOG" "$CFG1" "$CFG2"

HOST1_PID=""
HOST2_PID=""
SEED_PID=""
cleanup() {
  if [[ -n "${SEED_PID}" ]] && kill -0 "$SEED_PID" 2>/dev/null; then
    kill "$SEED_PID" 2>/dev/null || true
    wait "$SEED_PID" 2>/dev/null || true
  fi
  if [[ -n "${HOST1_PID}" ]] && kill -0 "$HOST1_PID" 2>/dev/null; then
    kill "$HOST1_PID" 2>/dev/null || true
    wait "$HOST1_PID" 2>/dev/null || true
  fi
  if [[ -n "${HOST2_PID}" ]] && kill -0 "$HOST2_PID" 2>/dev/null; then
    kill "$HOST2_PID" 2>/dev/null || true
    wait "$HOST2_PID" 2>/dev/null || true
  fi
  rm -f "$DB1" "$DB2" "$MERGED" "$CFG1" "$CFG2"
}
trap cleanup EXIT

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/8] Building Solution (Release) ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q
dotnet build tools/cd-phase-seed/cd-phase-seed.csproj -c Release --nologo -v q

echo "=== [2/8] Sync Unity LegendProtocol.cs from Contracts ==="
GEN="$(find server/Legend.Contracts/obj/Release -name LegendProtocol.cs | head -1)"
if [[ -z "$GEN" ]]; then
  echo "ERROR: generated LegendProtocol.cs not found"
  exit 1
fi
cp "$GEN" client/unity-sdk/Runtime/LegendProtocol.cs

echo "=== [3/8] Unit & Integration Tests ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

echo "=== [4/8] create-realm script smoke ==="
TMP_REALM="realm-verify-cd-$$"
bash scripts/create-realm.sh "$TMP_REALM" "验收临时区" 17300 17301
test -f "realms/${TMP_REALM}.json"
grep -q "$TMP_REALM" realms/catalog.json
rm -f "realms/${TMP_REALM}.json"
python3 - << PY
import json
from pathlib import Path
root = Path("realms")
files = sorted([p.name for p in root.glob("*.json") if p.name != "catalog.json"])
(root / "catalog.json").write_text(json.dumps({"realms": files}, ensure_ascii=False, indent=2) + "\n")
print("catalog restored:", files)
PY

fuser -k "${PORT1}/tcp" 2>/dev/null || true
fuser -k "${ADMIN1}/tcp" 2>/dev/null || true
fuser -k "${PORT2}/tcp" 2>/dev/null || true
fuser -k "${ADMIN2}/tcp" 2>/dev/null || true
sleep 0.3

echo "=== [5/8] Start 2 realms in parallel ==="
cat > "$CFG1" << JSON
{
  "realmId": "realm-classic-01",
  "realmName": "自研先锋一区",
  "host": "127.0.0.1",
  "port": ${PORT1},
  "adminPort": ${ADMIN1},
  "sqlitePath": "${DB1}",
  "manifestPath": "version-packs/sample-classic/manifest.json",
  "status": "OPEN",
  "versionPackId": "sample-classic-1.21.0"
}
JSON
cat > "$CFG2" << JSON
{
  "realmId": "realm-classic-02",
  "realmName": "自研先锋二区",
  "host": "127.0.0.1",
  "port": ${PORT2},
  "adminPort": ${ADMIN2},
  "sqlitePath": "${DB2}",
  "manifestPath": "version-packs/sample-classic/manifest.json",
  "status": "OPEN",
  "versionPackId": "sample-classic-1.21.0"
}
JSON

dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --realm-config "$CFG1" --realm-catalog realms \
  --admin-token "$ADMIN_TOKEN" --admin-bind 127.0.0.1 \
  --payment-secret "$PAY_SECRET" >"$LOG1" 2>&1 &
HOST1_PID=$!

dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --realm-config "$CFG2" --realm-catalog realms \
  --admin-token "$ADMIN_TOKEN" --admin-bind 127.0.0.1 \
  --payment-secret "$PAY_SECRET" >"$LOG2" 2>&1 &
HOST2_PID=$!

for _ in $(seq 1 80); do
  if grep -q "Admin API listening" "$LOG1" 2>/dev/null && grep -q "Admin API listening" "$LOG2" 2>/dev/null; then
    break
  fi
  sleep 0.15
done
sleep 0.5

if ! kill -0 "$HOST1_PID" 2>/dev/null || ! kill -0 "$HOST2_PID" 2>/dev/null; then
  echo "ERROR: one or both hosts failed"
  echo "--- LOG1 ---"; cat "$LOG1" || true
  echo "--- LOG2 ---"; cat "$LOG2" || true
  exit 1
fi

AUTH=(-H "Authorization: Bearer ${ADMIN_TOKEN}")
curl -sf "${AUTH[@]}" "http://127.0.0.1:${ADMIN1}/admin/online" | tee /tmp/cd-online1.json
grep -q '"ok":true' /tmp/cd-online1.json
curl -sf "${AUTH[@]}" "http://127.0.0.1:${ADMIN2}/admin/online" | tee /tmp/cd-online2.json
grep -q '"ok":true' /tmp/cd-online2.json
echo "two realms listening: ${PORT1}/${PORT2}"

echo "=== [6/8] Payment create → webhook fulfill (realm1) ==="
dotnet run --project tools/cd-phase-seed/cd-phase-seed.csproj -c Release --no-build -- \
  127.0.0.1 "$PORT1" cdverify --stay --pay-secret "$PAY_SECRET" >"$SEED_LOG" 2>&1 &
SEED_PID=$!

for _ in $(seq 1 80); do
  if grep -q '^ORDER=' "$SEED_LOG" 2>/dev/null; then
    break
  fi
  if ! kill -0 "$SEED_PID" 2>/dev/null; then
    echo "ERROR: seed exited early"
    cat "$SEED_LOG" || true
    exit 1
  fi
  sleep 0.1
done
if ! grep -q '^ORDER=' "$SEED_LOG"; then
  echo "ERROR: seed did not create order"
  cat "$SEED_LOG" || true
  exit 1
fi
cat "$SEED_LOG"

ORDER="$(grep '^ORDER=' "$SEED_LOG" | head -1 | cut -d= -f2)"
TXN="$(grep '^TXN=' "$SEED_LOG" | head -1 | cut -d= -f2)"
SIG="$(grep '^SIG=' "$SEED_LOG" | head -1 | cut -d= -f2)"
STATUS="$(grep '^STATUS=' "$SEED_LOG" | head -1 | cut -d= -f2)"

# bad signature
CODE="$(curl -s -o /tmp/cd-wh-bad.json -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' \
  -d "{\"orderId\":\"${ORDER}\",\"providerTxnId\":\"${TXN}\",\"status\":\"${STATUS}\"}" \
  "http://127.0.0.1:${ADMIN1}/admin/payment/webhook" || true)"
if [[ "$CODE" != "401" ]]; then
  echo "ERROR: expected 401 bad signature, got $CODE"
  cat /tmp/cd-wh-bad.json || true
  exit 1
fi

curl -sf -X POST \
  -H 'Content-Type: application/json' \
  -H "X-Payment-Signature: ${SIG}" \
  -d "{\"orderId\":\"${ORDER}\",\"providerTxnId\":\"${TXN}\",\"status\":\"${STATUS}\"}" \
  "http://127.0.0.1:${ADMIN1}/admin/payment/webhook" | tee /tmp/cd-wh-ok.json
grep -q '"ok":true' /tmp/cd-wh-ok.json
grep -q 'Fulfilled' /tmp/cd-wh-ok.json

# idempotent replay
curl -sf -X POST \
  -H 'Content-Type: application/json' \
  -H "X-Payment-Signature: ${SIG}" \
  -d "{\"orderId\":\"${ORDER}\",\"providerTxnId\":\"${TXN}\",\"status\":\"${STATUS}\"}" \
  "http://127.0.0.1:${ADMIN1}/admin/payment/webhook" | tee /tmp/cd-wh-replay.json
grep -q '"ok":true' /tmp/cd-wh-replay.json

# admin recharge
curl -sf -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d '{"characterName":"CdVerify","productId":"gold_100"}' \
  "http://127.0.0.1:${ADMIN1}/admin/recharge" | tee /tmp/cd-recharge.json
grep -q '"ok":true' /tmp/cd-recharge.json
grep -q 'Fulfilled' /tmp/cd-recharge.json

# seed on realm2 so both DBs have data for merge
dotnet run --project tools/cd-phase-seed/cd-phase-seed.csproj -c Release --no-build -- \
  127.0.0.1 "$PORT2" cdverify2 --pay-secret "$PAY_SECRET" | tee /tmp/cd-seed2.txt
grep -q '^OK ' /tmp/cd-seed2.txt

# stop hosts before merge (release sqlite locks)
kill "$SEED_PID" 2>/dev/null || true
wait "$SEED_PID" 2>/dev/null || true
SEED_PID=""
kill "$HOST1_PID" 2>/dev/null || true
kill "$HOST2_PID" 2>/dev/null || true
wait "$HOST1_PID" 2>/dev/null || true
wait "$HOST2_PID" 2>/dev/null || true
HOST1_PID=""
HOST2_PID=""
sleep 0.3

echo "=== [7/8] Legend.Merge dry-run + real + verify counts ==="
test -f "$DB1"
test -f "$DB2"

dotnet run --project tools/Legend.Merge/Legend.Merge.csproj -c Release --no-build -- \
  --source-a "$DB1" --source-b "$DB2" --target "$MERGED" --dry-run | tee /tmp/cd-merge-dry.json
python3 - << PY
import json
d=json.load(open("/tmp/cd-merge-dry.json"))
assert d.get("Ok") is True, d
assert d.get("DryRun") is True
assert int(d.get("AccountsMerged",0)) >= 1
print("dry-run ok", d.get("AccountsMerged"), d.get("CharactersWritten"))
PY
test ! -f "$MERGED"

dotnet run --project tools/Legend.Merge/Legend.Merge.csproj -c Release --no-build -- \
  --source-a "$DB1" --source-b "$DB2" --target "$MERGED" | tee /tmp/cd-merge-real.json
python3 - << PY
import json, sqlite3
d=json.load(open("/tmp/cd-merge-real.json"))
assert d.get("Ok") is True, d
assert d.get("DryRun") is False
conn=sqlite3.connect("$MERGED")
acc=conn.execute("select count(*) from accounts").fetchone()[0]
ch=conn.execute("select count(*) from characters").fetchone()[0]
print("merged counts accounts", acc, "chars", ch)
assert acc == int(d["AccountsMerged"])
assert ch == int(d["CharactersWritten"])
assert acc >= 2
assert ch >= 2
PY

echo "=== [8/8] Realm catalog sanity ==="
test -f realms/catalog.json
test -f realms/realm-classic-01.json
test -f realms/realm-classic-02.json
test -f tools/Legend.Merge/MERGE_RULES.md
test -f scripts/create-realm.sh

echo "=== PHASE C/D VERIFICATION PASSED ==="
