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

        string mapPath = Path.Combine(baseDir, manifest.DefaultMapFile);
        if (!File.Exists(mapPath))
            throw new FileNotFoundException($"Map config file not found: {mapPath}");

        string mapJson = File.ReadAllText(mapPath, Encoding.UTF8);
        var mapConfig = JsonSerializer.Deserialize<MapConfig>(mapJson, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize MapConfig.");

        string classesPath = Path.Combine(baseDir, manifest.ClassesConfigFile);
        if (!File.Exists(classesPath))
            throw new FileNotFoundException($"Classes config file not found: {classesPath}");

        string classesJson = File.ReadAllText(classesPath, Encoding.UTF8);
        var classList = JsonSerializer.Deserialize<List<ClassConfigItem>>(classesJson, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize ClassesConfig.");

        var classDict = classList.ToDictionary(c => c.ClassId, c => c, StringComparer.OrdinalIgnoreCase);

        var luaScripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var sha256 = SHA256.Create();
        var hashInput = new StringBuilder();
        hashInput.Append(manifestJson).Append(mapJson).Append(classesJson);

        foreach (string relScript in manifest.ScriptFiles)
        {
            string scriptPath = Path.Combine(baseDir, relScript);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Lua script not found in version pack: {scriptPath}");

            string scriptContent = File.ReadAllText(scriptPath, Encoding.UTF8);
            luaScripts[relScript] = scriptContent;
            hashInput.Append(scriptContent);
        }

        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput.ToString()));
        string packHash = Convert.ToHexString(hashBytes).ToLowerInvariant()[..16];

        StructuredLogger.Info("VersionPackLoader", $"Loaded version pack '{manifest.VersionId}' ({manifest.DisplayName})",
            extra: new
            {
                versionId = manifest.VersionId,
                realmId = manifest.RealmId,
                mapId = mapConfig.MapId,
                classCount = classDict.Count,
                scriptCount = luaScripts.Count,
                packHash
            });

        return new LoadedVersionPack
        {
            Manifest = manifest,
            BaseDirectory = baseDir,
            Map = mapConfig,
            Classes = classDict,
            LuaScripts = luaScripts,
            PackHash = packHash
        };
    }
}
