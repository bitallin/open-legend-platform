#!/usr/bin/env bash
# Phase J: build Android APK/AAB via Unity batchmode.
# Requires Unity Editor with Android Build Support (SDK/NDK/JDK).
# Without Unity this script exits non-zero and prints install guidance.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PROJECT_PATH="$ROOT_DIR/client/unity-mvp"
OUTPUT_DIR="${LEGEND_ANDROID_OUTPUT:-$ROOT_DIR/dist/android}"
export LEGEND_ANDROID_OUTPUT="$OUTPUT_DIR"

MODE="apk"
UNITY_EXTRA_ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --aab|aab)
      MODE="aab"
      shift
      ;;
    --apk|apk)
      MODE="apk"
      shift
      ;;
    --help|-h)
      cat << USAGE
Usage: ./scripts/build-android.sh [--apk|--aab]

Environment:
  UNITY_PATH / UNITY_EDITOR   Path to Unity Editor binary
  LEGEND_ANDROID_OUTPUT       Output dir (default: dist/android)
  LEGEND_ANDROID_PACKAGE      applicationId (default: com.legendplatform.mvp)
  LEGEND_ANDROID_KEYSTORE*    Optional release signing (see docs/ANDROID_BUILD.md)

Docs: docs/ANDROID_BUILD.md
Skeleton check (no Unity): ./scripts/verify-phase-j-android.sh
USAGE
      exit 0
      ;;
    *)
      UNITY_EXTRA_ARGS+=("$1")
      shift
      ;;
  esac
done

if [[ "$MODE" == "aab" ]]; then
  EXECUTE_METHOD="Legend.Build.Android.BuildAab"
else
  EXECUTE_METHOD="Legend.Build.Android.BuildApk"
fi

find_unity() {
  if [[ -n "${UNITY_EDITOR:-}" && -x "${UNITY_EDITOR}" ]]; then
    echo "$UNITY_EDITOR"
    return 0
  fi
  if [[ -n "${UNITY_PATH:-}" ]]; then
    if [[ -x "${UNITY_PATH}" ]]; then
      echo "$UNITY_PATH"
      return 0
    fi
    # UNITY_PATH may be the Editor install root
    for cand in \
      "${UNITY_PATH}/Unity" \
      "${UNITY_PATH}/Editor/Unity" \
      "${UNITY_PATH}/Unity.app/Contents/MacOS/Unity"
    do
      if [[ -x "$cand" ]]; then
        echo "$cand"
        return 0
      fi
    done
  fi
  # Common Linux / macOS Hub locations (best-effort)
  local hubs=(
    "$HOME/Unity/Hub/Editor"
    "/opt/unity/Editor"
    "/Applications/Unity/Hub/Editor"
  )
  for hub in "${hubs[@]}"; do
    [[ -d "$hub" ]] || continue
    # Prefer 2022.3.*
    local found
    found="$(find "$hub" -maxdepth 3 -type f \( -name Unity -o -name Unity.exe \) 2>/dev/null | head -n 1 || true)"
    if [[ -n "$found" && -x "$found" ]]; then
      echo "$found"
      return 0
    fi
  done
  # PATH
  if command -v Unity >/dev/null 2>&1; then
    command -v Unity
    return 0
  fi
  return 1
}

print_install_help() {
  cat << HELP

ERROR: Unity Editor with Android Build Support was not found.

This environment / machine cannot produce a real APK until Unity is installed.

Install guidance:
  1. Install Unity Hub + Editor 2022.3.x LTS (matches client/unity-mvp/ProjectSettings/ProjectVersion.txt).
  2. Add modules: Android Build Support, Android SDK & NDK Tools, OpenJDK.
  3. Export UNITY_EDITOR to the Editor binary, e.g.:
       export UNITY_EDITOR="\$HOME/Unity/Hub/Editor/2022.3.20f1/Editor/Unity"
  4. Sync SDK:  (cd client/unity-mvp && ./sync-sdk.sh)
  5. Re-run:    ./scripts/build-android.sh [--aab]

Skeleton / docs (no Editor required):
  ./scripts/verify-phase-j-android.sh
  docs/ANDROID_BUILD.md

HELP
}

UNITY_BIN=""
if ! UNITY_BIN="$(find_unity)"; then
  print_install_help
  exit 2
fi

mkdir -p "$OUTPUT_DIR"
LOG_FILE="${LEGEND_ANDROID_LOG:-$OUTPUT_DIR/unity-android-build.log}"

echo "=== Legend Android build ==="
echo "Unity:    $UNITY_BIN"
echo "Project:  $PROJECT_PATH"
echo "Method:   $EXECUTE_METHOD"
echo "Output:   $OUTPUT_DIR"
echo "Log:      $LOG_FILE"
echo

# Ensure Boot scene exists when possible (menu path); batchmode cannot click menus,
# so rely on EditorBuildSettings / Boot.unity / any scene discovery inside AndroidBuild.cs.
if [[ ! -d "$PROJECT_PATH/Assets" ]]; then
  echo "ERROR: Unity project missing at $PROJECT_PATH"
  exit 1
fi

set +e
"$UNITY_BIN" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_PATH" \
  -executeMethod "$EXECUTE_METHOD" \
  -logFile "$LOG_FILE" \
  "${UNITY_EXTRA_ARGS[@]+"${UNITY_EXTRA_ARGS[@]}"}"
STATUS=$?
set -e

if [[ $STATUS -ne 0 ]]; then
  echo "ERROR: Unity batchmode exited $STATUS — see $LOG_FILE"
  tail -n 80 "$LOG_FILE" 2>/dev/null || true
  exit "$STATUS"
fi

echo "=== Android build finished (exit 0) ==="
echo "Artifacts under: $OUTPUT_DIR"
ls -la "$OUTPUT_DIR" || true
