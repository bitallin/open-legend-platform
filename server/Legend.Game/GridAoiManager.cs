using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

public sealed class WorldPlayerEntity
{
    public required string EntityId { get; init; }
    public required string Name { get; init; }
    public required string ClassId { get; init; }
    public required string MapId { get; init; }
    public required IClientSession Session { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Direction { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }

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
        MaxHp = MaxHp
    };
}

/// <summary>
/// 2D Spatial Grid AOI (Area of Interest) Manager.
/// Partitions a map of (Width x Height) into cells of size AoiCellSize x AoiCellSize.
/// Queries and broadcasts within configurable cell radius (e.g., 3x3 surrounding cells).
/// </summary>
public sealed class GridAoiManager
{
    private readonly MapConfig _mapConfig;
    private readonly HashSet<(int X, int Y)> _blockedSet;
    private readonly ConcurrentDictionary<string, WorldPlayerEntity> _entities = new();
    private readonly object _gridLock = new();
    private readonly Dictionary<(int CellX, int CellY), HashSet<string>> _cellBuckets = new();

    public GridAoiManager(MapConfig mapConfig)
    {
        _mapConfig = mapConfig;
        _blockedSet = mapConfig.BlockedCells.Select(b => (b.X, b.Y)).ToHashSet();
    }

    public int OnlineCount => _entities.Count;

    public bool IsWalkable(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _mapConfig.Width || y >= _mapConfig.Height)
            return false;
        return !_blockedSet.Contains((x, y));
    }

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
                {
                    bucket.Remove(entity.EntityId);
                }
            }

            _entities[entity.EntityId] = entity;
            var newCell = ToCell(entity.X, entity.Y);
            if (!_cellBuckets.TryGetValue(newCell, out var newBucket))
            {
                newBucket = new HashSet<string>();
                _cellBuckets[newCell] = newBucket;
            }
            newBucket.Add(entity.EntityId);

            return GetNearbyEntitiesUnlocked(entity.X, entity.Y, excludeEntityId: entity.EntityId);
        }
    }

    public (List<WorldPlayerEntity> OldViewers, List<WorldPlayerEntity> NewViewers, List<WorldPlayerEntity> AllCurrentViewers) MoveEntity(
        string entityId, int toX, int toY, int direction)
    {
        lock (_gridLock)
        {
            if (!_entities.TryGetValue(entityId, out var entity))
                return (new(), new(), new());

            int fromX = entity.X;
            int fromY = entity.Y;
            var oldViewers = GetNearbyEntitiesUnlocked(fromX, fromY, excludeEntityId: entityId);

            var oldCell = ToCell(fromX, fromY);
            var newCell = ToCell(toX, toY);

            if (oldCell != newCell)
            {
                if (_cellBuckets.TryGetValue(oldCell, out var oldBucket))
                    oldBucket.Remove(entityId);

                if (!_cellBuckets.TryGetValue(newCell, out var newBucket))
                {
                    newBucket = new HashSet<string>();
                    _cellBuckets[newCell] = newBucket;
                }
                newBucket.Add(entityId);
            }

            entity.X = toX;
            entity.Y = toY;
            entity.Direction = direction;

            var currentViewers = GetNearbyEntitiesUnlocked(toX, toY, excludeEntityId: entityId);

            var oldIds = oldViewers.Select(v => v.EntityId).ToHashSet();
            var currentIds = currentViewers.Select(v => v.EntityId).ToHashSet();

            var leftViewers = oldViewers.Where(v => !currentIds.Contains(v.EntityId)).ToList();
            var enteredViewers = currentViewers.Where(v => !oldIds.Contains(v.EntityId)).ToList();

            return (leftViewers, enteredViewers, currentViewers);
        }
    }

    public List<WorldPlayerEntity> RemoveEntity(string entityId)
    {
        lock (_gridLock)
        {
            if (!_entities.TryRemove(entityId, out var entity))
                return new();

            var cell = ToCell(entity.X, entity.Y);
            if (_cellBuckets.TryGetValue(cell, out var bucket))
            {
                bucket.Remove(entityId);
            }

            return GetNearbyEntitiesUnlocked(entity.X, entity.Y, excludeEntityId: entityId);
        }
    }

    public bool TryGetEntity(string entityId, out WorldPlayerEntity? entity)
        => _entities.TryGetValue(entityId, out entity);

    private List<WorldPlayerEntity> GetNearbyEntitiesUnlocked(int x, int y, string? excludeEntityId)
    {
        var center = ToCell(x, y);
        int r = Math.Max(1, _mapConfig.AoiRadiusCells);
        var result = new List<WorldPlayerEntity>();

        for (int cx = center.CellX - r; cx <= center.CellX + r; cx++)
        {
            for (int cy = center.CellY - r; cy <= center.CellY + r; cy++)
            {
                if (_cellBuckets.TryGetValue((cx, cy), out var ids))
                {
                    foreach (string id in ids)
                    {
                        if (id == excludeEntityId)
                            continue;
                        if (_entities.TryGetValue(id, out var ent))
                        {
                            result.Add(ent);
                        }
                    }
                }
            }
        }
        return result;
    }
}
