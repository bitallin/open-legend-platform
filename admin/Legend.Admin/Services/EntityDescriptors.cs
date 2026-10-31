namespace Legend.Admin.Services;

/// <summary>Describes a content domain stored as JSON array (or maps/*.json files).</summary>
public sealed record EntityFieldDef(
    string Key,
    string Label,
    string InputKind, // text|number|select|textarea|json
    bool Required = false,
    string? SelectOptionsCsv = null,
    string? Hint = null);

public sealed record EntityDescriptor(
    string Kind,
    string TitleZh,
    string RelativePath,
    string IdProperty,
    string DisplayProperty,
    string[] ArrayKeys,
    string[] ListColumns,
    EntityFieldDef[] FormFields,
    Func<Dictionary<string, string>, Dictionary<string, object?>> BuildDefaults,
    string? LookAssetProperty = null, // e.g. look.spriteAssetId or iconAssetId
    bool IsMapFiles = false);

public static class EntityCatalog
{
    public static readonly EntityDescriptor Monsters = new(
        Kind: "monsters",
        TitleZh: "怪物",
        RelativePath: "configs/monsters.json",
        IdProperty: "monsterId",
        DisplayProperty: "displayName",
        ArrayKeys: ["templates", "monsters"],
        ListColumns: ["monsterId", "displayName", "maxHp", "atk", "def", "lootTableId", "expReward"],
        FormFields:
        [
            new("monsterId", "怪物 ID", "text", true, Hint: "英文蛇形，创建后不可改建议"),
            new("displayName", "显示名", "text", true),
            new("maxHp", "最大生命", "number", true),
            new("atk", "攻击", "number"),
            new("def", "防御", "number"),
            new("aggroRange", "仇恨范围", "number"),
            new("attackRange", "攻击距离", "number"),
            new("attackCooldownMs", "攻击间隔 ms", "number"),
            new("lootTableId", "掉落表 ID", "text"),
            new("respawnMs", "复活间隔 ms", "number"),
            new("ai", "AI", "select", SelectOptionsCsv: "chase,stationary,patrol"),
            new("expReward", "经验奖励", "number"),
            new("look.spriteAssetId", "精灵素材", "asset", Hint: "素材库 ID"),
            new("look.iconAssetId", "图标素材", "asset"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["monsterId"] = id["monsterId"],
            ["displayName"] = id.GetValueOrDefault("displayName") ?? id["monsterId"],
            ["maxHp"] = 50,
            ["atk"] = 5,
            ["def"] = 1,
            ["aggroRange"] = 5,
            ["attackRange"] = 1,
            ["attackCooldownMs"] = 1200,
            ["lootTableId"] = "",
            ["respawnMs"] = 10000,
            ["ai"] = "chase",
            ["expReward"] = 20,
        },
        LookAssetProperty: "look.spriteAssetId|look.iconAssetId");

    public static readonly EntityDescriptor Items = new(
        Kind: "items",
        TitleZh: "物品/装备",
        RelativePath: "configs/items.json",
        IdProperty: "itemId",
        DisplayProperty: "displayName",
        ArrayKeys: ["items", "templates"],
        ListColumns: ["itemId", "displayName", "kind", "slot", "atkBonus", "defBonus", "requiredLevel"],
        FormFields:
        [
            new("itemId", "物品 ID", "text", true),
            new("displayName", "显示名", "text", true),
            new("kind", "类型", "select", SelectOptionsCsv: "consumable,equipment,material,quest,other"),
            new("slot", "装备槽", "text", Hint: "weapon/armor/... 或空"),
            new("healHp", "回血", "number"),
            new("healMp", "回蓝", "number"),
            new("atkBonus", "攻击加成", "number"),
            new("defBonus", "防御加成", "number"),
            new("maxHpBonus", "生命加成", "number"),
            new("requiredLevel", "需求等级", "number"),
            new("weight", "负重", "number"),
            new("paperdoll", "纸娃娃键", "text"),
            new("iconAssetId", "图标素材", "asset"),
            new("dropSpriteAssetId", "掉落图素材", "asset"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["itemId"] = id["itemId"],
            ["displayName"] = id.GetValueOrDefault("displayName") ?? id["itemId"],
            ["kind"] = "material",
            ["slot"] = "",
            ["weight"] = 1,
        },
        LookAssetProperty: "iconAssetId|dropSpriteAssetId");

    public static readonly EntityDescriptor Npcs = new(
        Kind: "npcs",
        TitleZh: "NPC",
        RelativePath: "configs/npcs.json",
        IdProperty: "npcId",
        DisplayProperty: "displayName",
        ArrayKeys: ["npcs", "templates"],
        ListColumns: ["npcId", "displayName", "mapId", "x", "y", "shopId"],
        FormFields:
        [
            new("npcId", "NPC ID", "text", true),
            new("displayName", "显示名", "text", true),
            new("mapId", "地图 ID", "text"),
            new("x", "X", "number"),
            new("y", "Y", "number"),
            new("shopId", "商店 ID", "text"),
            new("dialogue", "对话", "textarea"),
            new("canRepair", "可修理", "select", SelectOptionsCsv: "true,false"),
            new("canStore", "可仓库", "select", SelectOptionsCsv: "true,false"),
            new("look.spriteAssetId", "精灵素材", "asset"),
            new("look.iconAssetId", "图标素材", "asset"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["npcId"] = id["npcId"],
            ["displayName"] = id.GetValueOrDefault("displayName") ?? id["npcId"],
            ["mapId"] = "novice_plain",
            ["x"] = 10,
            ["y"] = 10,
            ["shopId"] = "",
            ["dialogue"] = "你好，旅行者。",
            ["questIds"] = Array.Empty<string>(),
            ["canRepair"] = false,
            ["canStore"] = false,
        },
        LookAssetProperty: "look.spriteAssetId|look.iconAssetId");

    public static readonly EntityDescriptor Skills = new(
        Kind: "skills",
        TitleZh: "技能",
        RelativePath: "configs/skills.json",
        IdProperty: "skillId",
        DisplayProperty: "displayName",
        ArrayKeys: ["skills", "templates"],
        ListColumns: ["skillId", "displayName", "mpCost", "cooldownMs", "range", "damageMultiplier", "maxLevel"],
        FormFields:
        [
            new("skillId", "技能 ID", "text", true),
            new("displayName", "显示名", "text", true),
            new("mpCost", "耗蓝", "number"),
            new("cooldownMs", "冷却 ms", "number"),
            new("range", "距离", "number"),
            new("damageMultiplier", "伤害倍率", "number"),
            new("damageMultiplierPerLevel", "每级倍率", "number"),
            new("maxLevel", "最大等级", "number"),
            new("requiresLearn", "需学习", "select", SelectOptionsCsv: "true,false"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["skillId"] = id["skillId"],
            ["displayName"] = id.GetValueOrDefault("displayName") ?? id["skillId"],
            ["mpCost"] = 10,
            ["cooldownMs"] = 1000,
            ["range"] = 1,
            ["damageMultiplier"] = 1.0,
            ["damageMultiplierPerLevel"] = 0.1,
            ["allowedClasses"] = Array.Empty<string>(),
            ["requiresLearn"] = true,
            ["maxLevel"] = 3,
            ["skillExpPerCast"] = 1,
            ["skillExpPerKill"] = 5,
        });

    public static readonly EntityDescriptor Quests = new(
        Kind: "quests",
        TitleZh: "任务",
        RelativePath: "configs/quests.json",
        IdProperty: "questId",
        DisplayProperty: "title",
        ArrayKeys: ["quests", "templates"],
        ListColumns: ["questId", "title", "acceptNpcId", "objectiveType", "targetMonsterId", "targetCount", "rewardGold"],
        FormFields:
        [
            new("questId", "任务 ID", "text", true),
            new("title", "标题", "text", true),
            new("description", "描述", "textarea"),
            new("acceptNpcId", "接取 NPC", "text"),
            new("turnInNpcId", "交付 NPC", "text"),
            new("objectiveType", "目标类型", "select", SelectOptionsCsv: "kill,collect,talk,explore"),
            new("targetMonsterId", "目标怪物", "text"),
            new("targetCount", "目标数量", "number"),
            new("rewardGold", "金币奖励", "number"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["questId"] = id["questId"],
            ["title"] = id.GetValueOrDefault("displayName") ?? id["questId"],
            ["description"] = "",
            ["acceptNpcId"] = "",
            ["turnInNpcId"] = "",
            ["objectiveType"] = "kill",
            ["targetMonsterId"] = "",
            ["targetCount"] = 1,
            ["rewardGold"] = 100,
            ["rewardItems"] = Array.Empty<object>(),
        });

    public static readonly EntityDescriptor Shops = new(
        Kind: "shops",
        TitleZh: "商店",
        RelativePath: "configs/shops.json",
        IdProperty: "shopId",
        DisplayProperty: "displayName",
        ArrayKeys: ["shops", "templates"],
        ListColumns: ["shopId", "displayName"],
        FormFields:
        [
            new("shopId", "商店 ID", "text", true),
            new("displayName", "显示名", "text", true),
            new("offers", "货架 JSON", "json", Hint: "数组 [{itemId,price,currencyId}]"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["shopId"] = id["shopId"],
            ["displayName"] = id.GetValueOrDefault("displayName") ?? id["shopId"],
            ["offers"] = Array.Empty<object>(),
        });

    public static readonly EntityDescriptor Loot = new(
        Kind: "loot",
        TitleZh: "掉落表",
        RelativePath: "configs/loot_tables.json",
        IdProperty: "lootTableId",
        DisplayProperty: "lootTableId",
        ArrayKeys: ["lootTables", "templates", "tables"],
        ListColumns: ["lootTableId"],
        FormFields:
        [
            new("lootTableId", "掉落表 ID", "text", true),
            new("entries", "条目 JSON", "json", Hint: "数组 [{itemId,chance,minQty,maxQty}]"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["lootTableId"] = id["lootTableId"],
            ["entries"] = Array.Empty<object>(),
        });

    public static readonly EntityDescriptor Maps = new(
        Kind: "maps",
        TitleZh: "地图",
        RelativePath: "maps/",
        IdProperty: "mapId",
        DisplayProperty: "mapName",
        ArrayKeys: [],
        ListColumns: ["mapId", "mapName", "width", "height", "spawnX", "spawnY"],
        FormFields:
        [
            new("mapId", "地图 ID", "text", true),
            new("mapName", "显示名", "text", true),
            new("width", "宽", "number", true),
            new("height", "高", "number", true),
            new("aoiCellSize", "AOI 格", "number"),
            new("aoiRadiusCells", "AOI 半径", "number"),
            new("spawnX", "出生 X", "number"),
            new("spawnY", "出生 Y", "number"),
            new("look.minimapAssetId", "小地图素材", "asset"),
            new("look.tileAssetId", "地砖素材", "asset"),
            new("look.backdropAssetId", "背景素材", "asset"),
        ],
        BuildDefaults: id => new Dictionary<string, object?>
        {
            ["mapId"] = id["mapId"],
            ["mapName"] = id.GetValueOrDefault("displayName") ?? id["mapId"],
            ["width"] = 64,
            ["height"] = 64,
            ["aoiCellSize"] = 8,
            ["aoiRadiusCells"] = 1,
            ["spawnX"] = 10,
            ["spawnY"] = 10,
            ["blockedCells"] = Array.Empty<object>(),
            ["safeZones"] = Array.Empty<object>(),
            ["portals"] = Array.Empty<object>(),
            ["spawns"] = Array.Empty<object>(),
        },
        LookAssetProperty: "look.minimapAssetId|look.tileAssetId|look.backdropAssetId",
        IsMapFiles: true);

    public static IReadOnlyList<EntityDescriptor> All { get; } =
        [Monsters, Items, Npcs, Skills, Quests, Shops, Loot, Maps];

    public static EntityDescriptor? Get(string kind) =>
        All.FirstOrDefault(d => string.Equals(d.Kind, kind, StringComparison.OrdinalIgnoreCase));
}
