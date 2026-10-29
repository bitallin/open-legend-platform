#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/5] Required Phase I files ==="
REQUIRED=(
  launcher/Legend.Launcher.Desktop/Legend.Launcher.Desktop.csproj
  launcher/Legend.Launcher.Desktop/Program.cs
  launcher/Legend.Launcher.Desktop/App.axaml
  launcher/Legend.Launcher.Desktop/App.axaml.cs
  launcher/Legend.Launcher.Desktop/Views/MainWindow.axaml
  launcher/Legend.Launcher.Desktop/Views/MainWindow.axaml.cs
  launcher/Legend.Launcher.Desktop/ViewModels/MainViewModel.cs
  launcher/Legend.Launcher.Desktop/ViewModels/RealmItemViewModel.cs
  launcher/Legend.Launcher.Desktop/ViewModels/ViewModelBase.cs
  docs/LAUNCHER.md
  scripts/verify-phase-i-launcher-gui.sh
)
for f in "${REQUIRED[@]}"; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done
echo "OK (${#REQUIRED[@]} files)"

echo "=== [2/5] Markers (Avalonia Desktop + Core reuse) ==="
rg -q 'Legend.Launcher.Core' launcher/Legend.Launcher.Desktop/Legend.Launcher.Desktop.csproj
rg -q 'Avalonia' launcher/Legend.Launcher.Desktop/Legend.Launcher.Desktop.csproj
rg -q '刷新区服' launcher/Legend.Launcher.Desktop/Views/MainWindow.axaml
rg -q '启动游戏' launcher/Legend.Launcher.Desktop/Views/MainWindow.axaml
rg -q 'class LauncherFacade' launcher/Legend.Launcher.Core/LauncherFacade.cs
rg -q 'Legend.Launcher.Desktop' launcher/Legend.Launcher.sln
rg -q 'Phase I' docs/LAUNCHER.md
# Console fallback retained
test -f launcher/Legend.Launcher/Program.cs
rg -qF -- '--headless' launcher/Legend.Launcher/Program.cs
echo "OK"

echo "=== [3/5] Build launcher solution (incl. Desktop headless compile) ==="
dotnet build launcher/Legend.Launcher.sln -c Release --nologo -v q

echo "=== [4/5] Core unit tests still green ==="
dotnet test launcher/Legend.Launcher.sln -c Release --no-build --nologo \
  --filter "FullyQualifiedName~Legend.Launcher.Tests"

echo "=== [5/5] Desktop assembly present ==="
DESKTOP_DLL="$ROOT_DIR/launcher/Legend.Launcher.Desktop/bin/Release/net8.0/Legend.Launcher.Desktop.dll"
test -f "$DESKTOP_DLL"
echo "OK $DESKTOP_DLL"
# Do not start GUI on headless CI; Xvfb optional smoke left to docs.

echo
echo "=== Phase I launcher GUI VERIFY OK ==="
