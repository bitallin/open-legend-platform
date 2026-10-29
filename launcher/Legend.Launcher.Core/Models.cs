using System.Text.Json.Serialization;

namespace Legend.Launcher.Core;

/// <summary>Public realm entry for the launcher (no admin secrets).</summary>
public sealed class RealmEntry
{
    [JsonPropertyName("realmId")]
    public string RealmId { get; set; } = "";

    [JsonPropertyName("realmName")]
    public string RealmName { get; set; } = "";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 7100;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "OPEN";

    [JsonPropertyName("versionPackId")]
    public string VersionPackId { get; set; } = "";

    /// <summary>Filled by latency probe; not persisted.</summary>
    [JsonIgnore]
    public int? LatencyMs { get; set; }

    [JsonIgnore]
    public bool Reachable => LatencyMs is >= 0;
}

public sealed class RealmCatalogDocument
{
    [JsonPropertyName("realms")]
    public List<RealmEntry> Realms { get; set; } = new();
}

public sealed class LocalVersionInfo
{
    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; } = "0.0.0";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class UpdateManifest
{
    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = "0.0.0";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("minSupportedVersion")]
    public string MinSupportedVersion { get; set; } = "0.0.0";

    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = "";

    [JsonPropertyName("packageSizeBytes")]
    public long PackageSizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = "";

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }
}

public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    MandatoryUpdate,
    ChannelMismatch,
    Error
}

public sealed class UpdateCheckResult
{
    public UpdateStatus Status { get; init; }
    public LocalVersionInfo Local { get; init; } = new();
    public UpdateManifest? Remote { get; init; }
    public string Message { get; init; } = "";
}

public sealed class DownloadProgress
{
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }
    public double Percent => TotalBytes is > 0 ? 100.0 * BytesReceived / TotalBytes.Value : 0;
    public bool Completed { get; init; }
    public string? LocalPath { get; init; }
    public string? Error { get; init; }
}

public sealed class LauncherSettings
{
    [JsonPropertyName("realmsPath")]
    public string RealmsPath { get; set; } = "config/realms.json";

    [JsonPropertyName("realmListUrl")]
    public string? RealmListUrl { get; set; }

    [JsonPropertyName("versionPath")]
    public string VersionPath { get; set; } = "config/version.json";

    [JsonPropertyName("updateManifestPath")]
    public string? UpdateManifestPath { get; set; } = "config/update-manifest.sample.json";

    [JsonPropertyName("updateManifestUrl")]
    public string? UpdateManifestUrl { get; set; }

    [JsonPropertyName("clientExecutable")]
    public string ClientExecutable { get; set; } = "";

    [JsonPropertyName("clientArgsTemplate")]
    public string ClientArgsTemplate { get; set; } = "--host {host} --port {port} --realm-id {realmId}";

    [JsonPropertyName("downloadCacheDir")]
    public string DownloadCacheDir { get; set; } = "cache/updates";

    [JsonPropertyName("probeTimeoutMs")]
    public int ProbeTimeoutMs { get; set; } = 2000;
}

public sealed class LaunchResult
{
    public bool Ok { get; init; }
    public int ExitCode { get; init; }
    public string Executable { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string Message { get; init; } = "";
    public int? Pid { get; init; }
}
