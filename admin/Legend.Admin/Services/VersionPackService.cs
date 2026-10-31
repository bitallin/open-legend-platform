using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legend.Admin.Data;
using Legend.Admin.Models;
using System.Text.Encodings.Web;

namespace Legend.Admin.Services;

public sealed class VersionPackService(RepoPaths paths, AdminDbContext db)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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


    public async Task<(bool ok, string message)> CreateConfigFileAsync(string packId, string relativePath, string initialJson, string actor, string note, CancellationToken ct = default)
    {
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');
        if (!(relativePath.StartsWith("configs/", StringComparison.Ordinal) || relativePath.StartsWith("maps/", StringComparison.Ordinal) || relativePath == "manifest.json"))
            return (false, "仅允许在 configs/ 或 maps/ 下新建");
        if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return (false, "仅支持 .json");
        try
        {
            var path = ResolveSafe(packId, relativePath);
            if (File.Exists(path)) return (false, "文件已存在");
            if (string.IsNullOrWhiteSpace(initialJson)) initialJson = "{}\n";
            var (wok, wmsg, _) = await WriteAsync(packId, relativePath, initialJson, actor, note ?? "create", ct);
            return wok ? (true, "已创建") : (false, wmsg);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool ok, string message)> DeleteConfigFileAsync(string packId, string relativePath, string actor, bool allowNonEmpty, CancellationToken ct = default)
    {
        relativePath = relativePath.Replace('\\', '/');
        try
        {
            var path = ResolveSafe(packId, relativePath);
            if (!File.Exists(path)) return (false, "不存在");
            var text = await File.ReadAllTextAsync(path, ct);
            var trimmed = text.Trim();
            if (!allowNonEmpty && trimmed is not ("{}" or "[]" or ""))
            {
                // allow delete if only whitespace / empty object/array
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    var root = doc.RootElement;
                    bool empty = root.ValueKind == JsonValueKind.Object && !root.EnumerateObject().Any()
                                 || root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0;
                    if (!empty) return (false, "文件非空，删除需勾选「允许非空」或先清空");
                }
                catch { return (false, "非空或非法 JSON，拒绝删除"); }
            }
            File.Delete(path);
            db.ConfigRevisions.Add(new ConfigRevision
            {
                PackId = packId,
                RelativePath = relativePath,
                ContentHash = "deleted",
                Note = "delete",
                Actor = actor,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
            return (true, "已删除 " + relativePath);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool ok, string message)> CopyConfigFileAsync(string packId, string fromRel, string toRel, string actor, CancellationToken ct = default)
    {
        fromRel = fromRel.Replace('\\', '/');
        toRel = toRel.Replace('\\', '/');
        var (ok, msg, content) = await ReadAsync(packId, fromRel, ct);
        if (!ok || content is null) return (false, msg);
        return await CreateConfigFileAsync(packId, toRel, content, actor, $"copy from {fromRel}", ct);
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
