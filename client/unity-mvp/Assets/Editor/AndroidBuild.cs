#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Legend.Build
{
    /// <summary>
    /// Phase J Android export entry points for menu + batchmode (-executeMethod).
    /// Env (optional): LEGEND_ANDROID_OUTPUT, LEGEND_ANDROID_PACKAGE,
    /// LEGEND_ANDROID_KEYSTORE, LEGEND_ANDROID_KEYSTORE_PASS,
    /// LEGEND_ANDROID_KEYALIAS, LEGEND_ANDROID_KEYALIAS_PASS,
    /// LEGEND_ANDROID_MIN_SDK, LEGEND_ANDROID_TARGET_SDK,
    /// LEGEND_ANDROID_PRODUCT_NAME.
    /// </summary>
    public static class Android
    {
        public const string DefaultPackage = "com.legendplatform.mvp";
        public const string DefaultProductName = "LegendUnityMvp";
        public const int DefaultMinSdk = 24;
        public const int DefaultTargetSdk = 34;

        [MenuItem("Legend/Build/Android APK")]
        public static void BuildApkMenu() => BuildApk();

        [MenuItem("Legend/Build/Android AAB (Play Store)")]
        public static void BuildAabMenu() => BuildAab();

        /// <summary>Batchmode: -executeMethod Legend.Build.Android.BuildApk</summary>
        public static void BuildApk() => RunBuild(androidAppBundle: false);

        /// <summary>Batchmode: -executeMethod Legend.Build.Android.BuildAab</summary>
        public static void BuildAab() => RunBuild(androidAppBundle: true);

        static void RunBuild(bool androidAppBundle)
        {
            try
            {
                ApplyPlayerSettings();
                ConfigureKeystoreFromEnv();

                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android);
                EditorUserBuildSettings.buildAppBundle = androidAppBundle;

                string[] scenes = ResolveScenes();
                if (scenes.Length == 0)
                {
                    Debug.LogError(
                        "Legend Android build: no scenes in EditorBuildSettings and no Boot.unity. " +
                        "Use Legend → Create MVP Boot Scene, or add a scene to Build Settings.");
                    EditorApplication.Exit(2);
                    return;
                }

                string outDir = ResolveOutputDir();
                Directory.CreateDirectory(outDir);
                string ext = androidAppBundle ? "aab" : "apk";
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string outPath = Path.Combine(outDir, $"LegendUnityMvp-android-{stamp}.{ext}");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };

                Debug.Log($"Legend Android build → {outPath} (AAB={androidAppBundle}, package={PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)})");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"Legend Android build failed: {summary.result}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"Legend Android build OK: {outPath} size={summary.totalSize}");
                // Also write a stable latest symlink-style copy name for scripts
                string latest = Path.Combine(outDir, androidAppBundle ? "LegendUnityMvp-latest.aab" : "LegendUnityMvp-latest.apk");
                try
                {
                    if (File.Exists(latest)) File.Delete(latest);
                    File.Copy(outPath, latest, overwrite: true);
                }
                catch (Exception copyEx)
                {
                    Debug.LogWarning("Could not write latest copy: " + copyEx.Message);
                }

                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("Legend Android build exception: " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Applies package / IL2CPP / ARM64 / orientation / SDK levels.</summary>
        public static void ApplyPlayerSettings()
        {
            string package = EnvOr("LEGEND_ANDROID_PACKAGE", DefaultPackage);
            string product = EnvOr("LEGEND_ANDROID_PRODUCT_NAME", DefaultProductName);
            int minSdk = EnvInt("LEGEND_ANDROID_MIN_SDK", DefaultMinSdk);
            int targetSdk = EnvInt("LEGEND_ANDROID_TARGET_SDK", DefaultTargetSdk);

            PlayerSettings.companyName = "LegendPlatform";
            PlayerSettings.productName = product;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, package);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)minSdk;
            // 0 = automatic; otherwise cast target API
            if (targetSdk > 0)
                PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)targetSdk;

            // Landscape matches PC MVP default (1280x720)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.forceInternetPermission = true;

            Debug.Log(
                $"Legend Android PlayerSettings: package={package} minSdk={minSdk} targetSdk={targetSdk} " +
                "IL2CPP ARM64 Landscape");
        }

        [MenuItem("Legend/Build/Apply Android Player Settings")]
        public static void ApplyPlayerSettingsMenu() => ApplyPlayerSettings();

        static void ConfigureKeystoreFromEnv()
        {
            string keystore = Environment.GetEnvironmentVariable("LEGEND_ANDROID_KEYSTORE");
            if (string.IsNullOrWhiteSpace(keystore))
            {
                // Debug/dev builds use Unity debug keystore
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log("Legend Android: using Unity debug keystore (set LEGEND_ANDROID_KEYSTORE for release).");
                return;
            }

            if (!File.Exists(keystore))
                throw new FileNotFoundException("LEGEND_ANDROID_KEYSTORE not found: " + keystore);

            string storePass = Environment.GetEnvironmentVariable("LEGEND_ANDROID_KEYSTORE_PASS") ?? "";
            string alias = Environment.GetEnvironmentVariable("LEGEND_ANDROID_KEYALIAS") ?? "";
            string aliasPass = Environment.GetEnvironmentVariable("LEGEND_ANDROID_KEYALIAS_PASS") ?? storePass;
            if (string.IsNullOrEmpty(alias))
                throw new InvalidOperationException("LEGEND_ANDROID_KEYALIAS is required when using a custom keystore.");

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = Path.GetFullPath(keystore);
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = aliasPass;
            Debug.Log("Legend Android: custom keystore " + PlayerSettings.Android.keystoreName);
        }

        static string[] ResolveScenes()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path))
                .Select(s => s.path)
                .ToArray();
            if (enabled.Length > 0) return enabled;

            const string boot = "Assets/LegendClient/Scenes/Boot.unity";
            if (File.Exists(boot)) return new[] { boot };

            // Last resort: any .unity under Assets
            string[] found = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories);
            return found;
        }

        static string ResolveOutputDir()
        {
            string env = Environment.GetEnvironmentVariable("LEGEND_ANDROID_OUTPUT");
            if (!string.IsNullOrWhiteSpace(env))
                return Path.GetFullPath(env);

            // Repo root = three levels up from Assets (Assets -> unity-mvp -> client -> repo)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
            return Path.Combine(repoRoot, "dist", "android");
        }

        static string EnvOr(string key, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }

        static int EnvInt(string key, int fallback)
        {
            string v = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(v)) return fallback;
            return int.TryParse(v.Trim(), out int n) ? n : fallback;
        }
    }
}
#endif
