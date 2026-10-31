using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legend.Admin.Data;
using Legend.Admin.Models;

namespace Legend.Admin.Services;

public sealed class VersionPackService(RepoPaths paths, AdminDbContext db)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public IReadOnlyList<PackInfo> ListPacks()
    {
        if (!Directory.Exists(paths.VersionPacksRoot)) return Array.Empty<PackInfo>();
        return Directory.GetDirectories(paths.VersionPacksRoot)
            .Select(d =>
            {
                var id = Path.GetFileName(d)!;
                var manifest = Path.Combine(d, "manifest.json");
                string display = id;
                if (File.Exists(manifest))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                        if (doc.RootElement.TryGetProperty("displayName", out var dn))
                            display = dn.GetString() ?? id;
                    }
                    catch { /* ignore */ }
                }
                return new PackInfo(id, display, d, File.Exists(manifest));
            })
            .OrderBy(p => p.Id)
            .ToList();
    }

    public string GetPackRoot(string packId)
    {
        var root = Path.GetFullPath(Path.Combine(paths.VersionPacksRoot, packId));
        if (!root.StartsWith(Path.GetFullPath(paths.VersionPacksRoot), StringComparison.Ordinal))
            throw new InvalidOperationException("invalid pack path");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(packId);
        return root;
    }

    public IReadOnlyList<ConfigFileInfo> ListConfigFiles(string packId)
    {
        var root = GetPackRoot(packId);
        var files = new List<ConfigFileInfo>();
        void AddDir(string sub, string kind)
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                files.Add(new ConfigFileInfo(rel, kind, new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)));
            }
        }
        if (File.Exists(Path.Combine(root, "manifest.json")))
            files.Add(new ConfigFileInfo("manifest.json", "manifest", new FileInfo(Path.Combine(root, "manifest.json")).Length, File.GetLastWriteTimeUtc(Path.Combine(root, "manifest.json"))));
        AddDir("configs", "config");
        AddDir("maps", "map");
        return files.OrderBy(f => f.Kind).ThenBy(f => f.RelativePath).ToList();
    }

    public async Task<(bool ok, string message, string? content)> ReadAsync(string packId, string relativePath, CancellationToken ct = default)
    {
        try
        {
            var path = ResolveSafe(packId, relativePath);
            if (!File.Exists(path)) return (false, "not_found", null);
            var text = await File.ReadAllTextAsync(path, ct);
            return (true, "ok", text);
        }
        catch (Exception ex) { return (false, ex.Message, null); }
    }

    public async Task<(bool ok, string message, string hash)> WriteAsync(string packId, string relativePath, string content, string actor, string note, CancellationToken ct = default)
    {
        try
        {
            using var _ = JsonDocument.Parse(content); // validate JSON
        }
        catch (Exception ex)
        {
            return (false, "invalid_json: " + ex.Message, "");
        }

        var path = ResolveSafe(packId, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // pretty-print when possible
        string toWrite = content;
        try
        {
            using var doc = JsonDocument.Parse(content);
            toWrite = JsonSerializer.Serialize(doc.RootElement, Pretty) + "\n";
        }
        catch { /* keep raw */ }

        await File.WriteAllTextAsync(path, toWrite, ct);
        var hash = Sha256(toWrite);
        db.ConfigRevisions.Add(new ConfigRevision
        {
            PackId = packId,
            RelativePath = relativePath.Replace('\\', '/'),
            ContentHash = hash,
            Note = note,
            Actor = actor,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return (true, "saved", hash);
    }

    public JsonElement? TryParseArrayOrObject(string json)
    {
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch { return null; }
    }

    /// <summary>Extract array from common shapes: root array, or object with templates/items/entries key.</summary>
    public IReadOnlyList<JsonElement> ExtractEntities(string json, params string[] preferredKeys)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Select(e => e.Clone()).ToList();
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in preferredKeys.Concat(["templates", "items", "entries", "skills", "quests", "npcs", "shops", "monsters", "maps", "classes", "levels"]))
            {
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr.EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }
        return Array.Empty<JsonElement>();
    }

    private string ResolveSafe(string packId, string relativePath)
    {
        var root = GetPackRoot(packId);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!combined.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("path_escape");
        return combined;
    }

    private static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public record PackInfo(string Id, string DisplayName, string RootPath, bool HasManifest);
public record ConfigFileInfo(string RelativePath, string Kind, long SizeBytes, DateTime LastWriteUtc);
