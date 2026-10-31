using System.Text.Json;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

public sealed class MonsterEntity
{
    public required string EntityId { get; init; }
    public required string MonsterId { get; init; }
    public required string Name { get; init; }
    public required string MapId { get; set; }
    public required MonsterTemplate Template { get; init; }
    public int SpawnX { get; init; }
    public int SpawnY { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public bool Alive { get; set; } = true;
    public string? TargetEntityId { get; set; }
    public long NextAttackUnixMs { get; set; }
    public long RespawnAtUnixMs { get; set; }
    public string? InstanceId { get; set; }
    public bool NoRespawn { get; set; }
    /// <summary>When set, this monster is a managed world boss instance.</summary>
    public string? WorldBossId { get; set; }

    public EntitySnapshot ToSnapshot() => new()
    {
        EntityId = EntityId,
        Name = Name,
        ClassId = MonsterId,
        MapId = MapId,
        X = X,
        Y = Y,
        Direction = 4,
        Hp = Hp,
        MaxHp = MaxHp,
        InSafeZone = false,
        Kind = EntityKind.EntityMonster,
        TemplateId = MonsterId
    };
}

public sealed class LootEntity
{
    public required string EntityId { get; init; }
    public required string MapId { get; set; }
    public required string ItemId { get; init; }
    public int Count { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public string? OwnerCharacterId { get; set; }
    public long ExpireUnixMs { get; set; }
    public long FreeForAllUnixMs { get; set; }

    public EntitySnapshot ToSnapshot() => new()
    {
        EntityId = EntityId,
        Name = ItemId,
        ClassId = "loot",
        MapId = MapId,
        X = X,
        Y = Y,
        Hp = 0,
        MaxHp = 0,
        Kind = EntityKind.EntityLoot,
        TemplateId = ItemId,
        Quantity = Count,
        OwnerCharacterId = OwnerCharacterId ?? string.Empty
    };
}

public sealed class NpcEntity
{
    public required string EntityId { get; init; }
    public required NpcTemplate Template { get; init; }
    public string NpcId => Template.NpcId;
    public string Name => Template.DisplayName;
    public string MapId => Template.MapId;
    public int X => Template.X;
    public int Y => Template.Y;

    public EntitySnapshot ToSnapshot() => new()
    {
        EntityId = EntityId,
        Name = Name,
        ClassId = "npc",
        MapId = MapId,
        X = X,
        Y = Y,
        Direction = 4,
        Hp = 1,
        MaxHp = 1,
        InSafeZone = true,
        Kind = EntityKind.EntityNpc,
        TemplateId = NpcId
    };
}

public static class InventoryHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static List<InventorySlot> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<InventorySlot>();
        try
        {
            return JsonSerializer.Deserialize<List<InventorySlot>>(json, JsonOpts) ?? new();
        }
        catch
        {
            return new List<InventorySlot>();
        }
    }

    public static string Serialize(IEnumerable<InventorySlot> slots)
        => JsonSerializer.Serialize(slots.Where(s => s.Count > 0).ToList(), JsonOpts);

    public static void AddItem(List<InventorySlot> bag, string itemId, int count)
    {
        var existing = bag.FirstOrDefault(s => string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.Count += count;
        else
            bag.Add(new InventorySlot { ItemId = itemId, Count = count });
    }

    public static bool TryRemoveItem(List<InventorySlot> bag, string itemId, int count)
    {
        var existing = bag.FirstOrDefault(s => string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
        if (existing is null || existing.Count < count)
            return false;
        existing.Count -= count;
        if (existing.Count <= 0)
            bag.Remove(existing);
        return true;
    }

    public static IEnumerable<InventoryItem> ToProto(IEnumerable<InventorySlot> slots)
        => slots.Select(s => new InventoryItem { ItemId = s.ItemId, Count = s.Count });
}

public static class QuestHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static List<QuestProgressState> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<QuestProgressState>();
        try
        {
            return JsonSerializer.Deserialize<List<QuestProgressState>>(json, JsonOpts) ?? new();
        }
        catch
        {
            return new List<QuestProgressState>();
        }
    }

    public static string Serialize(IEnumerable<QuestProgressState> quests)
        => JsonSerializer.Serialize(quests.ToList(), JsonOpts);

    public static QuestStatus ToProtoStatus(string status) => status.ToLowerInvariant() switch
    {
        "active" => QuestStatus.QuestActive,
        "ready" => QuestStatus.QuestReady,
        "completed" => QuestStatus.QuestCompleted,
        _ => QuestStatus.QuestNone
    };

    public static QuestProgress ToProto(QuestProgressState state, QuestTemplate? tmpl)
    {
        var qp = new QuestProgress
        {
            QuestId = state.QuestId,
            Status = ToProtoStatus(state.Status),
            Progress = state.Progress,
            Title = tmpl?.Title ?? state.QuestId,
            TargetCount = tmpl?.TargetCount ?? 0,
            TargetMonsterId = tmpl?.TargetMonsterId ?? "",
            Description = tmpl?.Description ?? ""
        };
        return qp;
    }
}

public static class CombatMath
{
    public static int Chebyshev(int x1, int y1, int x2, int y2)
        => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    /// <summary>damage = max(1, floor(atk * mult) - def)</summary>
    public static int CalcDamage(int atk, int def, double multiplier)
    {
        int raw = (int)Math.Floor(atk * multiplier);
        return Math.Max(1, raw - Math.Max(0, def));
    }
}

public static class EquipmentHelper
{
    public static readonly HashSet<string> ValidSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "weapon",
        "armor",
        "helmet",
        "necklace",
        "bracelet",
        "bracelet2",
        "ring",
        "ring2",
        "boots",
        "belt",
        "torch"
    };

    public static bool IsValidEquipSlot(string slot, EquipmentSlotsConfigFile? slotsConfig = null)
    {
        if (string.IsNullOrWhiteSpace(slot))
            return false;
        if (ValidSlots.Contains(slot))
            return true;
        if (slotsConfig?.ExtraSlots is null)
            return false;
        return slotsConfig.ExtraSlots.Any(s =>
            string.Equals(s.Id, slot, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static List<EquippedItemState> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<EquippedItemState>();
        try
        {
            var list = JsonSerializer.Deserialize<List<EquippedItemState>>(json, JsonOpts) ?? new List<EquippedItemState>();
            foreach (var eq in list)
            {
                if (eq.MaxDurability <= 0)
                {
                    eq.MaxDurability = PhaseMRules.DurabilityMax;
                    eq.Durability = PhaseMRules.DurabilityMax;
                }
            }
            return list;
        }
        catch
        {
            return new List<EquippedItemState>();
        }
    }

    public static string Serialize(IReadOnlyList<EquippedItemState> list)
        => JsonSerializer.Serialize(list, JsonOpts);

    public static (int AtkBonus, int DefBonus, int MaxHpBonus) ComputeSlotBonus(
        EquippedItemState eq,
        IReadOnlyDictionary<string, ItemTemplate> items,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, ArtifactUpgradeConfig>>? artifactUpgrades = null)
    {
        if (!items.TryGetValue(eq.ItemId, out var tmpl))
            return (0, 0, 0);
        int lvl = Math.Max(0, eq.EnhanceLevel);
        int atk = tmpl.AtkBonus + lvl * tmpl.EnhanceAtkPerLevel;
        int def = tmpl.DefBonus + lvl * tmpl.EnhanceDefPerLevel;
        int hp = tmpl.MaxHpBonus + lvl * tmpl.EnhanceHpPerLevel;
        if (string.Equals(tmpl.Kind, "artifact", StringComparison.OrdinalIgnoreCase)
            && artifactUpgrades is not null
            && artifactUpgrades.TryGetValue(tmpl.ItemId, out var byLevel))
        {
            int artLvl = Math.Max(1, eq.ArtifactLevel);
            if (byLevel.TryGetValue(artLvl, out var up))
            {
                foreach (var bonus in up.StatBonuses ?? new List<ArtifactStatBonusConfig>())
                {
                    if (string.IsNullOrWhiteSpace(bonus.StatId))
                        continue;
                    if (bonus.StatId.Equals("atk", StringComparison.OrdinalIgnoreCase))
                        atk += bonus.Value;
                    else if (bonus.StatId.Equals("def", StringComparison.OrdinalIgnoreCase))
                        def += bonus.Value;
                    else if (bonus.StatId.Equals("maxHp", StringComparison.OrdinalIgnoreCase)
                             || bonus.StatId.Equals("max_hp", StringComparison.OrdinalIgnoreCase))
                        hp += bonus.Value;
                }
            }
        }
        if (tmpl.NeedsIdentify && !eq.Identified)
            return (0, 0, 0);
        // Artifacts ignore durability break (no wear on artifact slots).
        if (string.Equals(tmpl.Kind, "artifact", StringComparison.OrdinalIgnoreCase))
            return (atk, def, hp);
        return PhaseMRules.ApplyBroken(atk, def, hp, eq.Durability, eq.MaxDurability);
    }

    public static EquippedSlot ToSlotProto(EquippedItemState eq, IReadOnlyDictionary<string, ItemTemplate> items)
    {
        var (atk, def, hp) = ComputeSlotBonus(eq, items);
        return new EquippedSlot
        {
            Slot = eq.Slot,
            ItemId = eq.ItemId,
            EnhanceLevel = eq.EnhanceLevel,
            AtkBonus = atk,
            DefBonus = def,
            MaxHpBonus = hp,
            Durability = eq.Durability,
            MaxDurability = eq.MaxDurability,
            Identified = eq.Identified
        };
    }

    public static IEnumerable<EquippedSlot> ToProto(
        IReadOnlyList<EquippedItemState> list,
        IReadOnlyDictionary<string, ItemTemplate> items)
        => list.Select(s => ToSlotProto(s, items));

    public static void RecalculateStats(
        WorldPlayerEntity player,
        IReadOnlyDictionary<string, ItemTemplate> items,
        IReadOnlyDictionary<string, BuffConfigItem>? buffs = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, ArtifactUpgradeConfig>>? artifactUpgrades = null)
    {
        int totalAtk = player.BaseAtk + Math.Max(0, player.FashionAtkBonus);
        int totalDef = player.BaseDef;
        int totalMaxHp = player.BaseMaxHp;

        var setCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var eq in player.Equipment)
        {
            var (atk, def, hp) = ComputeSlotBonus(eq, items, artifactUpgrades);
            totalAtk += atk;
            totalDef += def;
            totalMaxHp += hp;
            if (items.TryGetValue(eq.ItemId, out var tmpl) && !string.IsNullOrWhiteSpace(tmpl.SetId))
                setCounts[tmpl.SetId] = setCounts.GetValueOrDefault(tmpl.SetId) + 1;
        }
        foreach (var z in player.ZodiacSlots ?? new List<EquippedItemState>())
        {
            var (atk, def, hp) = ComputeSlotBonus(z, items, artifactUpgrades: null);
            totalAtk += atk;
            totalDef += def;
            totalMaxHp += hp;
        }
        foreach (var count in setCounts.Values)
        {
            if (count >= 2)
            {
                totalAtk += 3;
                totalDef += 3;
            }
        }

        player.Atk = totalAtk;
        player.Def = totalDef;
        player.MaxHp = Math.Max(1, totalMaxHp);
        if (player.Hp > player.MaxHp)
            player.Hp = player.MaxHp;

        if (buffs != null)
            BuffHelper.ApplyBuffModifiers(player, buffs);
    }

    public static void RecalculateStats(WorldPlayerEntity player, LoadedVersionPack pack)
    {
        // Gear first without buffs; apply talent/internal flat bonuses; then percent buffs.
        RecalculateStats(player, pack.Items, buffs: null, pack.ArtifactUpgrades);
        ApplyProgressionStatBonuses(player, pack);
        if (pack.Buffs.Count > 0)
            BuffHelper.ApplyBuffModifiers(player, pack.Buffs);
    }

    /// <summary>
    /// Fold unlocked talent nodes, internal level entry, and meridians into Atk/Def/MaxHp/MaxMp
    /// (docs/site/03). Called from the pack overload of RecalculateStats.
    /// </summary>
    public static void ApplyProgressionStatBonuses(WorldPlayerEntity player, LoadedVersionPack pack)
    {
        int atk = 0, def = 0, maxHp = 0, maxMp = 0;

        var entry = InternalProgress.GetEntry(pack.Internal, player.InternalLevel);
        if (entry is not null)
        {
            atk += entry.AtkBonus;
            def += entry.DefBonus;
            maxHp += entry.MaxHpBonus;
            maxMp += entry.MaxMpBonus;
        }

        void Fold(Dictionary<string, int> stats)
        {
            foreach (var (statId, value) in stats)
            {
                if (value == 0) continue;
                if (string.Equals(statId, "atk", StringComparison.OrdinalIgnoreCase))
                    atk += value;
                else if (string.Equals(statId, "def", StringComparison.OrdinalIgnoreCase))
                    def += value;
                else if (string.Equals(statId, "maxHp", StringComparison.OrdinalIgnoreCase))
                    maxHp += value;
                else if (string.Equals(statId, "maxMp", StringComparison.OrdinalIgnoreCase))
                    maxMp += value;
            }
        }

        Fold(InternalProgress.SumMeridianStats(
            pack.Internal, player.UnlockedMeridians ?? new List<string>()));
        Fold(TalentProgress.SumStatEffects(
            pack.Talents, player.TalentUnlocked ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)));

        player.Atk += atk;
        player.Def += def;
        player.MaxHp = Math.Max(1, player.MaxHp + maxHp);
        // MaxMp is not rebuilt in RecalculateStats gear path — reset from base then add bonuses.
        player.MaxMp = Math.Max(0, player.BaseMaxMp + maxMp);
        if (player.Hp > player.MaxHp)
            player.Hp = player.MaxHp;
        if (player.Mp > player.MaxMp)
            player.Mp = player.MaxMp;
    }
}

public sealed class LearnedSkillState
{
    public string SkillId { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
}

public sealed class ActiveBuffState
{
    public string BuffId { get; set; } = string.Empty;
    public long ExpiresAtUnixMs { get; set; }
    public long NextTickUnixMs { get; set; }
    public int Stacks { get; set; } = 1;
    public int AbsorbRemaining { get; set; }
}

public static class SkillHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static List<LearnedSkillState> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<LearnedSkillState>();
        try
        {
            return JsonSerializer.Deserialize<List<LearnedSkillState>>(json, JsonOpts) ?? new();
        }
        catch
        {
            return new List<LearnedSkillState>();
        }
    }

    public static string Serialize(IEnumerable<LearnedSkillState> skills)
        => JsonSerializer.Serialize(skills.ToList(), JsonOpts);

    public static int ExpToNext(SkillConfigItem cfg, int level)
    {
        if (cfg.ExpToNextLevel.Count == 0)
            return 0;
        // index = current level (exp needed to reach level+1); level 1 uses index 1
        if (level < 0)
            return 0;
        if (level >= cfg.MaxLevel)
            return 0;
        if (level < cfg.ExpToNextLevel.Count)
            return Math.Max(0, cfg.ExpToNextLevel[level]);
        return cfg.ExpToNextLevel[^1];
    }

    public static double EffectiveMultiplier(SkillConfigItem cfg, int level)
    {
        int lv = Math.Max(1, level);
        return cfg.DamageMultiplier + cfg.DamageMultiplierPerLevel * (lv - 1);
    }

    public static int EffectiveCooldownMs(SkillConfigItem cfg, int level)
    {
        int lv = Math.Max(1, level);
        int cd = cfg.CooldownMs + cfg.CooldownMsPerLevel * (lv - 1);
        return Math.Max(200, cd);
    }

    public static SkillState ToProto(LearnedSkillState state, SkillConfigItem? cfg)
    {
        int maxLv = cfg?.MaxLevel ?? Math.Max(1, state.Level);
        return new SkillState
        {
            SkillId = state.SkillId,
            DisplayName = cfg?.DisplayName ?? state.SkillId,
            Level = state.Level,
            Exp = state.Exp,
            ExpToNext = cfg is null ? 0 : ExpToNext(cfg, state.Level),
            MaxLevel = maxLv
        };
    }
}

public static class BuffHelper
{
    public static BuffState ToProto(ActiveBuffState state, BuffConfigItem? cfg)
    {
        return new BuffState
        {
            BuffId = state.BuffId,
            DisplayName = cfg?.DisplayName ?? state.BuffId,
            Kind = cfg?.Kind ?? "buff",
            ExpiresAtUnixMs = state.ExpiresAtUnixMs,
            Stacks = state.Stacks,
            AtkBonusPercent = cfg?.AtkBonusPercent ?? 0,
            DefBonusPercent = cfg?.DefBonusPercent ?? 0,
            MoveSlowPercent = cfg?.MoveSlowPercent ?? 0
        };
    }

    public static void ApplyBuffModifiers(WorldPlayerEntity player, IReadOnlyDictionary<string, BuffConfigItem> buffs)
    {
        int atkPct = 0;
        int defPct = 0;
        foreach (var ab in player.ActiveBuffs)
        {
            if (!buffs.TryGetValue(ab.BuffId, out var cfg))
                continue;
            atkPct += cfg.AtkBonusPercent * Math.Max(1, ab.Stacks);
            defPct += cfg.DefBonusPercent * Math.Max(1, ab.Stacks);
        }
        if (atkPct != 0)
            player.Atk = Math.Max(1, (int)Math.Floor(player.Atk * (100.0 + atkPct) / 100.0));
        if (defPct != 0)
            player.Def = Math.Max(0, (int)Math.Floor(player.Def * (100.0 + defPct) / 100.0));
    }
}

/// <summary>M16 companion pet entity (AOI Kind=Pet). One active pet per owner.</summary>
public sealed class PetEntity
{
    public required string EntityId { get; init; }
    public required string PetId { get; init; }
    public required string DisplayName { get; init; }
    public required string OwnerCharacterId { get; init; }
    public required string OwnerEntityId { get; set; }
    public required string MapId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int FollowDistance { get; set; } = 2;
    public int AssistDamagePercent { get; set; } = 25;
    public int AssistCooldownMs { get; set; } = 1200;
    public int AssistRange { get; set; } = 3;
    public string? AssistTargetEntityId { get; set; }
    public long NextAssistUnixMs { get; set; }
    public bool Alive { get; set; } = true;

    public EntitySnapshot ToSnapshot() => new()
    {
        EntityId = EntityId,
        Name = DisplayName,
        ClassId = "",
        MapId = MapId,
        X = X,
        Y = Y,
        Direction = 4,
        Hp = 1,
        MaxHp = 1,
        InSafeZone = false,
        Kind = EntityKind.EntityPet,
        TemplateId = PetId,
        OwnerCharacterId = OwnerCharacterId,
        PetId = PetId
    };
}
