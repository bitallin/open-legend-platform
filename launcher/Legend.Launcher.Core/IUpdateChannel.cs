namespace Legend.Launcher.Core;

/// <summary>Extensible update channel: fetch manifest + download package.</summary>
public interface IUpdateChannel
{
    string Name { get; }

    Task<UpdateManifest> FetchManifestAsync(CancellationToken ct = default);

    /// <summary>
    /// Download package to cacheDir. Implementations may report progress via <paramref name="onProgress"/>.
    /// Fake/skeleton channels may simulate progress without a real network.
    /// </summary>
    Task<DownloadProgress> DownloadAsync(
        UpdateManifest manifest,
        string cacheDir,
        IProgress<DownloadProgress>? onProgress = null,
        CancellationToken ct = default);
}

public sealed class FileUpdateChannel : IUpdateChannel
{
    private readonly string _manifestPath;
    private readonly bool _simulateDownload;

    public FileUpdateChannel(string manifestPath, bool simulateDownload = true)
    {
        _manifestPath = manifestPath;
        _simulateDownload = simulateDownload;
    }

    public string Name => "file";

    public async Task<UpdateManifest> FetchManifestAsync(CancellationToken ct = default)
    {
        string json = await File.ReadAllTextAsync(_manifestPath, ct);
        return System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(json, JsonDefaults.Options)
               ?? throw new InvalidOperationException($"Invalid update manifest: {_manifestPath}");
    }

    public async Task<DownloadProgress> DownloadAsync(
        UpdateManifest manifest,
        string cacheDir,
        IProgress<DownloadProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        string dest = Path.Combine(cacheDir, $"update-{manifest.LatestVersion}.bin");

        if (!_simulateDownload && !string.IsNullOrWhiteSpace(manifest.PackageUrl)
            && File.Exists(manifest.PackageUrl))
        {
            await using var src = File.OpenRead(manifest.PackageUrl);
            await using var dst = File.Create(dest);
            long total = src.Length;
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                onProgress?.Report(new DownloadProgress
                {
                    BytesReceived = got,
                    TotalBytes = total,
                    Completed = false,
                    LocalPath = dest
                });
            }
            var done = new DownloadProgress
            {
                BytesReceived = got,
                TotalBytes = total,
                Completed = true,
                LocalPath = dest
            };
            onProgress?.Report(done);
            return done;
        }

        // Skeleton: fake progress then write a tiny marker file
        long fakeTotal = manifest.PackageSizeBytes > 0 ? manifest.PackageSizeBytes : 1_048_576;
        for (int i = 1; i <= 10; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(30, ct);
            onProgress?.Report(new DownloadProgress
            {
                BytesReceived = fakeTotal * i / 10,
                TotalBytes = fakeTotal,
                Completed = false
            });
        }

        await File.WriteAllTextAsync(dest,
            $"legend-update-stub version={manifest.LatestVersion} sha256={manifest.Sha256}\n", ct);

        var final = new DownloadProgress
        {
            BytesReceived = fakeTotal,
            TotalBytes = fakeTotal,
            Completed = true,
            LocalPath = dest
        };
        onProgress?.Report(final);
        return final;
    }
}

public sealed class HttpUpdateChannel : IUpdateChannel
{
    private readonly HttpClient _http;
    private readonly string _manifestUrl;

    public HttpUpdateChannel(string manifestUrl, HttpClient? http = null)
    {
        _manifestUrl = manifestUrl;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string Name => "http";

    public async Task<UpdateManifest> FetchManifestAsync(CancellationToken ct = default)
    {
        string json;
        if (_manifestUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            json = await File.ReadAllTextAsync(new Uri(_manifestUrl).LocalPath, ct);
        }
        else if (_manifestUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || _manifestUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var resp = await _http.GetAsync(_manifestUrl, ct);
            resp.EnsureSuccessStatusCode();
            json = await resp.Content.ReadAsStringAsync(ct);
        }
        else
        {
            json = await File.ReadAllTextAsync(_manifestUrl, ct);
        }

        return System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(json, JsonDefaults.Options)
               ?? throw new InvalidOperationException("Invalid remote update manifest.");
    }

    public async Task<DownloadProgress> DownloadAsync(
        UpdateManifest manifest,
        string cacheDir,
        IProgress<DownloadProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        string dest = Path.Combine(cacheDir, $"update-{manifest.LatestVersion}.bin");

        if (string.IsNullOrWhiteSpace(manifest.PackageUrl))
        {
            // Fall back to simulated download when URL missing (skeleton)
            var file = new FileUpdateChannel("unused", simulateDownload: true);
            return await file.DownloadAsync(manifest, cacheDir, onProgress, ct);
        }

        if (manifest.PackageUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            || (!manifest.PackageUrl.Contains("://", StringComparison.Ordinal)
                && File.Exists(manifest.PackageUrl)))
        {
            string path = manifest.PackageUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(manifest.PackageUrl).LocalPath
                : manifest.PackageUrl;
            var file = new FileUpdateChannel(path, simulateDownload: false);
            // Re-use file channel's real copy path via a temp manifest pointing at path
            var m = new UpdateManifest
            {
                LatestVersion = manifest.LatestVersion,
                PackageUrl = path,
                PackageSizeBytes = manifest.PackageSizeBytes,
                Sha256 = manifest.Sha256
            };
            return await new FileUpdateChannel(path, simulateDownload: false)
                .DownloadAsync(m, cacheDir, onProgress, ct);
        }

        using var resp = await _http.GetAsync(manifest.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength ?? (manifest.PackageSizeBytes > 0 ? manifest.PackageSizeBytes : null);
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long got = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            got += n;
            onProgress?.Report(new DownloadProgress
            {
                BytesReceived = got,
                TotalBytes = total,
                Completed = false,
                LocalPath = dest
            });
        }

        var done = new DownloadProgress
        {
            BytesReceived = got,
            TotalBytes = total ?? got,
            Completed = true,
            LocalPath = dest
        };
        onProgress?.Report(done);
        return done;
    }
}
