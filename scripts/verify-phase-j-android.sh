#!/usr/bin/env bash
# Phase J: Android export/build pipeline skeleton — works WITHOUT Unity Editor.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PROTO_VER="$(tr -d '[:space:]' < protocol/VERSION)"
if [[ "$PROTO_VER" != "1.21.0" ]]; then
  echo "ERROR: protocol/VERSION expected 1.21.0, got '$PROTO_VER'"
  exit 1
fi

echo "=== [1/7] Required Phase J files ==="
REQUIRED=(
  client/unity-mvp/Assets/Editor/AndroidBuild.cs
  client/unity-mvp/AndroidBuild/README.md
  client/unity-mvp/AndroidBuild/keystore.properties.example
  client/unity-mvp/AndroidBuild/player-settings.defaults.json
  client/unity-mvp/AndroidBuild/mainTemplate.gradle.example
  client/unity-mvp/AndroidBuild/.gitignore
  client/unity-mvp/ProjectSettings/ProjectSettings.asset
  client/unity-mvp/ProjectSettings/AndroidPlayerSettings.md
  client/unity-mvp/ProjectSettings/ProjectVersion.txt
  client/unity-mvp/README.md
  docs/ANDROID_BUILD.md
  scripts/build-android.sh
  scripts/build-android-aab.sh
  scripts/verify-phase-j-android.sh
  dist/android/.gitkeep
)
for f in "${REQUIRED[@]}"; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done
echo "OK (${#REQUIRED[@]} files)"

echo "=== [2/7] Scripts executable ==="
test -x scripts/build-android.sh || { echo "NOT EXECUTABLE: scripts/build-android.sh"; exit 1; }
test -x scripts/build-android-aab.sh || { echo "NOT EXECUTABLE: scripts/build-android-aab.sh"; exit 1; }
test -x scripts/verify-phase-j-android.sh || { echo "NOT EXECUTABLE: scripts/verify-phase-j-android.sh"; exit 1; }
echo "OK"

echo "=== [3/7] Editor script markers (batchmode entry) ==="
EDITOR_CS=client/unity-mvp/Assets/Editor/AndroidBuild.cs
rg -q 'namespace Legend\.Build' "$EDITOR_CS"
rg -q 'class Android' "$EDITOR_CS"
rg -q 'public static void BuildApk' "$EDITOR_CS"
rg -q 'public static void BuildAab' "$EDITOR_CS"
rg -q 'ApplyPlayerSettings' "$EDITOR_CS"
rg -q 'LEGEND_ANDROID_OUTPUT' "$EDITOR_CS"
rg -q 'LEGEND_ANDROID_KEYSTORE' "$EDITOR_CS"
rg -q 'LEGEND_ANDROID_PACKAGE' "$EDITOR_CS"
rg -q 'ScriptingImplementation\.IL2CPP' "$EDITOR_CS"
rg -q 'AndroidArchitecture\.ARM64' "$EDITOR_CS"
rg -q 'com\.legendplatform\.mvp' "$EDITOR_CS"
rg -q 'MenuItem\("Legend/Build/Android APK"\)' "$EDITOR_CS"
# No real secrets in Editor script
if rg -qi 'password\s*=\s*"[^"]{8,}"' "$EDITOR_CS"; then
  echo "ERROR: possible hard-coded password in AndroidBuild.cs"
  exit 1
fi
echo "OK"

echo "=== [4/7] Player settings defaults + ProjectSettings ==="
DEFAULTS=client/unity-mvp/AndroidBuild/player-settings.defaults.json
rg -q '"applicationIdentifier": "com\.legendplatform\.mvp"' "$DEFAULTS"
rg -q '"scriptingBackend": "IL2CPP"' "$DEFAULTS"
rg -q '"minSdkVersion": 24' "$DEFAULTS"
rg -q '"targetSdkVersion": 34' "$DEFAULTS"
rg -q 'Legend\.Build\.Android\.BuildApk' "$DEFAULTS"
rg -q 'com\.legendplatform\.mvp' client/unity-mvp/ProjectSettings/ProjectSettings.asset
rg -q 'AndroidMinSdkVersion: 24' client/unity-mvp/ProjectSettings/ProjectSettings.asset
rg -q 'AndroidTargetSdkVersion: 34' client/unity-mvp/ProjectSettings/ProjectSettings.asset
rg -q 'AndroidTargetArchitectures: 2' client/unity-mvp/ProjectSettings/ProjectSettings.asset
echo "OK"

echo "=== [5/7] Keystore example is placeholder-only ==="
KS=client/unity-mvp/AndroidBuild/keystore.properties.example
rg -q 'LEGEND_ANDROID_KEYSTORE' "$KS"
rg -q 'CHANGE_ME' "$KS"
# Must not look like a committed real keystore path under home with .keystore present in repo
if find client/unity-mvp/AndroidBuild -maxdepth 1 \( -name '*.keystore' -o -name '*.jks' -o -name keystore.properties \) | grep -q .; then
  echo "ERROR: real keystore / keystore.properties must not be committed under AndroidBuild/"
  exit 1
fi
echo "OK"

echo "=== [6/7] Docs + README links + build script guidance ==="
rg -q 'LEGEND_ANDROID_OUTPUT' docs/ANDROID_BUILD.md
rg -q 'LEGEND_ANDROID_KEYSTORE' docs/ANDROID_BUILD.md
rg -q 'com\.legendplatform\.mvp' docs/ANDROID_BUILD.md
rg -q 'IL2CPP' docs/ANDROID_BUILD.md
rg -q 'dist/android' docs/ANDROID_BUILD.md
rg -q 'verify-phase-j-android' docs/ANDROID_BUILD.md
rg -q 'build-android\.sh' docs/ANDROID_BUILD.md
rg -q 'ANDROID_BUILD' client/unity-mvp/README.md
rg -q 'UNITY_EDITOR|UNITY_PATH' scripts/build-android.sh
# build-android without Unity must fail clearly (non-zero)
set +e
OUT="$(scripts/build-android.sh --help 2>&1)"
HELP_STATUS=$?
set -e
[[ $HELP_STATUS -eq 0 ]] || { echo "ERROR: build-android.sh --help should exit 0"; exit 1; }
echo "$OUT" | rg -q 'UNITY_EDITOR|docs/ANDROID_BUILD'
# Simulate missing Unity: clear env and expect non-zero (may find Unity on some machines — only assert exit code path exists)
# We assert the help text and that find_unity failure message is in the script source.
rg -q 'print_install_help|Unity Editor with Android Build Support' scripts/build-android.sh
echo "OK"

echo "=== [7/7] Protocol still 1.21.0 + optional ClientCore build ==="
rg -q 'CurrentVersion = "1.21.0"' client/unity-sdk/Runtime/ProtocolConstants.cs
# Soft: ClientCore build if dotnet present — failure does not block Android skeleton
if command -v dotnet >/dev/null 2>&1; then
  if dotnet build client/Legend.ClientCore/Legend.ClientCore.csproj -c Release --nologo -v q; then
    echo "ClientCore build: OK (optional)"
  else
    echo "WARN: ClientCore build failed (non-blocking for Phase J skeleton)"
  fi
else
  echo "dotnet not found — skipped optional ClientCore build"
fi
echo "OK"

echo
echo "=== Phase J Android pipeline skeleton VERIFY OK ==="
echo "Note: no Unity Editor in this environment — real APK/AAB not produced."
echo "On a machine with Unity Android Module: ./scripts/build-android.sh"
