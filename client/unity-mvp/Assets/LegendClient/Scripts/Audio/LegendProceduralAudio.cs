#if UNITY_2019_1_OR_NEWER
using UnityEngine;

namespace Legend.Client.Audio
{
    /// <summary>Runtime sine-wave clips when WAV assets are missing. Original placeholders only.</summary>
    public static class LegendProceduralAudio
    {
        public static AudioClip Tone(string name, float[] freqs, float duration, float volume = 0.35f, int sampleRate = 22050)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
            var data = new float[n];
            float fade = Mathf.Min(0.02f, duration * 0.25f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sampleRate;
                float env = 1f;
                if (fade > 0f)
                {
                    if (t < fade) env = t / fade;
                    else if (t > duration - fade) env = Mathf.Max(0f, (duration - t) / fade);
                }
                float s = 0f;
                for (int f = 0; f < freqs.Length; f++)
                    s += Mathf.Sin(2f * Mathf.PI * freqs[f] * t);
                data[i] = (s / freqs.Length) * volume * env;
            }
            var clip = AudioClip.Create(name, n, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip UiClick() => Tone("ui_click", new[] { 880f }, 0.06f, 0.28f);
        public static AudioClip Attack() => Tone("attack", new[] { 220f, 330f }, 0.12f, 0.4f);
        public static AudioClip Hit() => Tone("hit", new[] { 160f, 90f }, 0.14f, 0.45f);
        public static AudioClip Pickup() => Tone("pickup", new[] { 660f, 990f }, 0.16f, 0.32f);
        public static AudioClip LoginBgm() => Tone("login_bgm", new[] { 262f, 330f, 392f }, 1.6f, 0.16f);
        public static AudioClip WorldBgm() => Tone("world_bgm", new[] { 196f, 247f, 294f }, 2.0f, 0.12f);
    }
}
#endif
