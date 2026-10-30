#!/usr/bin/env bash
# Phase K: build Windows Standalone player via Unity batchmode.
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  cat <<'HELP'
Usage: ./scripts/build-windows-player.sh

Requires Unity 2022.3 LTS (or compatible) with Windows Build Support.
Env:
  UNITY_EDITOR / UNITY_PATH  path to Unity binary
  LEGEND_WINDOWS_OUTPUT      output dir (default: dist/windows-player)

executeMethod: Legend.Build.Windows.BuildPlayer
Docs: docs/PLAYABLE_PC.md
HELP
  exit 0
fi

find_unity() {
  if [[ -n "${UNITY_EDITOR:-}" && -x "${UNITY_EDITOR}" ]]; then echo "$UNITY_EDITOR"; return; fi
  if [[ -n "${UNITY_PATH:-}" && -x "${UNITY_PATH}" ]]; then echo "$UNITY_PATH"; return; fi
  # Common Windows / Linux Hub paths (best-effort)
  local cands=(
    "/opt/Unity/Editor/Unity"
    "$HOME/Unity/Hub/Editor/2022.3.62f3c1/Editor/Unity"
    "$HOME/Unity/Hub/Editor/2022.3.20f1/Editor/Unity"
  )
  for c in "${cands[@]}"; do
    [[ -x "$c" ]] && { echo "$c"; return; }
  done
  return 1
}

print_install_help() {
  cat <<'E'
ERROR: Unity Editor not found.
Install Unity 2022.3 LTS + Windows Build Support, then:
  export UNITY_EDITOR="/path/to/Unity"
  ./scripts/build-windows-player.sh
See docs/PLAYABLE_PC.md
E
}

UNITY_BIN="$(find_unity)" || { print_install_help; exit 1; }
OUT="${LEGEND_WINDOWS_OUTPUT:-$ROOT_DIR/dist/windows-player}"
mkdir -p "$OUT"
PROJECT="$ROOT_DIR/client/unity-mvp"
LOG="$OUT/unity-windows-build.log"

echo "Unity: $UNITY_BIN"
echo "Project: $PROJECT"
echo "Output: $OUT"

# Ensure Boot scene exists tip (Editor script creates it if missing at menu time)
export LEGEND_WINDOWS_OUTPUT="$OUT"

"$UNITY_BIN" -batchmode -nographics -quit \
  -projectPath "$PROJECT" \
  -executeMethod Legend.Build.Windows.BuildPlayer \
  -logFile "$LOG"

echo "Build finished. See $OUT and $LOG"
ls -la "$OUT" || true
