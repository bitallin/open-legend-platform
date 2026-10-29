namespace Legend.Launcher.Core;

/// <summary>High-level orchestration used by CLI / future GUI.</summary>
public sealed class LauncherFacade
{
    private readonly LauncherSettings _settings;
    private readonly RealmCatalogService _realms;
    private readonly LatencyProbe _probe;
    private readonly UpdateChecker _updates;
    private readonly ClientProcessLauncher _launcher;

    public LauncherFacade(LauncherSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _realms = new RealmCatalogService(http);
        _probe = new LatencyProbe();
        _updates = new UpdateChecker();
        _launcher = new ClientProcessLauncher();
    }

    public LauncherSettings Settings => _settings;

    public Task<IReadOnlyList<RealmEntry>> LoadRealmsAsync(CancellationToken ct = default)
        => _realms.LoadAsync(_settings.RealmsPath, _settings.RealmListUrl, ct);

    public Task ProbeAsync(IEnumerable<RealmEntry> realms, CancellationToken ct = default)
        => _probe.ProbeAllAsync(realms, _settings.ProbeTimeoutMs, ct);

    public IUpdateChannel CreateUpdateChannel()
    {
        if (!string.IsNullOrWhiteSpace(_settings.UpdateManifestUrl))
            return new HttpUpdateChannel(_settings.UpdateManifestUrl!);
        if (!string.IsNullOrWhiteSpace(_settings.UpdateManifestPath))
            return new FileUpdateChannel(_settings.UpdateManifestPath!);
        throw new InvalidOperationException("未配置 updateManifestPath / updateManifestUrl");
    }

    public Task<UpdateCheckResult> CheckUpdateAsync(CancellationToken ct = default)
        => _updates.CheckAsync(_settings.VersionPath, CreateUpdateChannel(), ct);

    public Task<DownloadProgress> DownloadUpdateAsync(
        UpdateManifest manifest,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
        => CreateUpdateChannel().DownloadAsync(manifest, _settings.DownloadCacheDir, progress, ct);

    public Task<LaunchResult> LaunchAsync(RealmEntry realm, bool waitForExit = true, CancellationToken ct = default)
    {
        string args = _launcher.BuildArguments(_settings.ClientArgsTemplate, realm);
        return _launcher.LaunchAsync(_settings.ClientExecutable, args, waitForExit, ct);
    }

    public string BuildArgs(RealmEntry realm)
        => _launcher.BuildArguments(_settings.ClientArgsTemplate, realm);
}
