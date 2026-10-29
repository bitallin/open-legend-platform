#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${LEGEND_PORT_G:-17600}"
ADMIN="${LEGEND_ADMIN_PORT_G:-17601}"
DB="/tmp/legend-verify-g-$$.db"
LOG="/tmp/legend-host-g-$$.log"
CFG="/tmp/legend-g-cfg-$$.json"
CLI_LOG="/tmp/legend-cli-g-$$.log"
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

echo "=== [1/8] Required Phase G files ==="
REQUIRED=(
  client/Legend.ClientCore/ClientUiMode.cs
  client/Legend.ClientCore/LegendSessionFsm.cs
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendUiBuilder.cs
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendVirtualJoystickUi.cs
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendToastOverlay.cs
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
  client/unity-mvp/Assets/LegendClient/Scripts/Bootstrap/LegendClientBootstrap.cs
  client/unity-mvp/Assets/LegendClient/Scripts/World/LegendEntityView.cs
  client/unity-mvp/Assets/LegendClient/Scripts/Input/LegendMvpInput.cs
  client/unity-mvp/Packages/manifest.json
  docs/UNITY_CLIENT_UGUI.md
  server/Legend.Tests/PhaseGClientUiTests.cs
)
for f in "${REQUIRED[@]}"; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done
echo "OK (${#REQUIRED[@]} files)"

echo "=== [2/8] uGUI / playability markers ==="
rg -q 'UnityEngine.UI' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
rg -q 'CreateOverlayCanvas|EnsureEventSystem' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendUiBuilder.cs
rg -q 'LegendVirtualJoystickUi' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
rg -q '攻击|Attack' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
rg -q '拾取|Pickup' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
rg -q 'SelectRing|_selectRing' client/unity-mvp/Assets/LegendClient/Scripts/World/LegendEntityView.cs
rg -q 'InSafeZone|安全区' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
rg -q '你已死亡|ShowToast' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
rg -q 'SetExternalStick' client/unity-mvp/Assets/LegendClient/Scripts/Input/LegendMvpInput.cs
rg -q 'IsPointerOverGameObject' client/unity-mvp/Assets/LegendClient/Scripts/Input/LegendMvpInput.cs
rg -q 'FindNearestLoot' client/unity-mvp/Assets/LegendClient/Scripts/World/LegendWorldPresenter.cs
rg -q 'com.unity.ugui' client/unity-mvp/Packages/manifest.json
# IMGUI OnGUI should no longer drive the HUD
if rg -q 'private void OnGUI\(' client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs; then
  echo "ERROR: LegendHudController still has OnGUI (expected uGUI only)"
  exit 1
fi
echo "OK"

echo "=== [3/8] Protocol still 1.21.0 ==="
rg -q 'CurrentVersion = "1.21.0"' client/unity-sdk/Runtime/ProtocolConstants.cs
echo "OK"

echo "=== [4/8] Sync SDK + ClientCore ==="
bash client/unity-mvp/sync-sdk.sh
diff -q client/Legend.ClientCore/ClientUiMode.cs \
  client/unity-mvp/Assets/LegendClient/Scripts/ClientCore/ClientUiMode.cs
diff -q client/Legend.ClientCore/LegendSessionFsm.cs \
  client/unity-mvp/Assets/LegendClient/Scripts/ClientCore/LegendSessionFsm.cs
echo "OK"

echo "=== [5/8] Build ==="
dotnet build server/LegendPlatform.sln -c Release --nologo -v q
dotnet build client/Legend.ClientCore/Legend.ClientCore.csproj -c Release --nologo -v q

echo "=== [6/8] Unit tests (Phase G + full suite) ==="
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo --filter "FullyQualifiedName~PhaseGClientUi"
dotnet test server/LegendPlatform.sln -c Release --no-build --nologo

echo "=== [7/8] CliClient smoke ==="
fuser -k "${PORT}/tcp" 2>/dev/null || true
fuser -k "${ADMIN}/tcp" 2>/dev/null || true
sleep 0.2

cat > "$CFG" << JSON
{
  "realmId": "realm-classic-01",
  "realmName": "PhaseG验收区",
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
  --admin-token "g-verify-token" --admin-bind 127.0.0.1 \
  >"$LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 100); do
  if grep -q "Legend.Host ready" "$LOG" 2>/dev/null; then break; fi
  sleep 0.15
done
if ! grep -q "Legend.Host ready" "$LOG" 2>/dev/null; then
  echo "Host failed"; tail -60 "$LOG"; exit 1
fi

dotnet run --project server/Legend.CliClient/Legend.CliClient.csproj -c Release --no-build -- \
  --host 127.0.0.1 --port "$PORT" --no-combat \
  >"$CLI_LOG" 2>&1 || {
    echo "CliClient failed"; tail -80 "$LOG"; tail -80 "$CLI_LOG"; exit 1
  }
echo "CliClient smoke OK"

echo "=== [8/8] Docs mention uGUI acceptance ==="
rg -q 'uGUI|Phase G' docs/UNITY_CLIENT_UGUI.md
rg -q 'LegendClientBootstrap' docs/UNITY_CLIENT_UGUI.md
rg -q 'uGUI|Phase G' client/unity-mvp/README.md
echo "ALL PHASE G CHECKS PASSED"
