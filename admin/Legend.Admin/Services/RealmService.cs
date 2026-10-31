using System.Text.Json;
using System.Text.Json.Nodes;

namespace Legend.Admin.Services;

public sealed class RealmService(RepoPaths paths)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public IReadOnlyList<RealmInfo> ListRealms()
    {
        if (!Directory.Exists(paths.RealmsRoot)) return Array.Empty<RealmInfo>();
        return Directory.EnumerateFiles(paths.RealmsRoot, "realm-*.json")
            .Select(f =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(f));
                    var r = doc.RootElement;
                    return new RealmInfo(
                        r.GetProperty("realmId").GetString() ?? Path.GetFileNameWithoutExtension(f),
                        r.TryGetProperty("realmName", out var n) ? n.GetString() ?? "" : "",
                        r.TryGetProperty("host", out var h) ? h.GetString() ?? "" : "",
                        r.TryGetProperty("port", out var p) ? p.GetInt32() : 0,
                        r.TryGetProperty("adminPort", out var ap) ? ap.GetInt32() : 0,
                        r.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                        r.TryGetProperty("manifestPath", out var mp) ? mp.GetString() ?? "" : "",
                        Path.GetFileName(f)!
                    );
                }
                catch
                {
                    return new RealmInfo(Path.GetFileNameWithoutExtension(f)!, "", "", 0, 0, "INVALID", "", Path.GetFileName(f)!);
                }
            })
            .OrderBy(x => x.RealmId)
            .ToList();
    }

    public async Task<(bool ok, string message)> UpsertAsync(string realmId, string json, CancellationToken ct = default)
    {
        try { using var _ = JsonDocument.Parse(json); }
        catch (Exception ex) { return (false, "invalid_json: " + ex.Message); }

        var safeId = realmId.Trim();
        if (string.IsNullOrWhiteSpace(safeId) || safeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return (false, "bad_realm_id");
        var path = Path.Combine(paths.RealmsRoot, $"{safeId}.json");
        if (!safeId.StartsWith("realm-", StringComparison.Ordinal))
            path = Path.Combine(paths.RealmsRoot, $"realm-{safeId}.json");
        // Prefer filename from realmId field if file already named realm-*.json
        path = Path.Combine(paths.RealmsRoot, safeId.StartsWith("realm-") ? $"{safeId}.json" : $"realm-{safeId}.json");

        string pretty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            pretty = JsonSerializer.Serialize(doc.RootElement, Pretty) + "\n";
        }
        catch { pretty = json; }

        Directory.CreateDirectory(paths.RealmsRoot);
        await File.WriteAllTextAsync(path, pretty, ct);

        // refresh catalog.json
        await RefreshCatalogAsync(ct);
        return (true, path);
    }

    public async Task<(bool ok, string message, string? content)> ReadAsync(string fileName, CancellationToken ct = default)
    {
        var path = Path.Combine(paths.RealmsRoot, fileName);
        if (!File.Exists(path)) return (false, "not_found", null);
        return (true, "ok", await File.ReadAllTextAsync(path, ct));
    }

    public async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        var realms = ListRealms();
        var catalogPath = Path.Combine(paths.RealmsRoot, "catalog.json");
        var node = new JsonObject
        {
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["realms"] = new JsonArray(realms.Select(r => JsonValue.Create(r.RealmId)).ToArray())
        };
        await File.WriteAllTextAsync(catalogPath, node.ToJsonString(Pretty) + "\n", ct);
    }

    public async Task<(bool ok, string message)> SetStatusAsync(string fileName, string status, CancellationToken ct = default)
    {
        var (ok, msg, content) = await ReadAsync(fileName, ct);
        if (!ok || content is null) return (false, msg);
        var node = JsonNode.Parse(content)!.AsObject();
        node["status"] = status;
        await File.WriteAllTextAsync(Path.Combine(paths.RealmsRoot, fileName), node.ToJsonString(Pretty) + "\n", ct);
        return (true, "ok");
    }
}

public record RealmInfo(string RealmId, string RealmName, string Host, int Port, int AdminPort, string Status, string ManifestPath, string FileName);
