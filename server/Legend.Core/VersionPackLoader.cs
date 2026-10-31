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

    private readonly IConfigSchemaValidator _schemaValidator;

    public VersionPackLoader(IConfigSchemaValidator? schemaValidator = null)
    {
        _schemaValidator = schemaValidator ?? ConfigSchemaValidator.Instance;
    }

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
            _schemaValidator.Validate(ConfigSchemaIds.Map, mapJson, rel);
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
            _schemaValidator.Validate(ConfigSchemaIds.Skills, skillsJson, manifest.SkillsConfigFile);
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
            _schemaValidator.Validate(ConfigSchemaIds.Monsters, monstersJson, manifest.MonstersConfigFile);
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
            _schemaValidator.Validate(ConfigSchemaIds.LootTables, lootJson, manifest.LootTablesConfigFile);
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
            _schemaValidator.Validate(ConfigSchemaIds.Npcs, npcsJson, manifest.NpcsConfigFile);
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
            _schemaValidator.Validate(ConfigSchemaIds.Shops, shopsJson, manifest.ShopsConfigFile);
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
            _schemaValidator.Validate(ConfigSchemaIds.Quests, questsJson, manifest.QuestsConfigFile);
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
            _schemaValidator.Validate(ConfigSchemaIds.Items, itemsJson, manifest.ItemsConfigFile);
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

        EnsureCrossReferences(skillDict, monsterDict, lootDict, shopDict, questDict, itemDict, classDict);

        // Levels / growth curve
        var levelsConfig = new LevelsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.LevelsConfigFile))
        {
            string levelsPath = Path.Combine(baseDir, manifest.LevelsConfigFile);
            if (!File.Exists(levelsPath))
                throw new FileNotFoundException($"Levels config not found: {levelsPath}");
            string levelsJson = File.ReadAllText(levelsPath, Encoding.UTF8);
            hashInput.Append(levelsJson);
            _schemaValidator.Validate(ConfigSchemaIds.Levels, levelsJson, manifest.LevelsConfigFile);
            levelsConfig = JsonSerializer.Deserialize<LevelsConfigFile>(levelsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize levels.json");
            if (levelsConfig.Entries.Count == 0)
                throw new InvalidDataException("levels.json must define at least one entry.");
        }

        var guildConfig = new GuildConfig();
        if (!string.IsNullOrWhiteSpace(manifest.GuildConfigFile))
        {
            string guildPath = Path.Combine(baseDir, manifest.GuildConfigFile);
            if (!File.Exists(guildPath))
                throw new FileNotFoundException($"Guild config not found: {guildPath}");
            string guildJson = File.ReadAllText(guildPath, Encoding.UTF8);
            hashInput.Append(guildJson);
            _schemaValidator.Validate(ConfigSchemaIds.Guild, guildJson, manifest.GuildConfigFile);
            guildConfig = JsonSerializer.Deserialize<GuildConfig>(guildJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize guild.json");
        }

        var tradeConfig = new TradeConfig();
        if (!string.IsNullOrWhiteSpace(manifest.TradeConfigFile))
        {
            string tradePath = Path.Combine(baseDir, manifest.TradeConfigFile);
            if (!File.Exists(tradePath))
                throw new FileNotFoundException($"Trade config not found: {tradePath}");
            string tradeJson = File.ReadAllText(tradePath, Encoding.UTF8);
            hashInput.Append(tradeJson);
            _schemaValidator.Validate(ConfigSchemaIds.Trade, tradeJson, manifest.TradeConfigFile);
            tradeConfig = JsonSerializer.Deserialize<TradeConfig>(tradeJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize trade.json");
        }

        var partyConfig = new PartyConfig();
        if (!string.IsNullOrWhiteSpace(manifest.PartyConfigFile))
        {
            string partyPath = Path.Combine(baseDir, manifest.PartyConfigFile);
            if (!File.Exists(partyPath))
                throw new FileNotFoundException($"Party config not found: {partyPath}");
            string partyJson = File.ReadAllText(partyPath, Encoding.UTF8);
            hashInput.Append(partyJson);
            partyConfig = JsonSerializer.Deserialize<PartyConfig>(partyJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize party.json");
        }


        // Buffs
        var buffDict = new Dictionary<string, BuffConfigItem>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.BuffsConfigFile))
        {
            string buffsPath = Path.Combine(baseDir, manifest.BuffsConfigFile);
            if (!File.Exists(buffsPath))
                throw new FileNotFoundException($"Buffs config not found: {buffsPath}");
            string buffsJson = File.ReadAllText(buffsPath, Encoding.UTF8);
            hashInput.Append(buffsJson);
            var buffs = JsonSerializer.Deserialize<List<BuffConfigItem>>(buffsJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize buffs.json");
            foreach (var b in buffs)
            {
                if (string.IsNullOrWhiteSpace(b.BuffId))
                    throw new InvalidDataException("BuffConfigItem.BuffId cannot be empty.");
                buffDict[b.BuffId] = b;
            }
        }

        // Siege
        var siegeConfig = new SiegeConfig();
        if (!string.IsNullOrWhiteSpace(manifest.SiegeConfigFile))
        {
            string siegePath = Path.Combine(baseDir, manifest.SiegeConfigFile);
            if (!File.Exists(siegePath))
                throw new FileNotFoundException($"Siege config not found: {siegePath}");
            string siegeJson = File.ReadAllText(siegePath, Encoding.UTF8);
            hashInput.Append(siegeJson);
            siegeConfig = JsonSerializer.Deserialize<SiegeConfig>(siegeJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize siege.json");
            if (string.IsNullOrWhiteSpace(siegeConfig.CastleId))
                throw new InvalidDataException("SiegeConfig.CastleId cannot be empty.");
            if (!maps.ContainsKey(siegeConfig.CaptureZone.MapId))
                throw new InvalidDataException($"Siege captureZone map '{siegeConfig.CaptureZone.MapId}' not in version pack.");
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
                buffCount = buffDict.Count,
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


        var mailConfig = new MailConfig();
        if (!string.IsNullOrWhiteSpace(manifest.MailConfigFile))
        {
            string mailPath = Path.Combine(baseDir, manifest.MailConfigFile);
            if (!File.Exists(mailPath))
                throw new FileNotFoundException($"Mail config not found: {mailPath}");
            string mailJson = File.ReadAllText(mailPath);
            mailConfig = JsonSerializer.Deserialize<MailConfig>(mailJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to parse mail.json");
            if (mailConfig.MaxMailsPerCharacter <= 0)
                mailConfig.MaxMailsPerCharacter = 50;
            if (mailConfig.MaxInventorySlots <= 0)
                mailConfig.MaxInventorySlots = 40;
        }


        var dungeonsConfig = new DungeonsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.DungeonsConfigFile))
        {
            string dungeonPath = Path.Combine(baseDir, manifest.DungeonsConfigFile);
            if (!File.Exists(dungeonPath))
                throw new FileNotFoundException($"Dungeons config not found: {dungeonPath}");
            string dungeonJson = File.ReadAllText(dungeonPath);
            dungeonsConfig = JsonSerializer.Deserialize<DungeonsConfigFile>(dungeonJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to parse dungeons.json");
            foreach (var d in dungeonsConfig.Dungeons)
            {
                if (string.IsNullOrWhiteSpace(d.DungeonId))
                    throw new InvalidDataException("Dungeon missing dungeonId");
                if (!maps.ContainsKey(d.TemplateMapId))
                    throw new InvalidDataException($"Dungeon '{d.DungeonId}' templateMapId '{d.TemplateMapId}' not in version pack.");
                if (!maps.ContainsKey(d.ExitMapId) && !string.Equals(d.ExitMapId, defaultMap.MapId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Dungeon '{d.DungeonId}' exitMapId '{d.ExitMapId}' not in version pack.");
            }
        }


        var worldBossConfig = new WorldBossConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.WorldBossConfigFile))
        {
            string wbPath = Path.Combine(baseDir, manifest.WorldBossConfigFile);
            if (!File.Exists(wbPath))
                throw new FileNotFoundException($"World boss config not found: {wbPath}");
            string wbJson = File.ReadAllText(wbPath);
            worldBossConfig = JsonSerializer.Deserialize<WorldBossConfigFile>(wbJson, JsonOptions)
                ?? new WorldBossConfigFile();
            foreach (var b in worldBossConfig.Bosses)
            {
                if (string.IsNullOrWhiteSpace(b.BossId))
                    throw new InvalidDataException("World boss missing bossId");
                if (!maps.ContainsKey(b.MapId))
                    throw new InvalidDataException($"World boss '{b.BossId}' mapId '{b.MapId}' not in version pack.");
                if (!monsterDict.ContainsKey(b.MonsterId))
                    throw new InvalidDataException($"World boss '{b.BossId}' monsterId '{b.MonsterId}' not in monsters.json.");
            }
        }



        var currencyDict = new Dictionary<string, CurrencyConfigItem>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.CurrenciesConfigFile))
        {
            string currenciesPath = Path.Combine(baseDir, manifest.CurrenciesConfigFile);
            if (!File.Exists(currenciesPath))
                throw new FileNotFoundException($"Currencies config not found: {currenciesPath}");
            string currenciesJson = File.ReadAllText(currenciesPath, Encoding.UTF8);
            hashInput.Append(currenciesJson);
            _schemaValidator.Validate(ConfigSchemaIds.Currencies, currenciesJson, manifest.CurrenciesConfigFile);
            var currencyList = JsonSerializer.Deserialize<List<CurrencyConfigItem>>(currenciesJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize currencies.json");
            foreach (var c in currencyList)
            {
                if (string.IsNullOrWhiteSpace(c.CurrencyId))
                    throw new InvalidDataException("CurrencyConfigItem.CurrencyId cannot be empty.");
                if (currencyDict.ContainsKey(c.CurrencyId))
                    throw new InvalidDataException($"Duplicate currencyId '{c.CurrencyId}'.");
                if (string.IsNullOrWhiteSpace(c.DeductGroup))
                    c.DeductGroup = c.CurrencyId;
                currencyDict[c.CurrencyId] = c;
            }
        }
        else
        {
            // Implicit gold when pack omits currencies.json (legacy packs).
            currencyDict[CurrencyWallet.GoldId] = new CurrencyConfigItem
            {
                CurrencyId = CurrencyWallet.GoldId,
                DisplayName = "金币",
                Tradable = true,
                StallAllowed = true,
                AuctionAllowed = true,
                DeductGroup = CurrencyWallet.GoldId
            };
        }

        var recycleDict = new Dictionary<string, RecycleConfigItem>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.RecycleConfigFile))
        {
            string recyclePath = Path.Combine(baseDir, manifest.RecycleConfigFile);
            if (!File.Exists(recyclePath))
                throw new FileNotFoundException($"Recycle config not found: {recyclePath}");
            string recycleJson = File.ReadAllText(recyclePath, Encoding.UTF8);
            hashInput.Append(recycleJson);
            _schemaValidator.Validate(ConfigSchemaIds.Recycle, recycleJson, manifest.RecycleConfigFile);
            var recycleList = JsonSerializer.Deserialize<List<RecycleConfigItem>>(recycleJson, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize recycle.json");
            foreach (var r in recycleList)
            {
                if (string.IsNullOrWhiteSpace(r.ItemId))
                    throw new InvalidDataException("RecycleConfigItem.ItemId cannot be empty.");
                if (recycleDict.ContainsKey(r.ItemId))
                    throw new InvalidDataException($"Duplicate recycle itemId '{r.ItemId}'.");
                if (string.IsNullOrWhiteSpace(r.CurrencyId))
                    r.CurrencyId = CurrencyWallet.GoldId;
                if (!currencyDict.ContainsKey(r.CurrencyId))
                    throw new InvalidDataException($"Recycle item '{r.ItemId}' currencyId '{r.CurrencyId}' not in currencies.json.");
                if (itemDict.Count > 0 && !itemDict.ContainsKey(r.ItemId))
                    throw new InvalidDataException($"Recycle item '{r.ItemId}' not in items.json.");
                recycleDict[r.ItemId] = r;
            }
        }

        var mountsConfig = new MountsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.MountsConfigFile))
        {
            string mountsPath = Path.Combine(baseDir, manifest.MountsConfigFile);
            if (!File.Exists(mountsPath))
                throw new FileNotFoundException($"Mounts config not found: {mountsPath}");
            string mountsJson = File.ReadAllText(mountsPath);
            mountsConfig = JsonSerializer.Deserialize<MountsConfigFile>(mountsJson, JsonOptions)
                ?? new MountsConfigFile();
            hashInput.Append(mountsJson);
            foreach (var mnt in mountsConfig.Mounts)
            {
                if (string.IsNullOrWhiteSpace(mnt.MountId))
                    throw new InvalidDataException("Mount missing mountId");
            }
        }

        var fashionsConfig = new FashionsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.FashionsConfigFile))
        {
            string fashionPath = Path.Combine(baseDir, manifest.FashionsConfigFile);
            if (!File.Exists(fashionPath))
                throw new FileNotFoundException($"Fashions config not found: {fashionPath}");
            string fashionJson = File.ReadAllText(fashionPath);
            fashionsConfig = JsonSerializer.Deserialize<FashionsConfigFile>(fashionJson, JsonOptions)
                ?? new FashionsConfigFile();
            hashInput.Append(fashionJson);
            foreach (var f in fashionsConfig.Fashions)
            {
                if (string.IsNullOrWhiteSpace(f.FashionId))
                    throw new InvalidDataException("Fashion missing fashionId");
            }
        }


        var petsConfig = new PetsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.PetsConfigFile))
        {
            string petsPath = Path.Combine(baseDir, manifest.PetsConfigFile);
            if (!File.Exists(petsPath))
                throw new FileNotFoundException($"Pets config not found: {petsPath}");
            string petsJson = File.ReadAllText(petsPath);
            petsConfig = JsonSerializer.Deserialize<PetsConfigFile>(petsJson, JsonOptions)
                ?? new PetsConfigFile();
            hashInput.Append(petsJson);
            foreach (var pet in petsConfig.Pets)
            {
                if (string.IsNullOrWhiteSpace(pet.PetId))
                    throw new InvalidDataException("Pet missing petId");
            }
        }


        var talentsConfig = new TalentsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.TalentsConfigFile))
        {
            string talentsPath = Path.Combine(baseDir, manifest.TalentsConfigFile);
            if (!File.Exists(talentsPath))
                throw new FileNotFoundException($"Talents config not found: {talentsPath}");
            string talentsJson = File.ReadAllText(talentsPath, Encoding.UTF8);
            hashInput.Append(talentsJson);
            _schemaValidator.Validate(ConfigSchemaIds.Talents, talentsJson, manifest.TalentsConfigFile);
            talentsConfig = JsonSerializer.Deserialize<TalentsConfigFile>(talentsJson, JsonOptions)
                ?? new TalentsConfigFile();
            var treeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tree in talentsConfig.Trees)
            {
                if (string.IsNullOrWhiteSpace(tree.TreeId))
                    throw new InvalidDataException("Talent tree missing treeId.");
                if (!treeIds.Add(tree.TreeId))
                    throw new InvalidDataException($"Duplicate talent treeId '{tree.TreeId}'.");
                if (!string.IsNullOrWhiteSpace(tree.ClassId) && classDict.Count > 0 && !classDict.ContainsKey(tree.ClassId))
                    throw new InvalidDataException($"Talent tree '{tree.TreeId}' classId '{tree.ClassId}' not in classes.json.");
                var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in tree.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(node.NodeId))
                        throw new InvalidDataException($"Talent tree '{tree.TreeId}' has empty nodeId.");
                    if (!nodeIds.Add(node.NodeId))
                        throw new InvalidDataException($"Duplicate talent nodeId '{node.NodeId}' in tree '{tree.TreeId}'.");
                    foreach (var effect in node.Effects ?? new List<TalentEffectConfig>())
                    {
                        bool hasStat = !string.IsNullOrWhiteSpace(effect.StatId);
                        bool hasMod = !string.IsNullOrWhiteSpace(effect.SkillModifierId);
                        if (!hasStat && !hasMod)
                            throw new InvalidDataException($"Talent node '{node.NodeId}' effect needs statId or skillModifierId.");
                        if (hasStat && !StatIdWhitelist.IsAllowed(effect.StatId))
                            throw new InvalidDataException($"Talent node '{node.NodeId}' statId '{effect.StatId}' not in whitelist.");
                    }
                }
                foreach (var node in tree.Nodes)
                {
                    foreach (var req in node.Requires ?? new List<string>())
                    {
                        if (!nodeIds.Contains(req))
                            throw new InvalidDataException($"Talent node '{node.NodeId}' requires unknown '{req}'.");
                    }
                }
            }
            if (talentsConfig.ResetCost is not null
                && !string.IsNullOrWhiteSpace(talentsConfig.ResetCost.CurrencyId)
                && currencyDict.Count > 0
                && !currencyDict.ContainsKey(talentsConfig.ResetCost.CurrencyId))
            {
                throw new InvalidDataException(
                    $"Talent resetCost currencyId '{talentsConfig.ResetCost.CurrencyId}' not in currencies.json.");
            }
        }

        var internalConfig = new InternalConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.InternalConfigFile))
        {
            string internalPath = Path.Combine(baseDir, manifest.InternalConfigFile);
            if (!File.Exists(internalPath))
                throw new FileNotFoundException($"Internal config not found: {internalPath}");
            string internalJson = File.ReadAllText(internalPath, Encoding.UTF8);
            hashInput.Append(internalJson);
            _schemaValidator.Validate(ConfigSchemaIds.Internal, internalJson, manifest.InternalConfigFile);
            internalConfig = JsonSerializer.Deserialize<InternalConfigFile>(internalJson, JsonOptions)
                ?? new InternalConfigFile();
            if (internalConfig.Entries.Count == 0)
                throw new InvalidDataException("internal.json entries cannot be empty.");
            var levels = new HashSet<int>();
            foreach (var e in internalConfig.Entries)
            {
                if (!levels.Add(e.Level))
                    throw new InvalidDataException($"Duplicate internal level {e.Level}.");
            }
            var meridianIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in internalConfig.Meridians)
            {
                if (string.IsNullOrWhiteSpace(m.MeridianId))
                    throw new InvalidDataException("Meridian missing meridianId.");
                if (!meridianIds.Add(m.MeridianId))
                    throw new InvalidDataException($"Duplicate meridianId '{m.MeridianId}'.");
                foreach (var mat in m.Materials ?? new List<MeridianMaterialConfig>())
                {
                    if (itemDict.Count > 0 && !itemDict.ContainsKey(mat.ItemId))
                        throw new InvalidDataException($"Meridian '{m.MeridianId}' material '{mat.ItemId}' not in items.json.");
                }
                foreach (var effect in m.Effects ?? new List<TalentEffectConfig>())
                {
                    if (!string.IsNullOrWhiteSpace(effect.StatId) && !StatIdWhitelist.IsAllowed(effect.StatId))
                        throw new InvalidDataException($"Meridian '{m.MeridianId}' statId '{effect.StatId}' not in whitelist.");
                }
            }
        }


        var equipmentSlotsConfig = new EquipmentSlotsConfigFile();
        if (!string.IsNullOrWhiteSpace(manifest.EquipmentSlotsConfigFile))
        {
            string slotsPath = Path.Combine(baseDir, manifest.EquipmentSlotsConfigFile);
            if (!File.Exists(slotsPath))
                throw new FileNotFoundException($"Equipment slots config not found: {slotsPath}");
            string slotsJson = File.ReadAllText(slotsPath, Encoding.UTF8);
            hashInput.Append(slotsJson);
            _schemaValidator.Validate(ConfigSchemaIds.EquipmentSlots, slotsJson, manifest.EquipmentSlotsConfigFile);
            equipmentSlotsConfig = JsonSerializer.Deserialize<EquipmentSlotsConfigFile>(slotsJson, JsonOptions)
                ?? new EquipmentSlotsConfigFile();
            equipmentSlotsConfig.ExtraSlots ??= new();
            equipmentSlotsConfig.Zodiac ??= new ZodiacContainerConfig();
            var coreSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "weapon", "armor", "helmet", "necklace", "bracelet", "bracelet2",
                "ring", "ring2", "boots", "belt", "torch"
            };
            var extraIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in equipmentSlotsConfig.ExtraSlots)
            {
                if (string.IsNullOrWhiteSpace(slot.Id))
                    throw new InvalidDataException("equipment_slots extraSlots entry missing id.");
                if (!extraIds.Add(slot.Id))
                    throw new InvalidDataException($"Duplicate equipment slot id '{slot.Id}'.");
                if (coreSlots.Contains(slot.Id))
                    throw new InvalidDataException($"equipment_slots extraSlots id '{slot.Id}' collides with a core equip slot.");
            }
            if (equipmentSlotsConfig.Zodiac.SlotIds is null || equipmentSlotsConfig.Zodiac.SlotIds.Count == 0)
            {
                int n = Math.Max(1, equipmentSlotsConfig.Zodiac.SlotCount);
                equipmentSlotsConfig.Zodiac.SlotIds = Enumerable.Range(1, n)
                    .Select(i => $"zodiac_{i:00}")
                    .ToList();
            }
            if (string.IsNullOrWhiteSpace(equipmentSlotsConfig.Zodiac.ContainerId))
                equipmentSlotsConfig.Zodiac.ContainerId = "zodiac";
        }

        var artifactUpgradeDict = new Dictionary<string, IReadOnlyDictionary<int, ArtifactUpgradeConfig>>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.ArtifactsConfigFile))
        {
            string artPath = Path.Combine(baseDir, manifest.ArtifactsConfigFile);
            if (!File.Exists(artPath))
                throw new FileNotFoundException($"Artifacts config not found: {artPath}");
            string artJson = File.ReadAllText(artPath, Encoding.UTF8);
            hashInput.Append(artJson);
            _schemaValidator.Validate(ConfigSchemaIds.Artifacts, artJson, manifest.ArtifactsConfigFile);
            var artFile = JsonSerializer.Deserialize<ArtifactsConfigFile>(artJson, JsonOptions)
                ?? new ArtifactsConfigFile();
            var byItem = new Dictionary<string, Dictionary<int, ArtifactUpgradeConfig>>(StringComparer.OrdinalIgnoreCase);
            foreach (var up in artFile.Upgrades ?? new List<ArtifactUpgradeConfig>())
            {
                if (string.IsNullOrWhiteSpace(up.ArtifactItemId))
                    throw new InvalidDataException("artifacts upgrade missing artifactItemId.");
                if (up.Level < 1)
                    throw new InvalidDataException($"Artifact '{up.ArtifactItemId}' level must be >= 1.");
                if (itemDict.Count > 0 && !itemDict.ContainsKey(up.ArtifactItemId))
                    throw new InvalidDataException($"Artifact '{up.ArtifactItemId}' not in items.json.");
                if (!byItem.TryGetValue(up.ArtifactItemId, out var levels))
                {
                    levels = new Dictionary<int, ArtifactUpgradeConfig>();
                    byItem[up.ArtifactItemId] = levels;
                }
                if (!levels.TryAdd(up.Level, up))
                    throw new InvalidDataException($"Duplicate artifact level {up.Level} for '{up.ArtifactItemId}'.");
                foreach (var mat in up.Materials ?? new List<ArtifactMaterialConfig>())
                {
                    if (itemDict.Count > 0 && !itemDict.ContainsKey(mat.ItemId))
                        throw new InvalidDataException($"Artifact '{up.ArtifactItemId}' material '{mat.ItemId}' not in items.json.");
                }
                foreach (var bonus in up.StatBonuses ?? new List<ArtifactStatBonusConfig>())
                {
                    if (!string.IsNullOrWhiteSpace(bonus.StatId) && !StatIdWhitelist.IsAllowed(bonus.StatId))
                        throw new InvalidDataException($"Artifact '{up.ArtifactItemId}' L{up.Level} statId '{bonus.StatId}' not in whitelist.");
                }
            }
            foreach (var kv in byItem)
                artifactUpgradeDict[kv.Key] = kv.Value;
        }

        string profanityPath = Path.Combine(baseDir, "configs", "profanity.txt");
        if (File.Exists(profanityPath))
            PhasePRules.UseWordList(File.ReadAllLines(profanityPath));

        return new LoadedVersionPack
        {
            Manifest = manifest,
            BaseDirectory = baseDir,
            Map = defaultMap,
            Maps = maps,
            Classes = classDict,
            Skills = skillDict,
            Buffs = buffDict,
            Monsters = monsterDict,
            MonsterSpawns = spawnList,
            LootTables = lootDict,
            Npcs = npcDict,
            Shops = shopDict,
            Quests = questDict,
            Items = itemDict,
            EnhanceRules = enhanceRuleDict,
            Levels = levelsConfig,
            Guild = guildConfig,
            Trade = tradeConfig,
            Party = partyConfig,
            Siege = siegeConfig,
            Mail = mailConfig,
            Dungeons = dungeonsConfig,
            WorldBoss = worldBossConfig,
            Mounts = mountsConfig,
            Fashions = fashionsConfig,
            Pets = petsConfig,
            Currencies = currencyDict,
            Recycle = recycleDict,
            Talents = talentsConfig,
            Internal = internalConfig,
            EquipmentSlots = equipmentSlotsConfig,
            ArtifactUpgrades = artifactUpgradeDict,
            LuaScripts = luaScripts,
            PackHash = packHash
        };
    }

    private static void EnsureCrossReferences(
        IReadOnlyDictionary<string, SkillConfigItem> skills,
        IReadOnlyDictionary<string, MonsterTemplate> monsters,
        IReadOnlyDictionary<string, LootTableConfig> lootTables,
        IReadOnlyDictionary<string, ShopConfig> shops,
        IReadOnlyDictionary<string, QuestTemplate> quests,
        IReadOnlyDictionary<string, ItemTemplate> items,
        IReadOnlyDictionary<string, ClassConfigItem> classes)
    {
        foreach (var skill in skills.Values)
        {
            if (!string.IsNullOrEmpty(skill.SkillBookItemId) && !items.ContainsKey(skill.SkillBookItemId))
                throw new InvalidDataException($"Skill '{skill.SkillId}' skillBookItemId '{skill.SkillBookItemId}' is not in items.");
            foreach (string classId in skill.AllowedClasses)
            {
                if (!classes.ContainsKey(classId))
                    throw new InvalidDataException($"Skill '{skill.SkillId}' allowed class '{classId}' unknown.");
            }
        }

        foreach (var monster in monsters.Values)
        {
            if (!string.IsNullOrEmpty(monster.LootTableId) && !lootTables.ContainsKey(monster.LootTableId))
                throw new InvalidDataException($"Monster '{monster.MonsterId}' lootTableId '{monster.LootTableId}' is not in loot tables.");
        }

        foreach (var table in lootTables.Values)
        {
            foreach (var entry in table.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.ItemId) || !items.ContainsKey(entry.ItemId))
                    throw new InvalidDataException($"Loot table '{table.LootTableId}' itemId '{entry.ItemId}' is not in items.");
            }
        }

        foreach (var shop in shops.Values)
        {
            foreach (var offer in shop.Offers)
            {
                if (!items.ContainsKey(offer.ItemId))
                    throw new InvalidDataException($"Shop '{shop.ShopId}' itemId '{offer.ItemId}' is not in items.");
            }
        }

        foreach (var quest in quests.Values)
        {
            if (!string.IsNullOrEmpty(quest.TargetMonsterId) && !monsters.ContainsKey(quest.TargetMonsterId))
                throw new InvalidDataException($"Quest '{quest.QuestId}' targetMonsterId '{quest.TargetMonsterId}' unknown.");
            if (!string.IsNullOrEmpty(quest.PrerequisiteQuestId) && !quests.ContainsKey(quest.PrerequisiteQuestId))
                throw new InvalidDataException($"Quest '{quest.QuestId}' prerequisiteQuestId '{quest.PrerequisiteQuestId}' unknown.");
            foreach (var reward in quest.RewardItems)
            {
                if (!items.ContainsKey(reward.ItemId))
                    throw new InvalidDataException($"Quest '{quest.QuestId}' reward itemId '{reward.ItemId}' is not in items.");
            }
        }
    }
}
