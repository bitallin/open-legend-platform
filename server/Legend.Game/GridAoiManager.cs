using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed class WorldPlayerEntity
{
    public required string EntityId { get; init; }
    public required string CharacterId { get; init; }
    public required string Name { get; init; }
    public required string ClassId { get; init; }
    public required string MapId { get; set; }
    public required IClientSession Session { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Direction { get; set; }
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int BaseMaxHp { get; set; }
    public int BaseMaxMp { get; set; }
    public int BaseAtk { get; set; }
    public int BaseDef { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public int Atk { get; set; }
    public int Def { get; set; }
    public bool InSafeZone { get; set; }
    public bool IsDead { get; set; }
    public Dictionary<string, long> SkillReadyAtUnixMs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<InventorySlot> Inventory { get; set; } = new();
    /// <summary>Personal warehouse stacks. Persisted inside appearance JSON.</summary>
    public List<InventorySlot> Storage { get; set; } = new();
    public int Gold { get; set; }
    public List<QuestProgressState> Quests { get; set; } = new();
    public List<EquippedItemState> Equipment { get; set; } = new();
    public string GuildId { get; set; } = string.Empty;
    public string GuildName { get; set; } = string.Empty;
    public string GuildRank { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public List<LearnedSkillState> LearnedSkills { get; set; } = new();
    public List<ActiveBuffState> ActiveBuffs { get; set; } = new();
    public string? DungeonId { get; set; }
    public string? DungeonInstanceId { get; set; }
    public bool DungeonCompleted { get; set; }
    public Dictionary<string, DungeonProgressEntry> DungeonProgress { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Owned mount ids (persisted via appearance_json).</summary>
    public HashSet<string> OwnedMounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Currently summoned mount id (empty = on foot). Persisted optionally.</summary>
    public string ActiveMountId { get; set; } = string.Empty;
    public HashSet<string> OwnedFashions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string EquippedFashionId { get; set; } = string.Empty;
    /// <summary>ATK from equipped fashion; folded into RecalculateStats.</summary>
    public int FashionAtkBonus { get; set; }
    /// <summary>Authoritative min ms between accepted moves (lower = faster).</summary>
    public int MoveIntervalMs { get; set; } = 300;
    public long LastMoveUnixMs { get; set; }

    /// <summary>Owned pet template ids (appearance_json).</summary>
    public HashSet<string> OwnedPets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Active summoned pet template id (empty = none). Persisted.</summary>
    public string ActivePetId { get; set; } = string.Empty;
    /// <summary>Runtime pet world entity id (not persisted).</summary>
    public string ActivePetEntityId { get; set; } = string.Empty;
    public string AttackMode { get; set; } = "peace";
    public int PkKills { get; set; }
    public string Gender { get; set; } = "";
    public string MentorName { get; set; } = "";
    public List<string> Blacklist { get; set; } = new();

    public EntitySnapshot ToSnapshot() => new()
    {
        EntityId = EntityId,
        Name = Name,
        ClassId = ClassId,
        MapId = MapId,
        X = X,
        Y = Y,
        Direction = Direction,
        Hp = Hp,
        MaxHp = MaxHp,
        InSafeZone = InSafeZone,
        Kind = EntityKind.EntityPlayer,
        TemplateId = ClassId,
        MountId = ActiveMountId ?? string.Empty,
        FashionId = EquippedFashionId ?? string.Empty,
        MoveIntervalMs = MoveIntervalMs,
        PetId = ActivePetId ?? string.Empty,
        PkKills = PkKills,
        NameColor = PhaseMRules.NameColor(PkKills),
        AttackMode = string.IsNullOrEmpty(AttackMode) ? "peace" : AttackMode,
        Gender = Gender ?? "",
        Title = PhasePRules.Title(Level, GuildRank)
    };
}

/// <summary>Per-map grid AOI. Walkability uses MapConfig.IsBlocked (bitset or cells).</summary>
public sealed class GridAoiManager
{
    private readonly MapConfig _mapConfig;
    private readonly ConcurrentDictionary<string, WorldPlayerEntity> _entities = new();
    private readonly object _gridLock = new();
    private readonly Dictionary<(int CellX, int CellY), HashSet<string>> _cellBuckets = new();

    public GridAoiManager(MapConfig mapConfig)
    {
        _mapConfig = mapConfig;
        _mapConfig.EnsureBlockedCache();
    }

    public MapConfig Map => _mapConfig;
    public int OnlineCount => _entities.Count;

    public bool IsWalkable(int x, int y) => !_mapConfig.IsBlocked(x, y);

    public (int CellX, int CellY) ToCell(int x, int y)
    {
        int size = Math.Max(1, _mapConfig.AoiCellSize);
        return (x / size, y / size);
    }

    public List<WorldPlayerEntity> AddOrUpdateEntity(WorldPlayerEntity entity)
    {
        lock (_gridLock)
        {
            if (_entities.TryGetValue(entity.EntityId, out var existing))
            {
                var oldCell = ToCell(existing.X, existing.Y);
                if (_cellBuckets.TryGetValue(oldCell, out var bucket))
                    bucket.Remove(entity.EntityId);
            }

            entity.InSafeZone = DefaultMovementRules.Instance.IsInSafeZone(_mapConfig, entity.X, entity.Y);
            _entities[entity.EntityId] = entity;
            var cell = ToCell(entity.X, entity.Y);
            if (!_cellBuckets.TryGetValue(cell, out var newBucket))
            {
                newBucket = new HashSet<string>(StringComparer.Ordinal);
                _cellBuckets[cell] = newBucket;
            }
            newBucket.Add(entity.EntityId);
            return QueryNearbyLocked(entity.EntityId, entity.X, entity.Y);
        }
    }

    public bool TryGetEntity(string entityId, out WorldPlayerEntity? entity)
        => _entities.TryGetValue(entityId, out entity);

    public (List<WorldPlayerEntity> Left, List<WorldPlayerEntity> Entered, List<WorldPlayerEntity> Current)
        MoveEntity(string entityId, int toX, int toY, int direction)
    {
        lock (_gridLock)
        {
            if (!_entities.TryGetValue(entityId, out var entity))
                return (new(), new(), new());

            var oldNearby = QueryNearbyLocked(entityId, entity.X, entity.Y).Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);
            var oldCell = ToCell(entity.X, entity.Y);
            if (_cellBuckets.TryGetValue(oldCell, out var oldBucket))
                oldBucket.Remove(entityId);

            entity.X = toX;
            entity.Y = toY;
            entity.Direction = direction;
            entity.InSafeZone = DefaultMovementRules.Instance.IsInSafeZone(_mapConfig, toX, toY);

            var newCell = ToCell(toX, toY);
            if (!_cellBuckets.TryGetValue(newCell, out var newBucket))
            {
                newBucket = new HashSet<string>(StringComparer.Ordinal);
                _cellBuckets[newCell] = newBucket;
            }
            newBucket.Add(entityId);

            var current = QueryNearbyLocked(entityId, toX, toY);
            var currentIds = current.Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);
            var entered = current.Where(e => !oldNearby.Contains(e.EntityId)).ToList();
            var left = oldNearby.Where(id => !currentIds.Contains(id))
                .Select(id => _entities.TryGetValue(id, out var e) ? e : null)
                .Where(e => e != null)
                .Cast<WorldPlayerEntity>()
                .ToList();
            return (left, entered, current);
        }
    }

    public List<WorldPlayerEntity> RemoveEntity(string entityId)
    {
        lock (_gridLock)
        {
            if (!_entities.TryRemove(entityId, out var entity))
                return new();
            var viewers = QueryNearbyLocked(entityId, entity.X, entity.Y);
            var cell = ToCell(entity.X, entity.Y);
            if (_cellBuckets.TryGetValue(cell, out var bucket))
                bucket.Remove(entityId);
            return viewers;
        }
    }

    public List<WorldPlayerEntity> AllPlayers()
    {
        lock (_gridLock)
            return _entities.Values.ToList();
    }

    /// <summary>Players within AOI radius of a world cell (includes all in nearby buckets).</summary>
    public List<WorldPlayerEntity> QueryPlayersNear(int x, int y, string? excludeId = null)
    {
        lock (_gridLock)
            return QueryNearbyLocked(excludeId ?? string.Empty, x, y);
    }

    private List<WorldPlayerEntity> QueryNearbyLocked(string selfId, int x, int y)
    {
        int radius = Math.Max(0, _mapConfig.AoiRadiusCells);
        var (cx, cy) = ToCell(x, y);
        var result = new List<WorldPlayerEntity>();
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (!_cellBuckets.TryGetValue((cx + dx, cy + dy), out var bucket))
                    continue;
                foreach (string id in bucket)
                {
                    if (id == selfId)
                        continue;
                    if (_entities.TryGetValue(id, out var e))
                        result.Add(e);
                }
            }
        }
        return result;
    }
}
