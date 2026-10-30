#if UNITY_2019_1_OR_NEWER
using UnityEngine;

namespace Legend.Client.Config
{
    /// <summary>Runtime / inspector config for the Unity MVP client.</summary>
    [CreateAssetMenu(fileName = "LegendClientConfig", menuName = "Legend/Client Config")]
    public sealed class LegendClientConfig : ScriptableObject
    {
        public string Host = "127.0.0.1";
        public int Port = 7100;
        public string Platform = "PC";
        public string RealmId = "realm-classic-01";
        public string DefaultUsername = "unity_mvp";
        public string DefaultPassword = "pass123";
        public string DefaultCharName = "旅人";
        public string DefaultClassId = "warrior";
        public float CellSize = 1f;
        public float MoveCooldownSeconds = 0.12f;
        public float MoveLerpSeconds = 0.1f;
        public float CameraHeight = 14f;
        public float CameraDistance = 12f;
        public int GroundSize = 64;
        public string BasicAttackSkillId = "basic";
        // Visual safe-zone overlay (matches sample-classic novice_plain by default)
        public int SafeZoneX1 = 5;
        public int SafeZoneY1 = 5;
        public int SafeZoneX2 = 18;
        public int SafeZoneY2 = 18;
    }
}
#endif
