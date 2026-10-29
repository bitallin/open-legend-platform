#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT_F:-17500}"
ADMIN="${LEGEND_ADMIN_PORT_F:-17501}"
DB="/tmp/legend-verify-f-$$.db"
LOG="/tmp/legend-host-f-$$.log"
CFG="/tmp/legend-f-cfg-$$.json"
CLI_LOG="/tmp/legend-cli-f-$$.log"
rm -f "$DB" "$LOG" "$CFG" "$CLI_LOG"

HOST_PID=""
cleanup() {
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

echo "=== [1/7] Required files ==="
REQUIRED=(
  client/Legend.ClientCore/LegendSessionFsm.cs
  client/Legend.ClientCore/SessionPhase.cs
  client/Legend.ClientCore/ChatLogBuffer.cs
  client/Legend.ClientCore/InventoryViewModel.cs
  client/unity-sdk/Runtime/ProtocolConstants.cs
  client/unity-sdk/Runtime/LegendUnityNetworkClient.cs
  client/unity-mvp/README.md
  client/unity-mvp/sync-sdk.sh
  client/unity-mvp/Packages/manifest.json
  client/unity-mvp/ProjectSettings/ProjectVersion.txt
  client/unity-mvp/Assets/Plugins/Google.Protobuf.dll
  client/unity-mvp/Assets/LegendClient/Scripts/Bootstrap/LegendClientBootstrap.cs
  client/unity-mvp/Assets/LegendClient/Scripts/Session/LegendSessionService.cs
  client/unity-mvp/Assets/LegendClient/Scripts/World/LegendWorldPresenter.cs
  client/unity-mvp/Assets/LegendClient/Scripts/World/LegendEntityView.cs
  client/unity-mvp/Assets/LegendClient/Scripts/Combat/LegendCombatController.cs
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
  client/unity-mvp/Assets/LegendClient/Editor/LegendMvpSceneBuilder.cs
  docs/UNITY_CLIENT_MVP.md
  server/Legend.Tests/PhaseFClientCoreTests.cs
)
for f in "${REQUIRED[@]}"; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done
echo "OK (${#REQUIRED[@]} files)"

echo "=== [2/7] Protocol version alignment ==="
rg -q 'CurrentVersion = "1.21.0"' client/unity-sdk/Runtime/ProtocolConstants.cs
rg -q 'Protocol version: \*\*1.21.0\*\*' client/unity-sdk/README.md
rg -q 'Protocol \*\*1.21.0\*\*' client/unity-mvp/README.md
# payment fields still exposed on SDK (no UI required)
rg -q 'ProviderId|CheckoutUrl|PayPayload|ProviderOrderId' client/unity-sdk/Runtime/LegendProtocol.cs
rg -q 'OnDisconnected' client/unity-sdk/Runtime/LegendUnityNetworkClient.cs
rg -q 'IReadOnlyList<InventoryItem> Inventory' client/unity-sdk/Runtime/LegendUnityNetworkClient.cs
echo "OK"

echo "=== [3/7] Sync unity-mvp SDK copies ==="
bash client/unity-mvp/sync-sdk.sh
# Ensure synced ProtocolConstants matches source
diff -q client/unity-sdk/Runtime/ProtocolConstants.cs \
  client/unity-mvp/Assets/LegendSdk/Runtime/ProtocolConstants.cs
diff -q client/Legend.ClientCore/LegendSessionFsm.cs \
  client/unity-mvp/Assets/LegendClient/Scripts/ClientCore/LegendSessionFsm.cs
echo "OK"

echo "=== [4/7] Build solution + ClientCore ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q
dotnet build client/Legend.ClientCore/Legend.ClientCore.csproj -c Release --nologo -v q

echo "=== [5/7] Unit tests (Phase F + full suite green) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo --filter "FullyQualifiedName~PhaseFClientCore"
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

echo "=== [6/7] CliClient smoke vs temp Host ==="
fuser -k "${PORT}/tcp" 2>/dev/null || true
fuser -k "${ADMIN}/tcp" 2>/dev/null || true
sleep 0.2

cat > "$CFG" << JSON
{
  "realmId": "realm-classic-01",
  "realmName": "PhaseF验收区",
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
  --admin-token "f-verify-token" --admin-bind 127.0.0.1 \
  >"$LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 100); do
  if grep -q "Legend.Host ready" "$LOG" 2>/dev/null; then break; fi
  sleep 0.15
done
if ! grep -q "Legend.Host ready" "$LOG" 2>/dev/null; then
  echo "Host failed to become ready"; tail -60 "$LOG"; exit 1
fi

dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --no-combat \
  >"$CLI_LOG" 2>&1 || {
    echo "CliClient failed; host log:"; tail -80 "$LOG" || true
    echo "cli log:"; tail -80 "$CLI_LOG" || true
    exit 1
  }
grep -qiE "enter|world|login|character|ok|success|map" "$CLI_LOG" || {
  echo "WARN: CliClient log lacked expected keywords; dumping:"
  tail -40 "$CLI_LOG"
}
echo "CliClient smoke OK"
echo "=== [7/7] README acceptance steps present ==="
rg -q 'Manual acceptance checklist' client/unity-mvp/README.md
rg -q 'Open in Unity' client/unity-mvp/README.md
rg -q 'LegendClientBootstrap' client/unity-mvp/README.md
echo "ALL PHASE F CHECKS PASSED"
