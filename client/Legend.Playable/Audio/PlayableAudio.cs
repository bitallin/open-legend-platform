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

    public PlayableAudio()
    {
        _clips = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["ui"] = ProceduralWav.UiClick(),
            ["attack"] = ProceduralWav.Attack(),
            ["hit"] = ProceduralWav.Hit(),
            ["pickup"] = ProceduralWav.Pickup(),
            ["login"] = ProceduralWav.LoginBgm(),
            ["world"] = ProceduralWav.WorldBgm()
        };
    }

    public bool OutputAvailable => OperatingSystem.IsWindows();

    public void PlayLoginBgm() => Loop("login");
    public void PlayWorldBgm() => Loop("world");
    public void PlayUiClick() => OneShot("ui");
    public void PlayAttack() => OneShot("attack");
    public void PlayHit() => OneShot("hit");
    public void PlayPickup() => OneShot("pickup");

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
            var device = new WaveOutEvent { Volume = 0.35f };
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

    private void OneShot(string key)
    {
        if (!OperatingSystem.IsWindows() || _disposed)
            return;
        try
        {
            var reader = new WaveFileReader(new MemoryStream(_clips[key], writable: false));
            var device = new WaveOutEvent { Volume = 0.85f };
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
