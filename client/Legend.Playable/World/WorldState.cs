using Legend.Contracts.Proto;

namespace Legend.Playable.World;

public enum FloatKind
{
    Damage,
    Miss,
    Heal,
    Kill
}

public sealed class CastFx
{
    public double GridX { get; init; }
    public double GridY { get; init; }
    public string School { get; init; } = "warrior";
    public double Age { get; set; }
}

public sealed class FloatText
{
    public double GridX { get; init; }
    public double GridY { get; init; }
    public string Text { get; init; } = "";
    public FloatKind Kind { get; init; }
    public double Age { get; set; }
}

public sealed class ActorView
{
    public string EntityId { get; init; } = "";
    public string Name { get; set; } = "";
    public EntityKind Kind { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public double DrawX { get; set; }
    public double DrawY { get; set; }
    public int Direction { get; set; } = 4;
    public int Hp { get; set; }
    public int MaxHp { get; set; } = 1;
    public bool IsSelf { get; set; }
    public bool InSafeZone { get; set; }
}

public sealed class WorldState
{
    private readonly Dictionary<string, ActorView> _actors = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ActorView> Actors => _actors;
    public List<FloatText> Floats { get; } = new();
    public List<CastFx> Effects { get; } = new();
    public MapLayout Map { get; private set; } = MapCatalog.Get("novice_plain");
    public string? SelfId { get; private set; }
    public string? SelectedId { get; set; }
    public bool HasMoveGoal { get; private set; }
    public int GoalX { get; private set; }
    public int GoalY { get; private set; }

    public ActorView? Self => SelfId != null && _actors.TryGetValue(SelfId, out var a) ? a : null;

    public ActorView? Get(string? id)
        => id != null && _actors.TryGetValue(id, out var a) ? a : null;

    public void SetMap(string? mapId) => Map = MapCatalog.Get(mapId);

    public void ClearActors(bool keepSelf)
    {
        if (!keepSelf)
        {
            _actors.Clear();
            SelfId = null;
            SelectedId = null;
            ClearGoal();
            Floats.Clear();
            return;
        }

        var self = Self;
        _actors.Clear();
        SelectedId = null;
        if (self != null)
        {
            _actors[self.EntityId] = self;
            SelfId = self.EntityId;
        }
    }

    public void SetGoal(int x, int y)
    {
        HasMoveGoal = true;
        GoalX = x;
        GoalY = y;
    }

    public void ClearGoal() => HasMoveGoal = false;

    public ActorView Upsert(EntitySnapshot e, bool isSelf)
        => Upsert(e.EntityId, e.Name, e.Kind, e.X, e.Y, e.Direction, e.Hp, e.MaxHp, isSelf, e.InSafeZone);

    public ActorView Upsert(
        string id, string name, EntityKind kind, int x, int y, int dir, int hp, int maxHp, bool isSelf, bool inSafeZone)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("entity id required", nameof(id));
        bool fresh = !_actors.TryGetValue(id, out var actor);
        if (fresh || actor == null)
        {
            actor = new ActorView { EntityId = id, DrawX = x, DrawY = y };
            _actors[id] = actor;
        }

        actor.Name = name ?? "";
        actor.Kind = kind;
        actor.GridX = x;
        actor.GridY = y;
        actor.Direction = dir;
        actor.Hp = hp;
        actor.MaxHp = Math.Max(1, maxHp);
        actor.InSafeZone = inSafeZone;
        if (fresh || Math.Abs(actor.DrawX - x) > 3 || Math.Abs(actor.DrawY - y) > 3)
        {
            actor.DrawX = x;
            actor.DrawY = y;
        }

        if (isSelf || id == SelfId)
        {
            if (isSelf && SelfId != null && SelfId != id && _actors.TryGetValue(SelfId, out var old))
                old.IsSelf = false;
            if (isSelf)
                SelfId = id;
            actor.IsSelf = true;
        }
        else
        {
            actor.IsSelf = false;
        }

        return actor;
    }

    public void Remove(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return;
        _actors.Remove(id);
        if (SelectedId == id)
            SelectedId = null;
        if (SelfId == id)
            SelfId = null;
    }

    public void ApplyMove(MoveSync sync)
    {
        if (!sync.Accepted)
            return;
        if (!_actors.TryGetValue(sync.EntityId, out var actor))
            return;
        actor.GridX = sync.ToX;
        actor.GridY = sync.ToY;
        actor.Direction = sync.Direction;
        actor.InSafeZone = sync.InSafeZone;
        if (Math.Abs(actor.DrawX - sync.ToX) > 3 || Math.Abs(actor.DrawY - sync.ToY) > 3)
        {
            actor.DrawX = sync.ToX;
            actor.DrawY = sync.ToY;
        }
    }

    public void ApplyCombat(CombatSync cs)
    {
        if (!_actors.TryGetValue(cs.TargetEntityId, out var actor))
            return;
        actor.Hp = cs.TargetHp;
        if (cs.TargetMaxHp > 0)
            actor.MaxHp = cs.TargetMaxHp;
        FloatKind kind;
        string text;
        if (cs.Result == CombatResultKind.CombatMiss)
        {
            kind = FloatKind.Miss;
            text = "MISS";
        }
        else if (cs.Result == CombatResultKind.CombatHeal)
        {
            kind = FloatKind.Heal;
            text = "+" + cs.Heal;
        }
        else if (cs.TargetDied || cs.Result == CombatResultKind.CombatKill)
        {
            kind = FloatKind.Kill;
            text = "-" + cs.Damage;
            actor.Hp = 0;
        }
        else
        {
            kind = FloatKind.Damage;
            text = "-" + cs.Damage;
        }

        Floats.Add(new FloatText
        {
            GridX = actor.DrawX,
            GridY = actor.DrawY,
            Text = text,
            Kind = kind
        });
    }

    public void AddCast(double x, double y, string school)
    {
        Effects.Add(new CastFx { GridX = x, GridY = y, School = school });
    }

    public bool TryNextStep(out int toX, out int toY, out int dir)
    {
        toX = 0;
        toY = 0;
        dir = 4;
        var self = Self;
        if (self == null || !HasMoveGoal)
            return false;
        if (self.GridX == GoalX && self.GridY == GoalY)
        {
            ClearGoal();
            return false;
        }

        var (dx, dy) = GridMath.NextStep(self.GridX, self.GridY, GoalX, GoalY, Map.IsBlocked);
        if (dx == 0 && dy == 0)
        {
            ClearGoal();
            return false;
        }

        toX = self.GridX + dx;
        toY = self.GridY + dy;
        dir = GridMath.DirFromDelta(dx, dy);
        return true;
    }

    public string? CycleMonster(string? current)
    {
        var ids = _actors.Values
            .Where(a => !a.IsSelf && a.Kind == EntityKind.EntityMonster && a.Hp > 0)
            .Select(a => a.EntityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
            return null;
        int idx = 0;
        if (!string.IsNullOrEmpty(current))
        {
            int cur = ids.IndexOf(current);
            if (cur >= 0)
                idx = (cur + 1) % ids.Count;
        }
        return ids[idx];
    }

    public string? Nearest(EntityKind kind, int x, int y, int maxDist, bool livingOnly)
    {
        string? best = null;
        int bestD = maxDist;
        foreach (var actor in _actors.Values)
        {
            if (actor.IsSelf || actor.Kind != kind)
                continue;
            if (livingOnly && actor.Kind == EntityKind.EntityMonster && actor.Hp <= 0)
                continue;
            int d = GridMath.Chebyshev(x, y, actor.GridX, actor.GridY);
            if (d < bestD || (d == bestD && (best == null || string.CompareOrdinal(actor.EntityId, best) < 0)))
            {
                bestD = d;
                best = actor.EntityId;
            }
        }
        return best;
    }

    public void Tick(double dt)
    {
        double k = Math.Min(1, dt * 12);
        foreach (var actor in _actors.Values)
        {
            actor.DrawX += (actor.GridX - actor.DrawX) * k;
            actor.DrawY += (actor.GridY - actor.DrawY) * k;
        }

        for (int i = Floats.Count - 1; i >= 0; i--)
        {
            Floats[i].Age += dt;
            if (Floats[i].Age > 0.9)
                Floats.RemoveAt(i);
        }
        for (int i = Effects.Count - 1; i >= 0; i--)
        {
            Effects[i].Age += dt;
            if (Effects[i].Age > 0.45)
                Effects.RemoveAt(i);
        }
    }
}
