#!/usr/bin/env bash
# Phase K: playable PC vertical slice — works WITHOUT Unity Editor.
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/8] Required Phase K files ==="
REQUIRED=(
  client/unity-mvp/Assets/Editor/WindowsBuild.cs
  client/unity-mvp/WindowsBuild/README.md
  client/unity-mvp/Assets/LegendClient/Scripts/Audio/LegendAudioService.cs
  client/unity-mvp/Assets/LegendClient/Scripts/Audio/LegendProceduralAudio.cs
  client/unity-mvp/Assets/LegendClient/Audio/README.md
  client/unity-mvp/Assets/LegendClient/Audio/ui_click.wav
  client/unity-mvp/Assets/LegendClient/Audio/attack.wav
  client/unity-mvp/Assets/LegendClient/Audio/hit.wav
  client/unity-mvp/Assets/LegendClient/Audio/pickup.wav
  client/unity-mvp/Assets/LegendClient/Audio/login_bgm.wav
  client/unity-mvp/Assets/LegendClient/Audio/world_bgm.wav
  client/unity-mvp/Assets/LegendClient/Scripts/Bootstrap/LegendClientBootstrap.cs
  client/unity-mvp/Assets/LegendClient/Scripts/Input/LegendMvpInput.cs
  client/unity-mvp/Assets/LegendClient/Scripts/World/LegendWorldPresenter.cs
  client/unity-mvp/Assets/LegendClient/Scripts/World/LegendEntityView.cs
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
  docs/PLAYABLE_PC.md
  scripts/build-windows-player.sh
  scripts/verify-phase-k-playable.sh
  launcher/config/launcher.playable.example.json
  dist/windows-player/.gitkeep
)
for f in "${REQUIRED[@]}"; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done
echo "OK (${#REQUIRED[@]} files)"

echo "=== [2/8] Scripts executable ==="
test -x scripts/build-windows-player.sh
test -x scripts/verify-phase-k-playable.sh
echo "OK"

echo "=== [3/8] Windows BuildPlayer entry ==="
WB=client/unity-mvp/Assets/Editor/WindowsBuild.cs
rg -q 'namespace Legend\.Build' "$WB"
rg -q 'class Windows' "$WB"
rg -q 'public static void BuildPlayer' "$WB"
rg -q 'StandaloneWindows64' "$WB"
rg -q 'LEGEND_WINDOWS_OUTPUT' "$WB"
rg -q 'LegendClient' "$WB"
rg -q 'MenuItem\("Legend/Build/Windows Player"\)' "$WB"
echo "OK"

echo "=== [4/8] Audio + procedural fallback markers ==="
rg -q 'class LegendAudioService' client/unity-mvp/Assets/LegendClient/Scripts/Audio/LegendAudioService.cs
rg -q 'PlayLoginBgm|PlayWorldBgm|PlayAttack|PlayHit|PlayPickup|PlayUiClick' \
  client/unity-mvp/Assets/LegendClient/Scripts/Audio/LegendAudioService.cs
rg -q 'class LegendProceduralAudio' client/unity-mvp/Assets/LegendClient/Scripts/Audio/LegendProceduralAudio.cs
rg -q 'CC0|程序生成|禁止' client/unity-mvp/Assets/LegendClient/Audio/README.md
echo "OK"

echo "=== [5/8] PC input / click-to-move / hide joystick ==="
rg -q 'WantWorldClick|TabCyclePressed|ToggleBagPressed|ChatFocusPressed|IsPcStandalone' \
  client/unity-mvp/Assets/LegendClient/Scripts/Input/LegendMvpInput.cs
rg -q 'ScreenToGrid|ShowClickMarker|CycleAttackable' \
  client/unity-mvp/Assets/LegendClient/Scripts/World/LegendWorldPresenter.cs
rg -q 'HandleWorldClick|ApplyCommandLineArgs|--host|--realm-id' \
  client/unity-mvp/Assets/LegendClient/Scripts/Bootstrap/LegendClientBootstrap.cs
rg -q 'SetVisible\(false\)|IsPcStandalone' \
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs
rg -q 'SafeZoneX1|棋盘|mainTexture|_mpFill|小地图' \
  client/unity-mvp/Assets/LegendClient/Scripts/World/LegendWorldPresenter.cs \
  client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs \
  client/unity-mvp/Assets/LegendClient/Scripts/Config/LegendClientConfig.cs
echo "OK"

echo "=== [6/8] Launcher wiring ==="
rg -q 'LegendClient\.exe' launcher/config/launcher.playable.example.json
rg -q 'realms\.public\.example\.json|150\.158\.164\.90' launcher/config/launcher.playable.example.json launcher/config/realms.public.example.json
rg -q 'isGuiExe|UseShellExecute = isGuiExe' launcher/Legend.Launcher.Core/ClientProcessLauncher.cs
rg -q 'waitForExit: false' launcher/Legend.Launcher.Desktop/ViewModels/MainViewModel.cs
echo "OK"

echo "=== [7/8] Docs ==="
rg -q 'LegendClient\.exe' docs/PLAYABLE_PC.md
rg -q 'BuildPlayer|2022\.3' docs/PLAYABLE_PC.md
rg -q 'VerifyClient|验收包' docs/PLAYABLE_PC.md
rg -q '能玩一局|P1' docs/PLAYABLE_PC.md
rg -q 'UNITY_EDITOR|docs/PLAYABLE_PC' scripts/build-windows-player.sh
set +e
OUT="$(scripts/build-windows-player.sh --help 2>&1)"
HELP_STATUS=$?
set -e
[[ $HELP_STATUS -eq 0 ]] || { echo "ERROR: build-windows-player.sh --help should exit 0"; exit 1; }
echo "$OUT" | rg -q 'UNITY_EDITOR|PLAYABLE_PC'
echo "OK"

echo "=== [8/8] Protocol 1.21.0 + optional launcher tests ==="
rg -q 'CurrentVersion = "1.21.0"' client/unity-sdk/Runtime/ProtocolConstants.cs
if command -v dotnet >/dev/null 2>&1; then
  if dotnet test launcher/Legend.Launcher.Tests/Legend.Launcher.Tests.csproj -c Release --nologo -v q; then
    echo "Launcher tests: OK"
  else
    echo "ERROR: launcher tests failed"
    exit 1
  fi
else
  echo "dotnet not found — skipped launcher tests"
fi
echo "OK"

echo
echo "=== Phase K playable PC VERIFY OK ==="
echo "Unity player remains optional. Acceptance exe (no Unity): ./scripts/publish-playable-windows.sh"
echo "See docs/PLAYABLE_PC.md — public trial 150.158.164.90:7100"
