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
