namespace Legend.Playable.Audio;

/// <summary>Original sine-wave WAVs. No third-party game audio.</summary>
public static class ProceduralWav
{
    public static byte[] UiClick() => Tone(new[] { 880f }, 0.06f, 0.28f);
    public static byte[] Attack() => Tone(new[] { 220f, 330f }, 0.12f, 0.40f);
    public static byte[] Hit() => Tone(new[] { 160f, 90f }, 0.14f, 0.45f);
    public static byte[] Pickup() => Tone(new[] { 660f, 990f }, 0.16f, 0.32f);
    public static byte[] LoginBgm() => Melody(new[] { 262, 330, 392, 523, 392, 330 }, 0.38, 0.18);
    public static byte[] WorldBgm() => Melody(new[] { 196, 247, 294, 220, 247, 196 }, 0.42, 0.16);

    public static byte[] Tone(float[] freqs, float duration, float volume, int sampleRate = 22050)
    {
        if (freqs.Length == 0)
            throw new ArgumentException("freqs required", nameof(freqs));
        int n = Math.Max(1, (int)Math.Round(sampleRate * duration));
        var pcm = new short[n];
        float fade = Math.Min(0.02f, duration * 0.25f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sampleRate;
            float env = 1f;
            if (fade > 0f)
            {
                if (t < fade) env = t / fade;
                else if (t > duration - fade) env = Math.Max(0f, (duration - t) / fade);
            }
            float s = 0f;
            for (int f = 0; f < freqs.Length; f++)
                s += MathF.Sin(2f * MathF.PI * freqs[f] * t);
            float sample = (s / freqs.Length) * volume * env;
            pcm[i] = ToPcm(sample);
        }
        return WrapWav(pcm, sampleRate);
    }

    public static byte[] Melody(int[] noteHz, double noteSeconds, double volume, int sampleRate = 22050)
    {
        if (noteHz.Length == 0)
            throw new ArgumentException("notes required", nameof(noteHz));
        int noteSamples = Math.Max(1, (int)Math.Round(sampleRate * noteSeconds));
        var pcm = new short[noteSamples * noteHz.Length];
        double fade = Math.Min(0.03, noteSeconds * 0.2);
        for (int n = 0; n < noteHz.Length; n++)
        {
            for (int i = 0; i < noteSamples; i++)
            {
                double t = i / (double)sampleRate;
                double env = 1;
                if (fade > 0)
                {
                    if (t < fade) env = t / fade;
                    else if (t > noteSeconds - fade) env = Math.Max(0, (noteSeconds - t) / fade);
                }
                double s = Math.Sin(2 * Math.PI * noteHz[n] * t) * volume * env;
                pcm[n * noteSamples + i] = ToPcm(s);
            }
        }
        return WrapWav(pcm, sampleRate);
    }

    public static bool LooksLikeWav(ReadOnlySpan<byte> wav)
        => wav.Length > 44
           && wav[0] == (byte)'R' && wav[1] == (byte)'I' && wav[2] == (byte)'F' && wav[3] == (byte)'F'
           && wav[8] == (byte)'W' && wav[9] == (byte)'A' && wav[10] == (byte)'V' && wav[11] == (byte)'E';

    private static short ToPcm(double sample)
    {
        double clamped = Math.Clamp(sample, -1d, 1d);
        return (short)Math.Round(clamped * short.MaxValue);
    }

    private static byte[] WrapWav(short[] pcm, int sampleRate)
    {
        int dataBytes = pcm.Length * 2;
        var buffer = new byte[44 + dataBytes];
        WriteAscii(buffer, 0, "RIFF");
        WriteInt(buffer, 4, 36 + dataBytes);
        WriteAscii(buffer, 8, "WAVE");
        WriteAscii(buffer, 12, "fmt ");
        WriteInt(buffer, 16, 16);
        WriteShort(buffer, 20, 1);
        WriteShort(buffer, 22, 1);
        WriteInt(buffer, 24, sampleRate);
        WriteInt(buffer, 28, sampleRate * 2);
        WriteShort(buffer, 32, 2);
        WriteShort(buffer, 34, 16);
        WriteAscii(buffer, 36, "data");
        WriteInt(buffer, 40, dataBytes);
        for (int i = 0; i < pcm.Length; i++)
            WriteShort(buffer, 44 + i * 2, pcm[i]);
        return buffer;
    }

    private static void WriteAscii(byte[] buffer, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
            buffer[offset + i] = (byte)text[i];
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteShort(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }
}
