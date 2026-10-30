#!/usr/bin/env bash
# Non-Unity playable client: compile, smoke, and (unless skipped) win-x64 publish.
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/5] Required files ==="
REQUIRED=(
  client/Legend.Playable/Legend.Playable.csproj
  client/Legend.Playable/Program.cs
  client/Legend.Playable/Net/PlayableNetClient.cs
  client/Legend.Playable/Ui/GameWindow.cs
  client/Legend.Playable/Ui/WorldCanvas.cs
  client/Legend.Playable/Audio/ProceduralWav.cs
  client/Legend.Playable/Audio/PlayableAudio.cs
  client/Legend.Playable/World/WorldState.cs
  client/Legend.Playable/SmokeCheck.cs
  client/Legend.Playable.Tests/PlayableSliceTests.cs
  docs/PLAYABLE_PC.md
  launcher/config/launcher.playable.example.json
  scripts/publish-playable-windows.sh
  scripts/publish-playable-windows.bat
)
for f in "${REQUIRED[@]}"; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done
echo "OK"

echo "=== [2/5] Docs and launcher wiring ==="
rg -q 'dotnet publish' docs/PLAYABLE_PC.md
rg -q '150\.158\.164\.90' docs/PLAYABLE_PC.md README.md
rg -q 'WASD' docs/PLAYABLE_PC.md README.md
rg -q 'LegendClient\.exe' docs/PLAYABLE_PC.md launcher/config/launcher.playable.example.json
rg -q '无 Unity|非 Unity' docs/PLAYABLE_PC.md
rg -q 'basic_slash' client/Legend.Playable/Session/PlayableSession.cs
rg -q '1\.21\.0' client/Legend.Playable/Net/PlayableNetClient.cs docs/PLAYABLE_PC.md
echo "OK"

echo "=== [3/5] dotnet test ==="
dotnet test client/Legend.Playable.Tests/Legend.Playable.Tests.csproj -c Release --nologo
echo "OK"

echo "=== [4/5] --smoke / --help ==="
dotnet run --project client/Legend.Playable/Legend.Playable.csproj -c Release --no-build -- --smoke
dotnet run --project client/Legend.Playable/Legend.Playable.csproj -c Release --no-build -- --help | rg -q '150\.158\.164\.90'
echo "OK"

if [[ "${SKIP_WIN_PUBLISH:-}" == "1" ]]; then
  echo "=== [5/5] win-x64 publish skipped (SKIP_WIN_PUBLISH=1) ==="
else
  echo "=== [5/5] self-contained win-x64 publish ==="
  ./scripts/publish-playable-windows.sh
fi

echo
echo "=== Playable client VERIFY OK ==="
echo "Windows: scripts\\publish-playable-windows.bat"
echo "Play:    dist\\windows-player\\LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01"
