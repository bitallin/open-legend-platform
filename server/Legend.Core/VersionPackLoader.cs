using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legend.Contracts;

namespace Legend.Core;

public sealed class VersionPackLoader : IVersionPackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public LoadedVersionPack LoadFromManifest(string manifestPath)
    {
        string fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
            throw new FileNotFoundException($"Version pack manifest not found: {fullManifestPath}");

        string baseDir = Path.GetDirectoryName(fullManifestPath)!;
        string manifestJson = File.ReadAllText(fullManifestPath, Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<VersionPackManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize VersionPackManifest.");

        if (string.IsNullOrWhiteSpace(manifest.VersionId))
            throw new InvalidDataException("VersionPackManifest.VersionId cannot be empty.");

        var mapFiles = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifest.DefaultMapFile))
            mapFiles.Add(manifest.DefaultMapFile);
        foreach (var f in manifest.MapFiles)
        {
            if (!string.IsNullOrWhiteSpace(f) && !mapFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                mapFiles.Add(f);
        }

        var maps = new Dictionary<string, MapConfig>(StringComparer.OrdinalIgnoreCase);
        var hashInput = new StringBuilder();
        hashInput.Append(manifestJson);

        foreach (string rel in mapFiles)
        {
            string mapPath = Path.Combine(baseDir, rel);
            if (!File.Exists(mapPath))
                throw new FileNotFoundException($"Map config file not found: {mapPath}");
            string mapJson = File.ReadAllText(mapPath, Encoding.UTF8);
            hashInput.Append(mapJson);
            var mapConfig = JsonSerializer.Deserialize<MapConfig>(mapJson, JsonOptions)
                ?? throw new InvalidDataException($"Failed to deserialize MapConfig: {rel}");
            mapConfig.EnsureBlockedCache();
            maps[mapConfig.MapId] = mapConfig;
        }

        if (maps.Count == 0)
            throw new InvalidDataException("Version pack has no maps.");

        string defaultMapId;
        string defaultMapRel = manifest.DefaultMapFile;
        string defaultMapFull = Path.Combine(baseDir, defaultMapRel);
        string defaultMapJson = File.ReadAllText(defaultMapFull, Encoding.UTF8);
        var defaultMapParsed = JsonSerializer.Deserialize<MapConfig>(defaultMapJson, JsonOptions)!;
        defaultMapId = defaultMapParsed.MapId;
        var defaultMap = maps[defaultMapId];

        string classesPath = Path.Combine(baseDir, manifest.ClassesConfigFile);
        if (!File.Exists(classesPath))
            throw new FileNotFoundException($"Classes config file not found: {classesPath}");

        string classesJson = File.ReadAllText(classesPath, Encoding.UTF8);
        hashInput.Append(classesJson);
        var classList = JsonSerializer.Deserialize<List<ClassConfigItem>>(classesJson, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize ClassesConfig.");
        var classDict = classList.ToDictionary(c => c.ClassId, c => c, StringComparer.OrdinalIgnoreCase);

        var luaScripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relScript in manifest.ScriptFiles)
        {
            string scriptPath = Path.Combine(baseDir, relScript);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Lua script not found in version pack: {scriptPath}");
            string scriptContent = File.ReadAllText(scriptPath, Encoding.UTF8);
            luaScripts[relScript] = scriptContent;
            hashInput.Append(scriptContent);
        }

        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput.ToString()));
        string packHash = Convert.ToHexString(hashBytes).ToLowerInvariant()[..16];

        StructuredLogger.Info("VersionPackLoader", $"Loaded version pack '{manifest.VersionId}' ({manifest.DisplayName})",
            extra: new
            {
                versionId = manifest.VersionId,
                realmId = manifest.RealmId,
                mapId = defaultMap.MapId,
                mapCount = maps.Count,
                classCount = classDict.Count,
                scriptCount = luaScripts.Count,
                packHash
            });

        return new LoadedVersionPack
        {
            Manifest = manifest,
            BaseDirectory = baseDir,
            Map = defaultMap,
            Maps = maps,
            Classes = classDict,
            LuaScripts = luaScripts,
            PackHash = packHash
        };
    }
}
