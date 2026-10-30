using System.Runtime.Versioning;
using NAudio.Wave;

namespace Legend.Playable.Audio;

/// <summary>
/// Plays procedural WAVs. Windows uses NAudio waveOut; other OS keeps the clips but stays silent
/// so headless smoke tests still construct the service.
/// </summary>
public sealed class PlayableAudio : IDisposable
{
    private readonly Dictionary<string, byte[]> _clips;
    private readonly object _gate = new();
    private WaveOutEvent? _bgm;
    private WaveStream? _bgmStream;
    private bool _disposed;

    public float Master { get; set; } = 1f;
    public float BgmVolume { get; set; } = 0.7f;
    public float SfxVolume { get; set; } = 1f;
    public float UiVolume { get; set; } = 1f;
    public bool Muted { get; set; }

    public PlayableAudio()
    {
        _clips = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["ui"] = ProceduralWav.UiClick(),
            ["attack"] = ProceduralWav.Attack(),
            ["hit"] = ProceduralWav.Hit(),
            ["pickup"] = ProceduralWav.Pickup(),
            ["login"] = ProceduralWav.LoginBgm(),
            ["world"] = ProceduralWav.WorldBgm(),
            ["town"] = ProceduralWav.Melody(new[] { 294, 349, 392, 349, 330, 294 }, 0.46, 0.16),
            ["dungeon"] = ProceduralWav.Melody(new[] { 146, 174, 196, 155, 146 }, 0.5, 0.14),
            ["step"] = ProceduralWav.Tone(new[] { 140f, 90f }, 0.05f, 0.22f),
            ["cast_w"] = ProceduralWav.Tone(new[] { 180f, 240f }, 0.12f, 0.35f),
            ["cast_m"] = ProceduralWav.Tone(new[] { 520f, 780f }, 0.18f, 0.28f),
            ["cast_t"] = ProceduralWav.Tone(new[] { 360f, 480f, 240f }, 0.16f, 0.26f),
            ["level"] = ProceduralWav.Tone(new[] { 523f, 659f, 784f }, 0.28f, 0.3f),
            ["death"] = ProceduralWav.Tone(new[] { 196f, 130f, 98f }, 0.4f, 0.35f),
            ["open"] = ProceduralWav.Tone(new[] { 440f, 660f }, 0.07f, 0.22f),
            ["close"] = ProceduralWav.Tone(new[] { 330f, 220f }, 0.07f, 0.2f),
            ["monster"] = ProceduralWav.Tone(new[] { 110f, 80f }, 0.2f, 0.3f),
            ["ambient"] = ProceduralWav.Tone(new[] { 70f, 90f }, 1.2f, 0.08f)
        };
    }

    public bool OutputAvailable => OperatingSystem.IsWindows();

    public void PlayLoginBgm() => Loop("login");
    public void PlayWorldBgm() => Loop("world");
    public void PlayTownBgm() => Loop("town");
    public void PlayDungeonBgm() => Loop("dungeon");
    public void PlayForMap(string? mapId, bool safe)
    {
        if (safe)
        {
            PlayTownBgm();
            return;
        }
        if (mapId != null && (mapId.Contains("dungeon", StringComparison.OrdinalIgnoreCase)
                              || mapId.Contains("bone", StringComparison.OrdinalIgnoreCase)
                              || mapId.Contains("marsh", StringComparison.OrdinalIgnoreCase)))
        {
            PlayDungeonBgm();
            return;
        }
        PlayWorldBgm();
    }
    public void PlayUiClick() => OneShot("ui", ui: true);
    public void PlayOpen() => OneShot("open", ui: true);
    public void PlayClose() => OneShot("close", ui: true);
    public void PlayAttack() => OneShot("attack");
    public void PlayHit() => OneShot("hit");
    public void PlayPickup() => OneShot("pickup");
    public void PlayStep() => OneShot("step");
    public void PlayLevel() => OneShot("level");
    public void PlayDeath() => OneShot("death");
    public void PlayMonster() => OneShot("monster");
    public void PlayAmbient() => OneShot("ambient");
    public void PlayCast(string? school) => OneShot(school switch
    {
        "mage" => "cast_m",
        "taoist" => "cast_t",
        _ => "cast_w"
    });

    public void StopBgm()
    {
        lock (_gate)
        {
            try { _bgm?.Stop(); } catch { /* device already gone */ }
            _bgm?.Dispose();
            _bgmStream?.Dispose();
            _bgm = null;
            _bgmStream = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopBgm();
    }

    private void Loop(string key)
    {
        if (!OperatingSystem.IsWindows() || _disposed)
            return;
        try
        {
            StopBgm();
            var stream = new LoopWaveStream(new WaveFileReader(new MemoryStream(_clips[key], writable: false)));
            var device = new WaveOutEvent { Volume = Gain(BgmVolume, 0.35f) };
            device.Init(stream);
            lock (_gate)
            {
                _bgmStream = stream;
                _bgm = device;
            }
            device.Play();
        }
        catch
        {
            StopBgm();
        }
    }

    private float Gain(float channel, float scale)
        => Muted ? 0f : Math.Clamp(Master * channel * scale, 0f, 1f);

    private void OneShot(string key, bool ui = false)
    {
        if (!OperatingSystem.IsWindows() || _disposed || Muted)
            return;
        if (!_clips.ContainsKey(key))
            return;
        try
        {
            var reader = new WaveFileReader(new MemoryStream(_clips[key], writable: false));
            var device = new WaveOutEvent { Volume = Gain(ui ? UiVolume : SfxVolume, 0.85f) };
            device.Init(reader);
            device.PlaybackStopped += (_, _) =>
            {
                device.Dispose();
                reader.Dispose();
            };
            device.Play();
        }
        catch
        {
            /* missing waveOut should not kill the client */
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class LoopWaveStream : WaveStream
    {
        private readonly WaveStream _source;

        public LoopWaveStream(WaveStream source) => _source = source;

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;

        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = _source.Read(buffer, offset + total, count - total);
                if (n == 0)
                {
                    if (_source.Length == 0 || _source.Position == 0)
                        break;
                    _source.Position = 0;
                    continue;
                }
                total += n;
            }
            return total;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _source.Dispose();
            base.Dispose(disposing);
        }
    }
}
