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

        // Skills
        var skillDict = new Dictionary<string, SkillConfigItem>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.SkillsConfigFile))
        {
            string skillsPath = Path.Combine(baseDir, manifest.SkillsConfigFile);
            if (!File.Exists(skillsPath))
                throw new FileNotFoundException($"Skills config not found: {skillsPath}");
            string skillsJson = File.ReadAllText(skillsPath, Encoding.UTF8);
            hashInput.Append(skillsJson);
            var skills = JsonSerializer.Deserialize<List<SkillConfigItem>>(skillsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize skills.json");
            foreach (var s in skills)
            {
                if (string.IsNullOrWhiteSpace(s.SkillId))
                    throw new InvalidDataException("SkillConfigItem.SkillId cannot be empty.");
                skillDict[s.SkillId] = s;
            }
        }

        // Monsters + spawns
        var monsterDict = new Dictionary<string, MonsterTemplate>(StringComparer.OrdinalIgnoreCase);
        var spawnList = new List<MonsterSpawnPoint>();
        if (!string.IsNullOrWhiteSpace(manifest.MonstersConfigFile))
        {
            string monstersPath = Path.Combine(baseDir, manifest.MonstersConfigFile);
            if (!File.Exists(monstersPath))
                throw new FileNotFoundException($"Monsters config not found: {monstersPath}");
            string monstersJson = File.ReadAllText(monstersPath, Encoding.UTF8);
            hashInput.Append(monstersJson);
            var monstersFile = JsonSerializer.Deserialize<MonstersConfigFile>(monstersJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize monsters.json");
            foreach (var t in monstersFile.Templates)
            {
                if (string.IsNullOrWhiteSpace(t.MonsterId))
                    throw new InvalidDataException("MonsterTemplate.MonsterId cannot be empty.");
                monsterDict[t.MonsterId] = t;
            }
            foreach (var sp in monstersFile.Spawns)
            {
                if (!monsterDict.ContainsKey(sp.MonsterId))
                    throw new InvalidDataException($"Spawn references unknown monsterId '{sp.MonsterId}'.");
                if (!maps.ContainsKey(sp.MapId))
                    throw new InvalidDataException($"Spawn map '{sp.MapId}' not in version pack.");
                spawnList.Add(sp);
            }
        }

        // Loot tables
        var lootDict = new Dictionary<string, LootTableConfig>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.LootTablesConfigFile))
        {
            string lootPath = Path.Combine(baseDir, manifest.LootTablesConfigFile);
            if (!File.Exists(lootPath))
                throw new FileNotFoundException($"Loot tables config not found: {lootPath}");
            string lootJson = File.ReadAllText(lootPath, Encoding.UTF8);
            hashInput.Append(lootJson);
            var lootTables = JsonSerializer.Deserialize<List<LootTableConfig>>(lootJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize loot_tables.json");
            foreach (var t in lootTables)
            {
                if (string.IsNullOrWhiteSpace(t.LootTableId))
                    throw new InvalidDataException("LootTableConfig.LootTableId cannot be empty.");
                lootDict[t.LootTableId] = t;
            }
        }


        // NPCs
        var npcDict = new Dictionary<string, NpcTemplate>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.NpcsConfigFile))
        {
            string npcsPath = Path.Combine(baseDir, manifest.NpcsConfigFile);
            if (!File.Exists(npcsPath))
                throw new FileNotFoundException($"NPCs config not found: {npcsPath}");
            string npcsJson = File.ReadAllText(npcsPath, Encoding.UTF8);
            hashInput.Append(npcsJson);
            var npcs = JsonSerializer.Deserialize<List<NpcTemplate>>(npcsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize npcs.json");
            foreach (var n in npcs)
            {
                if (string.IsNullOrWhiteSpace(n.NpcId))
                    throw new InvalidDataException("NpcTemplate.NpcId cannot be empty.");
                if (!maps.ContainsKey(n.MapId))
                    throw new InvalidDataException($"NPC '{n.NpcId}' map '{n.MapId}' not in version pack.");
                npcDict[n.NpcId] = n;
            }
        }

        // Shops
        var shopDict = new Dictionary<string, ShopConfig>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.ShopsConfigFile))
        {
            string shopsPath = Path.Combine(baseDir, manifest.ShopsConfigFile);
            if (!File.Exists(shopsPath))
                throw new FileNotFoundException($"Shops config not found: {shopsPath}");
            string shopsJson = File.ReadAllText(shopsPath, Encoding.UTF8);
            hashInput.Append(shopsJson);
            var shops = JsonSerializer.Deserialize<List<ShopConfig>>(shopsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize shops.json");
            foreach (var s in shops)
            {
                if (string.IsNullOrWhiteSpace(s.ShopId))
                    throw new InvalidDataException("ShopConfig.ShopId cannot be empty.");
                shopDict[s.ShopId] = s;
            }
        }

        // Quests
        var questDict = new Dictionary<string, QuestTemplate>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.QuestsConfigFile))
        {
            string questsPath = Path.Combine(baseDir, manifest.QuestsConfigFile);
            if (!File.Exists(questsPath))
                throw new FileNotFoundException($"Quests config not found: {questsPath}");
            string questsJson = File.ReadAllText(questsPath, Encoding.UTF8);
            hashInput.Append(questsJson);
            var quests = JsonSerializer.Deserialize<List<QuestTemplate>>(questsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize quests.json");
            foreach (var q in quests)
            {
                if (string.IsNullOrWhiteSpace(q.QuestId))
                    throw new InvalidDataException("QuestTemplate.QuestId cannot be empty.");
                if (!string.IsNullOrEmpty(q.AcceptNpcId) && !npcDict.ContainsKey(q.AcceptNpcId))
                    throw new InvalidDataException($"Quest '{q.QuestId}' acceptNpcId '{q.AcceptNpcId}' unknown.");
                if (!string.IsNullOrEmpty(q.TurnInNpcId) && !npcDict.ContainsKey(q.TurnInNpcId))
                    throw new InvalidDataException($"Quest '{q.QuestId}' turnInNpcId '{q.TurnInNpcId}' unknown.");
                questDict[q.QuestId] = q;
            }
        }

        // Items & Enhance rules
        var itemDict = new Dictionary<string, ItemTemplate>(StringComparer.OrdinalIgnoreCase);
        var enhanceRuleDict = new Dictionary<int, EnhanceRuleConfig>();
        if (!string.IsNullOrWhiteSpace(manifest.ItemsConfigFile))
        {
            string itemsPath = Path.Combine(baseDir, manifest.ItemsConfigFile);
            if (!File.Exists(itemsPath))
                throw new FileNotFoundException($"Items config not found: {itemsPath}");
            string itemsJson = File.ReadAllText(itemsPath, Encoding.UTF8);
            hashInput.Append(itemsJson);
            var itemsFile = JsonSerializer.Deserialize<ItemsConfigFile>(itemsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize items.json");
            foreach (var item in itemsFile.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ItemId))
                    throw new InvalidDataException("ItemTemplate.ItemId cannot be empty.");
                itemDict[item.ItemId] = item;
            }
            foreach (var r in itemsFile.EnhanceRules)
            {
                if (r.TargetLevel <= 0)
                    throw new InvalidDataException("EnhanceRuleConfig.TargetLevel must be > 0.");
                enhanceRuleDict[r.TargetLevel] = r;
            }
        }

        // Levels / growth curve
        var levelsConfig = new LevelsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.LevelsConfigFile))
        {
            string levelsPath = Path.Combine(baseDir, manifest.LevelsConfigFile);
            if (!File.Exists(levelsPath))
                throw new FileNotFoundException($"Levels config not found: {levelsPath}");
            string levelsJson = File.ReadAllText(levelsPath, Encoding.UTF8);
            hashInput.Append(levelsJson);
            levelsConfig = JsonSerializer.Deserialize<LevelsConfigFile>(levelsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize levels.json");
            if (levelsConfig.Entries.Count == 0)
                throw new InvalidDataException("levels.json must define at least one entry.");
        }

        // Cross-check NPC shop / quest refs
        foreach (var n in npcDict.Values)
        {
            if (!string.IsNullOrEmpty(n.ShopId) && !shopDict.ContainsKey(n.ShopId))
                throw new InvalidDataException($"NPC '{n.NpcId}' shopId '{n.ShopId}' unknown.");
            foreach (var qid in n.QuestIds)
            {
                if (!questDict.ContainsKey(qid))
                    throw new InvalidDataException($"NPC '{n.NpcId}' questId '{qid}' unknown.");
            }
        }

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
                skillCount = skillDict.Count,
                monsterCount = monsterDict.Count,
                lootTableCount = lootDict.Count,
                npcCount = npcDict.Count,
                shopCount = shopDict.Count,
                questCount = questDict.Count,
                itemCount = itemDict.Count,
                enhanceRuleCount = enhanceRuleDict.Count,
                levelEntryCount = levelsConfig.Entries.Count,
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
            Skills = skillDict,
            Monsters = monsterDict,
            MonsterSpawns = spawnList,
            LootTables = lootDict,
            Npcs = npcDict,
            Shops = shopDict,
            Quests = questDict,
            Items = itemDict,
            EnhanceRules = enhanceRuleDict,
            Levels = levelsConfig,
            LuaScripts = luaScripts,
            PackHash = packHash
        };
    }
}
