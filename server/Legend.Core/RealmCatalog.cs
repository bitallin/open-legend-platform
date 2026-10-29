using System.Text.Json;

namespace Legend.Core;

public sealed class RealmConfig
{
    public string RealmId { get; set; } = "";
    public string RealmName { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7100;
    public int AdminPort { get; set; }
    public string SqlitePath { get; set; } = "";
    public string ManifestPath { get; set; } = "version-packs/sample-classic/manifest.json";
    public string Status { get; set; } = "OPEN";
    public string VersionPackId { get; set; } = "";
    public string AdminToken { get; set; } = "";
}

public sealed class RealmCatalogFile
{
    public List<RealmConfig> Realms { get; set; } = new();
}

/// <summary>Load multi-realm catalog from a JSON file or a directory of realm JSON files.</summary>
public static class RealmCatalogLoader
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static RealmConfig LoadRealmFile(string path)
    {
        string json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<RealmConfig>(json, Opts)
                  ?? throw new InvalidOperationException($"Invalid realm config: {path}");
        if (string.IsNullOrWhiteSpace(cfg.RealmId))
            throw new InvalidOperationException($"realmId required in {path}");
        if (string.IsNullOrWhiteSpace(cfg.RealmName))
            cfg.RealmName = cfg.RealmId;
        return cfg;
    }

    public static IReadOnlyList<RealmConfig> Load(string pathOrDir)
    {
        if (string.IsNullOrWhiteSpace(pathOrDir))
            return Array.Empty<RealmConfig>();

        string full = Path.GetFullPath(pathOrDir);
        if (Directory.Exists(full))
        {
            var list = new List<RealmConfig>();
            // Prefer catalog.json if present
            string catalog = Path.Combine(full, "catalog.json");
            if (File.Exists(catalog))
                return LoadCatalogJson(catalog, full);

            foreach (var file in Directory.EnumerateFiles(full, "*.json")
                         .Where(f => !string.Equals(Path.GetFileName(f), "catalog.json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(LoadRealmFile(file));
            }
            return list;
        }

        if (File.Exists(full))
        {
            // Single file: either catalog shape or one realm
            string json = File.ReadAllText(full);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("realms", out _))
                return LoadCatalogJson(full, Path.GetDirectoryName(full) ?? ".");
            return new[] { LoadRealmFile(full) };
        }

        throw new FileNotFoundException("Realm catalog/path not found", pathOrDir);
    }

    private static IReadOnlyList<RealmConfig> LoadCatalogJson(string catalogPath, string baseDir)
    {
        string json = File.ReadAllText(catalogPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = new List<RealmConfig>();

        if (!root.TryGetProperty("realms", out var realmsEl))
            return list;

        if (realmsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in realmsEl.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    string rel = el.GetString() ?? "";
                    string p = Path.IsPathRooted(rel) ? rel : Path.Combine(baseDir, rel);
                    list.Add(LoadRealmFile(p));
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    var cfg = JsonSerializer.Deserialize<RealmConfig>(el.GetRawText(), Opts)!;
                    if (string.IsNullOrWhiteSpace(cfg.RealmName))
                        cfg.RealmName = cfg.RealmId;
                    list.Add(cfg);
                }
            }
        }
        return list;
    }

    public static void SaveRealmFile(string path, RealmConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Opts) + "\n");
    }

    public static void SaveCatalogIndex(string catalogPath, IEnumerable<string> relativeRealmFiles)
    {
        var payload = new { realms = relativeRealmFiles.ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!);
        File.WriteAllText(catalogPath, JsonSerializer.Serialize(payload, Opts) + "\n");
    }
}
