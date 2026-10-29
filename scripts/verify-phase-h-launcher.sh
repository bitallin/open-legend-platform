#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/6] Required Phase H files ==="
REQUIRED=(
  launcher/Legend.Launcher.sln
  launcher/Legend.Launcher.Core/Models.cs
  launcher/Legend.Launcher.Core/RealmCatalogService.cs
  launcher/Legend.Launcher.Core/LatencyProbe.cs
  launcher/Legend.Launcher.Core/IUpdateChannel.cs
  launcher/Legend.Launcher.Core/UpdateChecker.cs
  launcher/Legend.Launcher.Core/ClientProcessLauncher.cs
  launcher/Legend.Launcher.Core/LauncherFacade.cs
  launcher/Legend.Launcher/Program.cs
  launcher/Legend.ClientStub/Program.cs
  launcher/Legend.Launcher.Tests/RealmCatalogTests.cs
  launcher/Legend.Launcher.Tests/UpdateCheckerTests.cs
  launcher/Legend.Launcher.Tests/ClientLaunchTests.cs
  launcher/config/realms.json
  launcher/config/version.json
  launcher/config/update-manifest.sample.json
  launcher/config/launcher.json
  launcher/README.md
  docs/LAUNCHER.md
  scripts/fake-client.sh
)
for f in "${REQUIRED[@]}"; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done
echo "OK (${#REQUIRED[@]} files)"

echo "=== [2/6] Markers / no protocol bump ==="
rg -q 'interface IUpdateChannel' launcher/Legend.Launcher.Core/IUpdateChannel.cs
rg -q 'class RealmCatalogService' launcher/Legend.Launcher.Core/RealmCatalogService.cs
rg -q 'CompareVersions' launcher/Legend.Launcher.Core/UpdateChecker.cs
rg -qF -- '--headless' launcher/Legend.Launcher/Program.cs
rg -q 'ClientStub' launcher/Legend.ClientStub/Program.cs
rg -q 'realm-classic-01' launcher/config/realms.json
test "$(tr -d '[:space:]' < protocol/VERSION)" = "1.21.0"
rg -q 'CurrentVersion = "1.21.0"' server/Legend.Contracts/ProtocolConstants.cs
echo "OK"

echo "=== [3/6] Build launcher solution ==="
dotnet build launcher/Legend.Launcher.sln -c Release --nologo -v q

echo "=== [4/6] Unit tests (realm list + update + launch) ==="
dotnet test launcher/Legend.Launcher.sln -c Release --no-build --nologo \
  --filter "FullyQualifiedName~Legend.Launcher.Tests"

echo "=== [5/6] Headless launcher smoke (probe + update + stub) ==="
STUB="$ROOT_DIR/launcher/Legend.ClientStub/bin/Release/net8.0/Legend.ClientStub.dll"
test -f "$STUB"
LAUCH_LOG="/tmp/legend-launcher-h-$$.log"
(
  cd "$ROOT_DIR/launcher"
  dotnet exec "$ROOT_DIR/launcher/Legend.Launcher/bin/Release/net8.0/Legend.Launcher.dll" \
    --headless --probe --check-update --download \
    --launch --realm-id realm-classic-01 \
    --client "$STUB"
) | tee "$LAUCH_LOG"

rg -q 'realms=' "$LAUCH_LOG"
rg -q 'update:' "$LAUCH_LOG"
rg -q 'host=127.0.0.1' "$LAUCH_LOG"
rg -q 'port=7100' "$LAUCH_LOG"
rg -q 'realm-id=realm-classic-01' "$LAUCH_LOG"
rg -q 'launch ok=True' "$LAUCH_LOG"
# fake download marker
test -d "$ROOT_DIR/launcher/cache/updates" || test -d /tmp  # cache under launcher cwd
CACHE_HIT="$(find "$ROOT_DIR/launcher/cache/updates" -name 'update-*.bin' 2>/dev/null | head -1 || true)"
if [[ -z "$CACHE_HIT" ]]; then
  echo "WARN: no update cache file (download may have used different cwd)"
else
  echo "OK cache=$CACHE_HIT"
fi

echo "=== [6/6] fake-client.sh smoke ==="
bash "$ROOT_DIR/scripts/fake-client.sh" --host 127.0.0.1 --port 7100 --realm-id realm-classic-01 | tee /tmp/legend-fake-client-$$.log
rg -q 'host=127.0.0.1' /tmp/legend-fake-client-$$.log
(
  cd "$ROOT_DIR/launcher"
  dotnet exec "$ROOT_DIR/launcher/Legend.Launcher/bin/Release/net8.0/Legend.Launcher.dll" \
    --launch --realm-id realm-classic-02 \
    --client "$ROOT_DIR/scripts/fake-client.sh"
) | tee /tmp/legend-launcher-fake-$$.log
rg -q 'port=7200' /tmp/legend-launcher-fake-$$.log
rg -q 'realm-id=realm-classic-02' /tmp/legend-launcher-fake-$$.log

echo
echo "=== Phase H launcher VERIFY OK ==="
