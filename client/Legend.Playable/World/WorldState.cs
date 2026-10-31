using Legend.ClientCore;
using Legend.Contracts.Proto;

namespace Legend.Playable.World;

public enum FloatKind
{
    Damage,
    Miss,
    Heal,
    Kill
}

public sealed class GroundPatch
{
    public string MapId { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int Radius { get; init; } = 1;
    public double Life { get; set; } = 8;
}

public sealed class CastFx
{
    public double GridX { get; init; }
    public double GridY { get; init; }
    public string School { get; init; } = "warrior";
    public string Kind { get; init; } = "slash";
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
    public string NameColor { get; set; } = "white";
    public string Gender { get; set; } = "";
    public string Pose { get; set; } = "idle";
    public string ClassId { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public double PoseLock { get; set; }
    public double AnimPhase { get; set; }
    public double BuffUntil { get; set; }

    public void PlayPose(string pose, double lockSeconds)
    {
        Pose = pose;
        PoseLock = lockSeconds;
        AnimPhase = 0;
    }
    public string MountId { get; set; } = "";
    public string FashionId { get; set; } = "";
    public string Title { get; set; } = "";
    public int MoveIntervalMs { get; set; }
}

public sealed class WorldState
{
    private readonly Dictionary<string, ActorView> _actors = new(StringComparer.Ordinal);

    public IMovePrediction MovePrediction { get; }

    public WorldState(IMovePrediction? movePrediction = null)
    {
        MovePrediction = movePrediction ?? new MovePrediction();
    }

    public IReadOnlyDictionary<string, ActorView> Actors => _actors;
    public List<FloatText> Floats { get; } = new();
    public List<CastFx> Effects { get; } = new();
    public List<GroundPatch> Grounds { get; } = new();
    public double Time { get; private set; }
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
    {
        var actor = Upsert(e.EntityId, e.Name, e.Kind, e.X, e.Y, e.Direction, e.Hp, e.MaxHp, isSelf, e.InSafeZone);
        if (!string.IsNullOrWhiteSpace(e.NameColor))
            actor.NameColor = e.NameColor;
        if (e.Kind == EntityKind.EntityLoot)
            actor.Name = Data.ContentCatalog.ItemName(string.IsNullOrEmpty(e.TemplateId) ? e.Name : e.TemplateId);
        if (!string.IsNullOrWhiteSpace(e.Gender))
            actor.Gender = e.Gender;
        if (!string.IsNullOrWhiteSpace(e.ClassId))
            actor.ClassId = e.ClassId;
        if (!string.IsNullOrWhiteSpace(e.TemplateId))
            actor.TemplateId = e.TemplateId;
        actor.MountId = e.MountId ?? "";
        actor.FashionId = e.FashionId ?? "";
        if (!string.IsNullOrWhiteSpace(e.Title))
            actor.Title = e.Title;
        if (e.MoveIntervalMs > 0)
            actor.MoveIntervalMs = e.MoveIntervalMs;
        return actor;
    }

    public void SetNameColor(string entityId, string color)
    {
        if (Get(entityId) is { } actor && !string.IsNullOrWhiteSpace(color))
            actor.NameColor = color;
    }

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

    /// <summary>Optimistic local step (grid only). Paired with <see cref="ApplyMove"/>.</summary>
    public void PredictSelfMove(int toX, int toY, int direction)
    {
        var self = Self;
        if (self is null)
            return;
        MovePrediction.Begin(self.GridX, self.GridY, toX, toY);
        self.GridX = toX;
        self.GridY = toY;
        self.Direction = direction;
    }

    public void ApplyMove(MoveSync sync)
    {
        if (!_actors.TryGetValue(sync.EntityId, out var actor))
            return;

        bool isSelf = SelfId != null && string.Equals(sync.EntityId, SelfId, StringComparison.Ordinal);
        if (isSelf && MovePrediction.HasPending)
        {
            int serverX = sync.Accepted ? sync.ToX : sync.FromX;
            int serverY = sync.Accepted ? sync.ToY : sync.FromY;
            var result = MovePrediction.Resolve(sync.Accepted, serverX, serverY);
            actor.GridX = result.GridX;
            actor.GridY = result.GridY;
            if (sync.Accepted)
            {
                actor.Direction = sync.Direction;
                actor.InSafeZone = sync.InSafeZone;
            }
            actor.DrawX = result.GridX;
            actor.DrawY = result.GridY;
            return;
        }

        if (!sync.Accepted)
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
            actor.PlayPose("death", 1.2);
        }
        else
        {
            kind = FloatKind.Damage;
            text = "-" + cs.Damage;
            if (actor.PoseLock < 0.2)
                actor.PlayPose("hit", 0.22);
        }

        Floats.Add(new FloatText
        {
            GridX = actor.DrawX,
            GridY = actor.DrawY,
            Text = text,
            Kind = kind
        });
    }

    public void AddGround(string mapId, int x, int y, int radius, double seconds)
    {
        Grounds.Add(new GroundPatch { MapId = mapId, X = x, Y = y, Radius = Math.Max(1, radius), Life = Math.Max(1, seconds) });
    }

    public void AddCast(double x, double y, string school, string? kind = null)
    {
        string fx = kind ?? school switch
        {
            "mage" => "fire",
            "taoist" => "heal",
            _ => "slash"
        };
        Effects.Add(new CastFx { GridX = x, GridY = y, School = school, Kind = fx });
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
        Time += dt;
        double k = Math.Min(1, dt * 12);
        foreach (var actor in _actors.Values)
        {
            double dx = actor.GridX - actor.DrawX;
            double dy = actor.GridY - actor.DrawY;
            double step = Math.Sqrt(dx * dx + dy * dy);
            actor.AnimPhase += dt;
            if (actor.Hp <= 0)
            {
                if (actor.Pose != "death")
                    actor.PlayPose("death", 1.2);
            }
            else if (actor.PoseLock > 0)
            {
                actor.PoseLock -= dt;
            }
            else if (step < 0.04 && actor.Pose is "walk" or "run")
            {
                actor.Pose = "idle";
                actor.AnimPhase = 0;
            }
            actor.DrawX += dx * k;
            actor.DrawY += dy * k;
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
            if (Effects[i].Age > 0.62)
                Effects.RemoveAt(i);
        }
        for (int i = Grounds.Count - 1; i >= 0; i--)
        {
            Grounds[i].Life -= dt;
            if (Grounds[i].Life <= 0)
                Grounds.RemoveAt(i);
        }
    }
}
