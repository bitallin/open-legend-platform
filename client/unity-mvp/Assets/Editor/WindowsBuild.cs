#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Legend.Client.EditorTools;

namespace Legend.Build
{
    /// <summary>
    /// Phase K Windows Standalone player for Avalonia launcher.
    /// Batchmode: -executeMethod Legend.Build.Windows.BuildPlayer
    /// Env: LEGEND_WINDOWS_OUTPUT (default repo dist/windows-player)
    /// </summary>
    public static class Windows
    {
        public const string DefaultProductName = "LegendClient";

        [MenuItem("Legend/Build/Windows Player")]
        public static void BuildPlayerMenu() => BuildPlayer();

        /// <summary>Batchmode entry: Legend.Build.Windows.BuildPlayer</summary>
        public static void BuildPlayer()
        {
            try
            {
                ApplyPlayerSettings();
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

                EnsureBootScene();
                string[] scenes = ResolveScenes();
                if (scenes.Length == 0)
                {
                    Debug.LogError(
                        "Legend Windows build: no scenes even after EnsureBootScene.");
                    EditorApplication.Exit(2);
                    return;
                }

                string outDir = ResolveOutputDir();
                Directory.CreateDirectory(outDir);
                string exePath = Path.Combine(outDir, DefaultProductName + ".exe");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = exePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };

                Debug.Log($"Legend Windows build → {exePath}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("Legend Windows build failed: " + report.summary.result);
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"Legend Windows build OK: {exePath} size={report.summary.totalSize}");
                File.WriteAllText(Path.Combine(outDir, "BUILD_INFO.txt"),
                    $"product={DefaultProductName}\nbuiltUtc={DateTime.UtcNow:o}\nscenes={string.Join(",", scenes)}\n");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("Legend Windows build exception: " + ex);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Legend/Build/Apply Windows Player Settings")]
        public static void ApplyPlayerSettingsMenu() => ApplyPlayerSettings();

        public static void ApplyPlayerSettings()
        {
            string product = EnvOr("LEGEND_WINDOWS_PRODUCT_NAME", DefaultProductName);
            PlayerSettings.companyName = "LegendPlatform";
            PlayerSettings.productName = product;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.legendplatform.client");
            PlayerSettings.defaultIsFullScreen = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.runInBackground = true;
            Debug.Log($"Legend Windows PlayerSettings: product={product} 1280x720 windowed");
        }


        static void EnsureBootScene()
        {
            const string boot = "Assets/LegendClient/Scenes/Boot.unity";
            if (File.Exists(boot))
            {
                var scenes = EditorBuildSettings.scenes;
                if (scenes == null || scenes.Length == 0 || scenes.All(s => s.path != boot))
                    EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(boot, true) };
                return;
            }
            Debug.Log("Legend Windows build: creating Boot.unity via LegendMvpSceneBuilder");
            LegendMvpSceneBuilder.CreateBootScene();
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

            return Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories);
        }

        static string ResolveOutputDir()
        {
            string env = Environment.GetEnvironmentVariable("LEGEND_WINDOWS_OUTPUT");
            if (!string.IsNullOrWhiteSpace(env))
                return Path.GetFullPath(env);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
            return Path.Combine(repoRoot, "dist", "windows-player");
        }

        static string EnvOr(string key, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }
    }
}
#endif
