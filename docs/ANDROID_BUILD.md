# Android Build Pipeline (Phase J)

Protocol **1.21.0** (unchanged). Skeleton for exporting the Unity MVP client
(`client/unity-mvp/`) as **APK** or **AAB**.

> This agent / CI box has **no Unity Editor and no Android SDK**. Phase J
> delivers a pipeline you can run on a properly provisioned machine, plus
> `./scripts/verify-phase-j-android.sh` that stays green without producing a
> real APK.

## Prerequisites (machine that actually builds)

| Component | Notes |
|-----------|--------|
| Unity Hub + **2022.3.x LTS** | Matches `ProjectSettings/ProjectVersion.txt` (2022.3.20f1) |
| Android Build Support | Hub → Add Modules |
| Android SDK & NDK Tools | Bundled module or point `ANDROID_HOME` |
| OpenJDK (Unity module) | Unity’s JDK is fine for IL2CPP |
| Disk | IL2CPP first build needs several GB free |

```bash
export UNITY_EDITOR="$HOME/Unity/Hub/Editor/2022.3.20f1/Editor/Unity"
# optional aliases also accepted:
# export UNITY_PATH="$HOME/Unity/Hub/Editor/2022.3.20f1"
```

## One-shot commands

```bash
# from repo root
cd client/unity-mvp && ./sync-sdk.sh && cd ../..

./scripts/build-android.sh          # APK → dist/android/
./scripts/build-android.sh --aab    # or: ./scripts/build-android-aab.sh

# skeleton acceptance (no Unity):
./scripts/verify-phase-j-android.sh
```

Unity menu (Editor GUI): **Legend → Build → Android APK / Android AAB**.

Batchmode methods:

- `Legend.Build.Android.BuildApk`
- `Legend.Build.Android.BuildAab`
- `Legend.Build.Android.ApplyPlayerSettings`

## Environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `UNITY_EDITOR` / `UNITY_PATH` | (auto-detect) | Editor binary or install root |
| `LEGEND_ANDROID_OUTPUT` | `dist/android/` | Artifact directory |
| `LEGEND_ANDROID_PACKAGE` | `com.legendplatform.mvp` | applicationId |
| `LEGEND_ANDROID_PRODUCT_NAME` | `LegendUnityMvp` | Player productName |
| `LEGEND_ANDROID_MIN_SDK` | `24` | minSdkVersion |
| `LEGEND_ANDROID_TARGET_SDK` | `34` | targetSdkVersion |
| `LEGEND_ANDROID_KEYSTORE` | _(empty = Unity debug)_ | Path to `.keystore` / `.jks` |
| `LEGEND_ANDROID_KEYSTORE_PASS` | | Store password |
| `LEGEND_ANDROID_KEYALIAS` | | Key alias |
| `LEGEND_ANDROID_KEYALIAS_PASS` | _(falls back to store pass)_ | Key password |
| `LEGEND_ANDROID_LOG` | `dist/android/unity-android-build.log` | Batchmode log |

Signing cheat-sheet (no secrets in git):
`client/unity-mvp/AndroidBuild/keystore.properties.example`.

## Player / engine defaults

Canonical JSON: `client/unity-mvp/AndroidBuild/player-settings.defaults.json`.

| Setting | Value |
|---------|--------|
| applicationIdentifier | `com.legendplatform.mvp` |
| Scripting backend | **IL2CPP** |
| Architectures | **ARM64** only |
| Orientation | Landscape (matches PC 1280×720 MVP) |
| min / target API | 24 / 34 |

`Assets/Editor/AndroidBuild.cs` applies these at build time. Documented also in
`ProjectSettings/AndroidPlayerSettings.md` and mirrored in
`ProjectSettings/ProjectSettings.asset`.

## Artifacts

```text
dist/android/
  LegendUnityMvp-android-<timestamp>.apk   # or .aab
  LegendUnityMvp-latest.apk                # stable copy name
  unity-android-build.log
```

`dist/android/` is gitignored for binaries; `.gitkeep` keeps the folder.

## Layout

```text
client/unity-mvp/
  Assets/Editor/AndroidBuild.cs     # menu + -executeMethod
  AndroidBuild/
    player-settings.defaults.json
    keystore.properties.example
    mainTemplate.gradle.example     # optional custom Gradle (inactive)
    README.md
  ProjectSettings/
    ProjectSettings.asset           # package / API stubs
    AndroidPlayerSettings.md
scripts/
  build-android.sh
  build-android-aab.sh
  verify-phase-j-android.sh
docs/ANDROID_BUILD.md               # this file
```

## Host / launcher联调

1. Start `Legend.Host` (local or tunnel) — see `docs/UNITY_CLIENT_UGUI.md`.
2. On device, install the APK; login panel Host/Port should point at a reachable
   address (LAN IP, public deploy, or tunnel), **not** `127.0.0.1` unless you
   use `adb reverse`.
3. PC launcher (`docs/LAUNCHER.md`) remains for desktop builds; Android uses
   direct install for Phase J (no store listing / OTA yet).

Example with `adb reverse` for local Host on 7100:

```bash
adb reverse tcp:7100 tcp:7100
# then in-app Host: 127.0.0.1 Port: 7100
```

## Generate a release keystore (local only)

```bash
keytool -genkeypair -v -keystore legend-release.keystore -alias legend \
  -keyalg RSA -keysize 2048 -validity 10000
# export LEGEND_ANDROID_KEYSTORE=... and passwords — never commit them
```

## Known limits

- **No real APK** from this agent environment (no Unity / Android SDK).
- First IL2CPP build is slow; requires Android NDK via Unity module.
- No Google Play Console upload automation.
- No hot-update CDN / asset bundle split yet.
- Boot scene: prefer **Legend → Create MVP Boot Scene** once in Editor so
  `EditorBuildSettings` has a scene; otherwise the builder looks for
  `Assets/LegendClient/Scenes/Boot.unity` or any `.unity` under `Assets/`.
- Protocol stays **1.21.0**; do not bump unless the whole stack advances.

## Verify

```bash
./scripts/verify-phase-j-android.sh
# Prior client phases still green:
./scripts/verify-phase-g-client.sh
./scripts/verify-phase-i-launcher-gui.sh
```
