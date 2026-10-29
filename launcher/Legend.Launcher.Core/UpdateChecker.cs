using System.Text.Json;

namespace Legend.Launcher.Core;

public sealed class UpdateChecker
{
    public LocalVersionInfo LoadLocalVersion(string path)
    {
        if (!File.Exists(path))
            return new LocalVersionInfo { ClientVersion = "0.0.0", Channel = "stable", Notes = "missing version.json" };

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LocalVersionInfo>(json, JsonDefaults.Options)
               ?? new LocalVersionInfo();
    }

    public UpdateCheckResult Check(LocalVersionInfo local, UpdateManifest remote)
    {
        if (!string.Equals(local.Channel, remote.Channel, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(remote.Channel)
            && !string.IsNullOrWhiteSpace(local.Channel))
        {
            // Still allow compare, but note mismatch
            return new UpdateCheckResult
            {
                Status = UpdateStatus.ChannelMismatch,
                Local = local,
                Remote = remote,
                Message = $"频道不一致：本地={local.Channel} 远端={remote.Channel}"
            };
        }

        int cmpLatest = CompareVersions(local.ClientVersion, remote.LatestVersion);
        if (cmpLatest >= 0)
        {
            return new UpdateCheckResult
            {
                Status = UpdateStatus.UpToDate,
                Local = local,
                Remote = remote,
                Message = $"已是最新（{local.ClientVersion}）"
            };
        }

        int cmpMin = CompareVersions(local.ClientVersion, remote.MinSupportedVersion);
        bool mandatory = remote.Mandatory || cmpMin < 0;
        return new UpdateCheckResult
        {
            Status = mandatory ? UpdateStatus.MandatoryUpdate : UpdateStatus.UpdateAvailable,
            Local = local,
            Remote = remote,
            Message = mandatory
                ? $"发现强制更新：{local.ClientVersion} → {remote.LatestVersion}"
                : $"发现更新：{local.ClientVersion} → {remote.LatestVersion}"
        };
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string localVersionPath,
        IUpdateChannel channel,
        CancellationToken ct = default)
    {
        try
        {
            var local = LoadLocalVersion(localVersionPath);
            var remote = await channel.FetchManifestAsync(ct);
            return Check(local, remote);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateStatus.Error,
                Message = $"更新检查失败：{ex.Message}"
            };
        }
    }

    /// <summary>Compare dotted numeric versions (1.2.3). Non-numeric tails ignored. Returns &lt;0 if a&lt;b.</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int xa = i < pa.Length ? pa[i] : 0;
            int xb = i < pb.Length ? pb[i] : 0;
            if (xa != xb)
                return xa.CompareTo(xb);
        }
        return 0;
    }

    private static int[] Parse(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return new[] { 0 };
        return v.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                int i = 0;
                while (i < part.Length && char.IsDigit(part[i])) i++;
                return i > 0 && int.TryParse(part.AsSpan(0, i), out int n) ? n : 0;
            })
            .ToArray();
    }
}
