#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT_E:-17400}"
ADMIN="${LEGEND_ADMIN_PORT_E:-17401}"
ADMIN_TOKEN="${LEGEND_ADMIN_TOKEN:-e-verify-token}"
PAY_SECRET="${LEGEND_PAYMENT_SECRET:-e-verify-pay-secret}"
DB="/tmp/legend-verify-e-$$.db"
LOG="/tmp/legend-host-e-$$.log"
SEED_LOG="/tmp/legend-e-seed-$$.log"
CFG="/tmp/legend-e-cfg-$$.json"
rm -f "$DB" "$LOG" "$SEED_LOG" "$CFG"

HOST_PID=""
SEED_PID=""
cleanup() {
  if [[ -n "${SEED_PID}" ]] && kill -0 "$SEED_PID" 2>/dev/null; then
    kill "$SEED_PID" 2>/dev/null || true
    wait "$SEED_PID" 2>/dev/null || true
  fi
  if [[ -n "${HOST_PID}" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
  rm -f "$DB" "$CFG"
}
trap cleanup EXIT

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/6] Build ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q
dotnet build tools/phase-e-payment-seed/phase-e-payment-seed.csproj -c Release --nologo -v q
dotnet build tools/payment-sandbox-sim/payment-sandbox-sim.csproj -c Release --nologo -v q

echo "=== [2/6] Sync Unity LegendProtocol.cs ==="
GEN="$(find server/Legend.Contracts/obj/Release -name LegendProtocol.cs | head -1)"
test -n "$GEN"
cp "$GEN" client/unity-sdk/Runtime/LegendProtocol.cs

echo "=== [3/6] Unit & Integration Tests (incl. Phase E) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo --filter "FullyQualifiedName~PhaseEPayment|FullyQualifiedName~PhaseCd"

echo "=== [4/6] Start Host (mock active; wechat/alipay sim always on) ==="
fuser -k "${PORT}/tcp" 2>/dev/null || true
fuser -k "${ADMIN}/tcp" 2>/dev/null || true
sleep 0.2

cat > "$CFG" << JSON
{
  "realmId": "realm-classic-01",
  "realmName": "PhaseE验收区",
  "host": "127.0.0.1",
  "port": ${PORT},
  "adminPort": ${ADMIN},
  "sqlitePath": "${DB}",
  "manifestPath": "version-packs/sample-classic/manifest.json",
  "status": "OPEN",
  "versionPackId": "sample-classic-1.21.0"
}
JSON

dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release --no-build -- \
  --realm-config "$CFG" --realm-catalog realms \
  --admin-token "$ADMIN_TOKEN" --admin-bind 127.0.0.1 \
  --payment-secret "$PAY_SECRET" --payment-provider mock \
  >"$LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 80); do
  if grep -q "Admin API listening" "$LOG" 2>/dev/null; then break; fi
  sleep 0.15
done
if ! kill -0 "$HOST_PID" 2>/dev/null; then
  echo "ERROR: host failed"; cat "$LOG"; exit 1
fi
grep -q 'sandbox_sim' "$LOG" || true
echo "host up on $PORT admin $ADMIN"

AUTH=(-H "Authorization: Bearer ${ADMIN_TOKEN}")

echo "=== [5/6] Seed 3 orders + wechat/alipay/mock notifies ==="
dotnet run --project tools/phase-e-payment-seed/phase-e-payment-seed.csproj -c Release --no-build -- \
  127.0.0.1 "$PORT" phaseeverify --stay --pay-secret "$PAY_SECRET" --count 3 >"$SEED_LOG" 2>&1 &
SEED_PID=$!

for _ in $(seq 1 80); do
  if grep -q '^ORDER3=' "$SEED_LOG" 2>/dev/null; then break; fi
  if ! kill -0 "$SEED_PID" 2>/dev/null; then
    echo "ERROR: seed exited early"; cat "$SEED_LOG"; exit 1
  fi
  sleep 0.1
done
cat "$SEED_LOG"
ORDER1="$(grep '^ORDER1=' "$SEED_LOG" | head -1 | cut -d= -f2)"
ORDER2="$(grep '^ORDER2=' "$SEED_LOG" | head -1 | cut -d= -f2)"
ORDER3="$(grep '^ORDER3=' "$SEED_LOG" | head -1 | cut -d= -f2)"
MOCK_TXN="$(grep '^MOCK_TXN=' "$SEED_LOG" | head -1 | cut -d= -f2)"
MOCK_SIG="$(grep '^MOCK_SIG=' "$SEED_LOG" | head -1 | cut -d= -f2)"
MOCK_STATUS="$(grep '^MOCK_STATUS=' "$SEED_LOG" | head -1 | cut -d= -f2)"
test -n "$ORDER1" && test -n "$ORDER2" && test -n "$ORDER3"

# --- WeChat: build signed notify via admin, POST real notify path ---
curl -sf "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"provider\":\"wechat\",\"orderId\":\"${ORDER1}\",\"providerTxnId\":\"wx-${ORDER1}\",\"amountCents\":100}" \
  "http://127.0.0.1:${ADMIN}/admin/payment/sandbox-sim/build-notify" | tee /tmp/e-wx-build.json
python3 - << PY
import json, urllib.request
d=json.load(open("/tmp/e-wx-build.json"))
assert d.get("ok") and d.get("mode")=="sandbox_sim"
body=d["body"].encode()
req=urllib.request.Request(f"http://127.0.0.1:${ADMIN}/admin/payment/wechat/notify", data=body, method="POST")
for k,v in d["headers"].items():
    if k.lower()=="content-type":
        req.add_header("Content-Type", v)
    else:
        req.add_header(k, v)
with urllib.request.urlopen(req) as resp:
    text=resp.read().decode()
    print("wechat notify resp", resp.status, text)
    assert "SUCCESS" in text
# bad signature
bad=urllib.request.Request(f"http://127.0.0.1:${ADMIN}/admin/payment/wechat/notify", data=b"{}", method="POST")
bad.add_header("Content-Type","application/json")
try:
    urllib.request.urlopen(bad)
    raise SystemExit("expected 401")
except urllib.error.HTTPError as e:
    assert e.code==401, e
    print("wechat bad sig -> 401 ok")
# idempotent replay
req2=urllib.request.Request(f"http://127.0.0.1:${ADMIN}/admin/payment/wechat/notify", data=body, method="POST")
for k,v in d["headers"].items():
    if k.lower()=="content-type":
        req2.add_header("Content-Type", v)
    else:
        req2.add_header(k, v)
with urllib.request.urlopen(req2) as resp:
    assert "SUCCESS" in resp.read().decode()
    print("wechat replay ok")
PY

# --- Alipay ---
curl -sf "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"provider\":\"alipay\",\"orderId\":\"${ORDER2}\",\"providerTxnId\":\"ali-${ORDER2}\",\"amountCents\":100}" \
  "http://127.0.0.1:${ADMIN}/admin/payment/sandbox-sim/build-notify" | tee /tmp/e-ali-build.json
python3 - << PY
import json, urllib.request
d=json.load(open("/tmp/e-ali-build.json"))
assert d.get("ok") and d.get("mode")=="sandbox_sim"
body=d["body"].encode()
req=urllib.request.Request(f"http://127.0.0.1:${ADMIN}/admin/payment/alipay/notify", data=body, method="POST")
req.add_header("Content-Type", d["headers"].get("Content-Type","application/x-www-form-urlencoded"))
with urllib.request.urlopen(req) as resp:
    text=resp.read().decode().strip()
    print("alipay notify resp", resp.status, text)
    assert text=="success"
# bad sig
bad=urllib.request.Request(f"http://127.0.0.1:${ADMIN}/admin/payment/alipay/notify",
    data=b"out_trade_no=x&trade_status=TRADE_SUCCESS&sign=nope", method="POST")
bad.add_header("Content-Type","application/x-www-form-urlencoded")
try:
    urllib.request.urlopen(bad)
    raise SystemExit("expected 401")
except urllib.error.HTTPError as e:
    assert e.code==401
    print("alipay bad sig -> 401 ok")
# replay
req2=urllib.request.Request(f"http://127.0.0.1:${ADMIN}/admin/payment/alipay/notify", data=body, method="POST")
req2.add_header("Content-Type", d["headers"].get("Content-Type","application/x-www-form-urlencoded"))
with urllib.request.urlopen(req2) as resp:
    assert resp.read().decode().strip()=="success"
    print("alipay replay ok")
PY

# --- Mock webhook regression (ORDER3 = MOCK_ORDER which is ORDER1 — use ORDER3) ---
# Rebuild mock sig for ORDER3
python3 - << PY
import hmac, hashlib, json, urllib.request, os
secret="""$PAY_SECRET"""
order="""$ORDER3"""
txn=f"mock-e-{order[:8]}"
status="paid"
canonical=f"{order}|{txn}|{status}"
sig=hmac.new(secret.encode(), canonical.encode(), hashlib.sha256).hexdigest()
body=json.dumps({"orderId":order,"providerTxnId":txn,"status":status}).encode()
req=urllib.request.Request(f"http://127.0.0.1:${ADMIN}/admin/payment/webhook", data=body, method="POST")
req.add_header("Content-Type","application/json")
req.add_header("X-Payment-Signature", sig)
with urllib.request.urlopen(req) as resp:
    d=json.load(resp)
    print("mock webhook", d)
    assert d.get("ok") is True
    assert d.get("status")=="Fulfilled"
PY

echo "=== [6/6] Docs & artifacts sanity ==="
test -f docs/PAYMENT_SANDBOX.md
test -f certs/payment/README.md
test -f tools/payment-sandbox-sim/Program.cs

# wait seed status lines
sleep 1
kill "$SEED_PID" 2>/dev/null || true
wait "$SEED_PID" 2>/dev/null || true
SEED_PID=""

echo "=== PHASE E PAYMENT VERIFICATION PASSED ==="
