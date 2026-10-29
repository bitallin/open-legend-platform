# Android build helpers (Phase J)

Templates and defaults for exporting the Unity MVP client as APK / AAB.

| File | Purpose |
|------|---------|
| `player-settings.defaults.json` | Canonical package / IL2CPP / API levels |
| `keystore.properties.example` | Signing env var cheat-sheet (no secrets) |
| `mainTemplate.gradle.example` | Optional custom Gradle placeholder |
| `.gitignore` | Blocks keystores / local.properties |

**Build entry:** `Assets/Editor/AndroidBuild.cs` (`Legend.Build.Android.BuildApk` / `BuildAab`).

**One-shot (machine with Unity Android Module):**

```bash
# from repo root
./scripts/build-android.sh          # APK → dist/android/
./scripts/build-android.sh --aab    # Play Store bundle
```

Full prerequisites and env table: [`docs/ANDROID_BUILD.md`](../../../docs/ANDROID_BUILD.md).

Skeleton verify (no Unity required):

```bash
./scripts/verify-phase-j-android.sh
```
