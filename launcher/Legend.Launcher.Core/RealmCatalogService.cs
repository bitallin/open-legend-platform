using System.Text.Json;

namespace Legend.Launcher.Core;

/// <summary>Load realm list from local JSON and optionally merge/overwrite from HTTP/file URL.</summary>
public sealed class RealmCatalogService
{
    private readonly HttpClient _http;

    public RealmCatalogService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<IReadOnlyList<RealmEntry>> LoadAsync(
        string localPath,
        string? remoteUrl = null,
        CancellationToken ct = default)
    {
        var map = new Dictionary<string, RealmEntry>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            foreach (var r in ParseDocument(await File.ReadAllTextAsync(localPath, ct)))
                Upsert(map, r);
        }

        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            string json = await FetchTextAsync(remoteUrl, ct);
            foreach (var r in ParseDocument(json))
                Upsert(map, r);
        }

        return map.Values
            .OrderBy(r => r.RealmId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<RealmEntry> ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<RealmEntry>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Shape A: { "realms": [ {...} | "file.json" ] }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("realms", out var realmsEl)
            && realmsEl.ValueKind == JsonValueKind.Array)
        {
            var list = new List<RealmEntry>();
            foreach (var el in realmsEl.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object)
                {
                    var entry = JsonSerializer.Deserialize<RealmEntry>(el.GetRawText(), JsonDefaults.Options);
                    if (entry is not null && !string.IsNullOrWhiteSpace(entry.RealmId))
                    {
                        if (string.IsNullOrWhiteSpace(entry.RealmName))
                            entry.RealmName = entry.RealmId;
                        if (string.IsNullOrWhiteSpace(entry.Status))
                            entry.Status = "OPEN";
                        list.Add(entry);
                    }
                }
                // string entries (file refs) are ignored in launcher public catalog —
                // launcher config should embed objects, not server-side catalog indexes.
            }
            return list;
        }

        // Shape B: single realm object
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("realmId", out _))
        {
            var one = JsonSerializer.Deserialize<RealmEntry>(json, JsonDefaults.Options);
            if (one is not null && !string.IsNullOrWhiteSpace(one.RealmId))
            {
                if (string.IsNullOrWhiteSpace(one.RealmName))
                    one.RealmName = one.RealmId;
                return new[] { one };
            }
        }

        // Shape C: bare array
        if (root.ValueKind == JsonValueKind.Array)
        {
            var list = new List<RealmEntry>();
            foreach (var el in root.EnumerateArray())
            {
                var entry = JsonSerializer.Deserialize<RealmEntry>(el.GetRawText(), JsonDefaults.Options);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.RealmId))
                {
                    if (string.IsNullOrWhiteSpace(entry.RealmName))
                        entry.RealmName = entry.RealmId;
                    list.Add(entry);
                }
            }
            return list;
        }

        throw new InvalidOperationException("Unrecognized realm catalog JSON shape.");
    }

    private async Task<string> FetchTextAsync(string urlOrPath, CancellationToken ct)
    {
        if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var resp = await _http.GetAsync(urlOrPath, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        if (urlOrPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(urlOrPath);
            return await File.ReadAllTextAsync(uri.LocalPath, ct);
        }

        // Treat as local path
        return await File.ReadAllTextAsync(urlOrPath, ct);
    }

    private static void Upsert(Dictionary<string, RealmEntry> map, RealmEntry r)
    {
        if (string.IsNullOrWhiteSpace(r.RealmId))
            return;
        map[r.RealmId] = r;
    }
}
