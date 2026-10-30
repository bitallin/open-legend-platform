#if UNITY_2019_1_OR_NEWER
using System.IO;
using UnityEngine;

namespace Legend.Client.Audio
{
    /// <summary>
    /// Phase K audio: login/world BGM + attack/hit/pickup/UI SFX.
    /// Loads WAV from Assets/LegendClient/Audio when available (Editor / StreamingAssets copy),
    /// else uses procedural beeps. Replace WAV with CC0/self-made clips — never closed Mir packs.
    /// </summary>
    public sealed class LegendAudioService : MonoBehaviour
    {
        private AudioSource _bgm = null!;
        private AudioSource _sfx = null!;
        private AudioClip _loginBgm = null!;
        private AudioClip _worldBgm = null!;
        private AudioClip _uiClick = null!;
        private AudioClip _attack = null!;
        private AudioClip _hit = null!;
        private AudioClip _pickup = null!;
        private string _bgmMode = "";

        public void Ensure()
        {
            if (_bgm != null) return;
            _bgm = gameObject.AddComponent<AudioSource>();
            _bgm.loop = true;
            _bgm.playOnAwake = false;
            _bgm.volume = 0.35f;
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.loop = false;
            _sfx.playOnAwake = false;
            _sfx.volume = 0.7f;
            _loginBgm = LoadOr("login_bgm", LegendProceduralAudio.LoginBgm);
            _worldBgm = LoadOr("world_bgm", LegendProceduralAudio.WorldBgm);
            _uiClick = LoadOr("ui_click", LegendProceduralAudio.UiClick);
            _attack = LoadOr("attack", LegendProceduralAudio.Attack);
            _hit = LoadOr("hit", LegendProceduralAudio.Hit);
            _pickup = LoadOr("pickup", LegendProceduralAudio.Pickup);
        }

        public void PlayLoginBgm()
        {
            Ensure();
            if (_bgmMode == "login" && _bgm.isPlaying) return;
            _bgmMode = "login";
            _bgm.clip = _loginBgm;
            _bgm.volume = 0.32f;
            _bgm.Play();
        }

        public void PlayWorldBgm()
        {
            Ensure();
            if (_bgmMode == "world" && _bgm.isPlaying) return;
            _bgmMode = "world";
            _bgm.clip = _worldBgm;
            _bgm.volume = 0.28f;
            _bgm.Play();
        }

        public void StopBgm()
        {
            if (_bgm != null) _bgm.Stop();
            _bgmMode = "";
        }

        public void PlayUiClick() { Ensure(); _sfx.PlayOneShot(_uiClick, 0.8f); }
        public void PlayAttack() { Ensure(); _sfx.PlayOneShot(_attack, 0.9f); }
        public void PlayHit() { Ensure(); _sfx.PlayOneShot(_hit, 0.85f); }
        public void PlayPickup() { Ensure(); _sfx.PlayOneShot(_pickup, 0.9f); }

        private static AudioClip LoadOr(string stem, System.Func<AudioClip> fallback)
        {
            // Prefer Resources if user copies clips there later
            var fromRes = Resources.Load<AudioClip>("LegendAudio/" + stem);
            if (fromRes != null) return fromRes;

#if UNITY_EDITOR
            string editorPath = Path.Combine(Application.dataPath, "LegendClient", "Audio", stem + ".wav");
            if (File.Exists(editorPath))
            {
                var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/LegendClient/Audio/" + stem + ".wav");
                if (loaded != null) return loaded;
            }
#endif
            string streaming = Path.Combine(Application.streamingAssetsPath, "LegendAudio", stem + ".wav");
            // StreamingAssets file load is platform-specific; fall through to procedural.
            _ = streaming;
            return fallback();
        }
    }
}
#endif
