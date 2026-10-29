# Android Player Settings (Phase J)

Runtime application of these values happens in `Assets/Editor/AndroidBuild.cs`
(`Legend.Build.Android.ApplyPlayerSettings`). Canonical JSON:
`AndroidBuild/player-settings.defaults.json`.

| Setting | Default |
|---------|---------|
| applicationIdentifier | `com.legendplatform.mvp` |
| minSdkVersion | 24 |
| targetSdkVersion | 34 |
| scriptingBackend | IL2CPP |
| architectures | ARM64 only |
| orientation | LandscapeLeft (+ LandscapeRight) |
| productName | LegendUnityMvp |
| companyName | LegendPlatform |

Override with `LEGEND_ANDROID_PACKAGE`, `LEGEND_ANDROID_MIN_SDK`,
`LEGEND_ANDROID_TARGET_SDK`, `LEGEND_ANDROID_PRODUCT_NAME`.
