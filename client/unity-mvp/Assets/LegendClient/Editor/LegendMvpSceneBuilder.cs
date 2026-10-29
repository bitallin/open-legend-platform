#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Legend.Client.Bootstrap;

namespace Legend.Client.EditorTools
{
    /// <summary>Menu: Legend → Create MVP Boot Scene (optional; runtime Bootstrap alone is enough).</summary>
    public static class LegendMvpSceneBuilder
    {
        [MenuItem("Legend/Create MVP Boot Scene")]
        public static void CreateBootScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var boot = new GameObject("LegendClientBootstrap");
            boot.AddComponent<LegendClientBootstrap>();
            // Soft ground marker
            var marker = GameObject.CreatePrimitive(PrimitiveType.Plane);
            marker.name = "BootGroundHint";
            marker.transform.localScale = new Vector3(6f, 1f, 6f);
            Object.DestroyImmediate(marker.GetComponent<Collider>());

            const string dir = "Assets/LegendClient/Scenes";
            if (!AssetDatabase.IsValidFolder("Assets/LegendClient"))
                AssetDatabase.CreateFolder("Assets", "LegendClient");
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/LegendClient", "Scenes");
            string path = dir + "/Boot.unity";
            EditorSceneManager.SaveScene(scene, path);
            var scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(path, true)
            };
            EditorBuildSettings.scenes = scenes;
            Debug.Log("Legend MVP Boot scene saved to " + path + ". Press Play.");
        }
    }
}
#endif
