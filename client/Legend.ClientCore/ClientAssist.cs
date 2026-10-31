using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legend.ClientCore;

public enum AssistCommandKind
{
    None = 0,
    Move = 1,
    Attack = 2,
    Pickup = 3,
    UseItem = 4,
    Stop = 5
}

public readonly record struct AssistCommand(
    AssistCommandKind Kind,
    int ToX = 0,
    int ToY = 0,
    string TargetEntityId = "",
    string SkillId = "",
    string ItemId = "",
    string Reason = "");

public sealed class AssistWorldView
{
    public int SelfX { get; set; }
    public int SelfY { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; } = 1;
    public int Mp { get; set; }
    public int MaxMp { get; set; } = 1;
    public bool IsDead { get; set; }
    public bool MapNoHang { get; set; }
    public bool MapNoAutoPickup { get; set; }
    public bool OnPortal { get; set; }
    public string MapId { get; set; } = "";
    public IReadOnlyList<AssistEntity> Monsters { get; set; } = Array.Empty<AssistEntity>();
    public IReadOnlyList<AssistLoot> Loot { get; set; } = Array.Empty<AssistLoot>();
    public IReadOnlySet<string> LearnedSkills { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> ActiveBuffIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, int> InventoryCounts { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public long NowUnixMs { get; set; }
}

public sealed class AssistEntity
{
    public string EntityId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Kind { get; set; } = "monster";
    public List<string> Tags { get; set; } = new();
    public bool Alive { get; set; } = true;
}

public sealed class AssistLoot
{
    public string EntityId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class ClientAssistSettings
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "anchor";
    public int AnchorRadiusCells { get; set; } = 12;
    public List<AssistPoint> PatrolPoints { get; set; } = new();
    public int SearchRangeCells { get; set; } = 8;
    public int MaxPullCount { get; set; } = 3;
    public List<string> IgnoreTags { get; set; } = new();
    public List<string> DoNotAttackEntityKinds { get; set; } = new();
    public bool StopOnPortal { get; set; } = true;
    public bool StopOnMapChange { get; set; } = true;
    public bool StopOnDeath { get; set; } = true;
    public bool StopOnManualMove { get; set; } = true;
    public int GlobalCastGapMs { get; set; } = 350;
    public List<AssistSkillSetting> Skills { get; set; } = new();
    public AssistPickupSetting Pickup { get; set; } = new();
    public List<AssistPotionSetting> HpPotions { get; set; } = new();
    public List<AssistPotionSetting> MpPotions { get; set; } = new();
    public double EmergencyScrollHpPercent { get; set; } = 15;
    public string EmergencyScrollItemId { get; set; } = "item_town_scroll";

    public static ClientAssistSettings FromJson(string json)
    {
        var cfg = JsonSerializer.Deserialize<ClientAssistSettings>(json, JsonOpts) ?? new ClientAssistSettings();
        cfg.Normalize();
        // Pack / file defaults must remain off; player enables locally.
        cfg.Enabled = false;
        return cfg;
    }

    public void Normalize()
    {
        PatrolPoints ??= new();
        IgnoreTags ??= new();
        DoNotAttackEntityKinds ??= new();
        Skills ??= new();
        Pickup ??= new AssistPickupSetting();
        Pickup.PriorityItemIds ??= new();
        HpPotions ??= new();
        MpPotions ??= new();
        if (SearchRangeCells < 1) SearchRangeCells = 8;
        if (MaxPullCount < 1) MaxPullCount = 1;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class AssistPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class AssistSkillSetting
{
    public string SkillId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public double MinMpPercent { get; set; }
    public double? MinHpPercent { get; set; }
    public int MinMobCount { get; set; }
    public string? RequireBuffMissing { get; set; }
}

public sealed class AssistPickupSetting
{
    public bool Enabled { get; set; } = true;
    public int PickupRangeCells { get; set; } = 3;
    public int DelayMs { get; set; } = 200;
    public bool PauseAttackWhilePicking { get; set; } = true;
    public List<string> PriorityItemIds { get; set; } = new();
}

public sealed class AssistPotionSetting
{
    public string ItemId { get; set; } = "";
    public double HpPercent { get; set; }
    public double MpPercent { get; set; }
    public int Priority { get; set; }
    public int MinIntervalMs { get; set; } = 800;
}

public sealed class PickupFilterSettings
{
    public List<PickupFilterProfileSetting> Profiles { get; set; } = new();

    public static PickupFilterSettings FromJson(string json) =>
        JsonSerializer.Deserialize<PickupFilterSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new PickupFilterSettings();
}

public sealed class PickupFilterProfileSetting
{
    public string ProfileId { get; set; } = "";
    public bool DefaultForAssist { get; set; }
    public List<PickupFilterRuleSetting> Rules { get; set; } = new();
}

public sealed class PickupFilterRuleSetting
{
    public string ItemId { get; set; } = "";
    public bool AssistPickup { get; set; } = true;
    public int Priority { get; set; }
}

/// <summary>
/// Client-only assist / autoplay state machine (docs/site/19). Emits intents for existing
/// Move/Attack/Pickup/UseItem messages. Default off. No privileged MsgIds.
/// </summary>
public sealed class ClientAssistEngine
{
    private readonly ClientAssistSettings _cfg;
    private readonly PickupFilterSettings _filters;
    private int _anchorX;
    private int _anchorY;
    private int _patrolIndex;
    private long _nextCastUnixMs;
    private long _nextPickupUnixMs;
    private long _nextHpPotionUnixMs;
    private long _nextMpPotionUnixMs;
    private string _mapId = "";

    public bool Running { get; private set; }
    public string LastStopReason { get; private set; } = "";

    public ClientAssistEngine(ClientAssistSettings cfg, PickupFilterSettings? filters = null)
    {
        _cfg = cfg ?? new ClientAssistSettings();
        _cfg.Normalize();
        _filters = filters ?? new PickupFilterSettings();
        _filters.Profiles ??= new();
    }

    public bool TryStart(AssistWorldView world, out string reason)
    {
        reason = "";
        if (world.MapNoHang)
        {
            reason = "map noHang";
            Running = false;
            LastStopReason = reason;
            return false;
        }
        if (world.IsDead)
        {
            reason = "dead";
            return false;
        }
        Running = true;
        LastStopReason = "";
        _anchorX = world.SelfX;
        _anchorY = world.SelfY;
        _mapId = world.MapId ?? "";
        _patrolIndex = 0;
        return true;
    }

    public void Stop(string reason = "manual")
    {
        Running = false;
        LastStopReason = reason;
    }

    public void NotifyManualMove()
    {
        if (Running && _cfg.StopOnManualMove)
            Stop("manual_move");
    }

    public AssistCommand Tick(AssistWorldView world)
    {
        if (!Running)
            return default;

        if (world.MapNoHang)
        {
            Stop("map noHang");
            return new AssistCommand(AssistCommandKind.Stop, Reason: "map noHang");
        }
        if (_cfg.StopOnDeath && world.IsDead)
        {
            Stop("death");
            return new AssistCommand(AssistCommandKind.Stop, Reason: "death");
        }
        if (_cfg.StopOnMapChange && !string.IsNullOrEmpty(_mapId)
            && !string.Equals(_mapId, world.MapId, StringComparison.OrdinalIgnoreCase))
        {
            Stop("map_change");
            return new AssistCommand(AssistCommandKind.Stop, Reason: "map_change");
        }
        if (_cfg.StopOnPortal && world.OnPortal)
        {
            Stop("portal");
            return new AssistCommand(AssistCommandKind.Stop, Reason: "portal");
        }

        var protect = TryProtect(world);
        if (protect.Kind != AssistCommandKind.None)
            return protect;

        if (_cfg.Pickup.Enabled && !world.MapNoAutoPickup)
        {
            var pick = TryPickup(world);
            if (pick.Kind != AssistCommandKind.None)
                return pick;
        }

        var target = SelectMonster(world);
        if (target is null)
            return IdleMove(world);

        string skillId = SelectSkill(world);
        if (string.IsNullOrEmpty(skillId))
            skillId = "basic_slash";

        int range = 1;
        int dist = Chebyshev(world.SelfX, world.SelfY, target.X, target.Y);
        if (dist > range)
        {
            var (nx, ny) = StepToward(world.SelfX, world.SelfY, target.X, target.Y);
            return new AssistCommand(AssistCommandKind.Move, ToX: nx, ToY: ny, Reason: "approach");
        }

        if (world.NowUnixMs < _nextCastUnixMs)
            return default;

        _nextCastUnixMs = world.NowUnixMs + Math.Max(0, _cfg.GlobalCastGapMs);
        return new AssistCommand(
            AssistCommandKind.Attack,
            TargetEntityId: target.EntityId,
            SkillId: skillId,
            Reason: "attack");
    }

    private AssistCommand TryProtect(AssistWorldView world)
    {
        double hpPct = world.MaxHp <= 0 ? 100 : 100.0 * world.Hp / world.MaxHp;
        double mpPct = world.MaxMp <= 0 ? 100 : 100.0 * world.Mp / world.MaxMp;

        if (hpPct <= _cfg.EmergencyScrollHpPercent
            && !string.IsNullOrWhiteSpace(_cfg.EmergencyScrollItemId)
            && InventoryHas(world, _cfg.EmergencyScrollItemId))
        {
            return new AssistCommand(
                AssistCommandKind.UseItem,
                ItemId: _cfg.EmergencyScrollItemId,
                Reason: "emergency_scroll");
        }

        foreach (var pot in _cfg.HpPotions.OrderByDescending(p => p.Priority))
        {
            if (hpPct > pot.HpPercent) continue;
            if (world.NowUnixMs < _nextHpPotionUnixMs) break;
            if (!InventoryHas(world, pot.ItemId)) continue;
            _nextHpPotionUnixMs = world.NowUnixMs + Math.Max(0, pot.MinIntervalMs);
            return new AssistCommand(AssistCommandKind.UseItem, ItemId: pot.ItemId, Reason: "hp_potion");
        }

        foreach (var pot in _cfg.MpPotions.OrderByDescending(p => p.Priority))
        {
            double threshold = pot.MpPercent > 0 ? pot.MpPercent : pot.HpPercent;
            if (mpPct > threshold) continue;
            if (world.NowUnixMs < _nextMpPotionUnixMs) break;
            if (!InventoryHas(world, pot.ItemId)) continue;
            _nextMpPotionUnixMs = world.NowUnixMs + Math.Max(0, pot.MinIntervalMs);
            return new AssistCommand(AssistCommandKind.UseItem, ItemId: pot.ItemId, Reason: "mp_potion");
        }

        return default;
    }

    private AssistCommand TryPickup(AssistWorldView world)
    {
        if (world.NowUnixMs < _nextPickupUnixMs)
            return default;

        var allowed = BuildPickupAllowSet();
        var candidates = world.Loot
            .Where(l => Chebyshev(world.SelfX, world.SelfY, l.X, l.Y) <= Math.Max(0, _cfg.Pickup.PickupRangeCells))
            .Where(l => allowed.Count == 0 || allowed.Contains(l.ItemId))
            .OrderByDescending(l => PriorityOf(l.ItemId))
            .ThenBy(l => Chebyshev(world.SelfX, world.SelfY, l.X, l.Y))
            .ToList();
        if (candidates.Count == 0)
            return default;

        var loot = candidates[0];
        int dist = Chebyshev(world.SelfX, world.SelfY, loot.X, loot.Y);
        if (dist > 0)
        {
            if (_cfg.Pickup.PauseAttackWhilePicking)
            {
                var (nx, ny) = StepToward(world.SelfX, world.SelfY, loot.X, loot.Y);
                return new AssistCommand(AssistCommandKind.Move, ToX: nx, ToY: ny, Reason: "loot_approach");
            }
            return default;
        }

        _nextPickupUnixMs = world.NowUnixMs + Math.Max(0, _cfg.Pickup.DelayMs);
        return new AssistCommand(AssistCommandKind.Pickup, TargetEntityId: loot.EntityId, ItemId: loot.ItemId, Reason: "pickup");
    }

    private AssistEntity? SelectMonster(AssistWorldView world)
    {
        var ignore = new HashSet<string>(_cfg.IgnoreTags ?? new(), StringComparer.OrdinalIgnoreCase);
        var denyKinds = new HashSet<string>(_cfg.DoNotAttackEntityKinds ?? new(), StringComparer.OrdinalIgnoreCase);
        return world.Monsters
            .Where(m => m.Alive)
            .Where(m => !denyKinds.Contains(m.Kind))
            .Where(m => m.Tags.All(t => !ignore.Contains(t)))
            .Where(m => Chebyshev(world.SelfX, world.SelfY, m.X, m.Y) <= Math.Max(1, _cfg.SearchRangeCells))
            .OrderBy(m => Chebyshev(world.SelfX, world.SelfY, m.X, m.Y))
            .Take(Math.Max(1, _cfg.MaxPullCount))
            .FirstOrDefault();
    }

    private string SelectSkill(AssistWorldView world)
    {
        int mobCount = world.Monsters.Count(m =>
            m.Alive && Chebyshev(world.SelfX, world.SelfY, m.X, m.Y) <= Math.Max(1, _cfg.SearchRangeCells));
        double mpPct = world.MaxMp <= 0 ? 100 : 100.0 * world.Mp / world.MaxMp;
        double hpPct = world.MaxHp <= 0 ? 100 : 100.0 * world.Hp / world.MaxHp;

        foreach (var sk in _cfg.Skills.Where(s => s.Enabled).OrderByDescending(s => s.Priority))
        {
            if (world.LearnedSkills.Count > 0 && !world.LearnedSkills.Contains(sk.SkillId))
                continue;
            if (mpPct < sk.MinMpPercent)
                continue;
            if (sk.MinHpPercent is double needHp && hpPct > needHp)
                continue;
            if (sk.MinMobCount > 0 && mobCount < sk.MinMobCount)
                continue;
            if (!string.IsNullOrWhiteSpace(sk.RequireBuffMissing)
                && world.ActiveBuffIds.Contains(sk.RequireBuffMissing!))
                continue;
            return sk.SkillId;
        }
        return "basic_slash";
    }

    private AssistCommand IdleMove(AssistWorldView world)
    {
        if (string.Equals(_cfg.Mode, "patrol", StringComparison.OrdinalIgnoreCase)
            && _cfg.PatrolPoints is { Count: > 0 })
        {
            var pt = _cfg.PatrolPoints[_patrolIndex % _cfg.PatrolPoints.Count];
            if (world.SelfX == pt.X && world.SelfY == pt.Y)
            {
                _patrolIndex++;
                pt = _cfg.PatrolPoints[_patrolIndex % _cfg.PatrolPoints.Count];
            }
            var (nx, ny) = StepToward(world.SelfX, world.SelfY, pt.X, pt.Y);
            return new AssistCommand(AssistCommandKind.Move, ToX: nx, ToY: ny, Reason: "patrol");
        }

        int rad = Math.Max(0, _cfg.AnchorRadiusCells);
        if (Chebyshev(world.SelfX, world.SelfY, _anchorX, _anchorY) > rad)
        {
            var (nx, ny) = StepToward(world.SelfX, world.SelfY, _anchorX, _anchorY);
            return new AssistCommand(AssistCommandKind.Move, ToX: nx, ToY: ny, Reason: "anchor_return");
        }
        return default;
    }

    private HashSet<string> BuildPickupAllowSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profile = _filters.Profiles.FirstOrDefault(p => p.DefaultForAssist)
                      ?? _filters.Profiles.FirstOrDefault();
        if (profile is not null)
        {
            foreach (var r in profile.Rules ?? new())
            {
                if (r.AssistPickup)
                    set.Add(r.ItemId);
            }
        }
        foreach (var id in _cfg.Pickup.PriorityItemIds ?? new())
            set.Add(id);
        return set;
    }

    private int PriorityOf(string itemId)
    {
        int idx = (_cfg.Pickup.PriorityItemIds ?? new()).FindIndex(x =>
            string.Equals(x, itemId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            return 10_000 - idx;
        var profile = _filters.Profiles.FirstOrDefault(p => p.DefaultForAssist)
                      ?? _filters.Profiles.FirstOrDefault();
        var rule = profile?.Rules?.FirstOrDefault(r =>
            string.Equals(r.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
        return rule?.Priority ?? 0;
    }

    private static bool InventoryHas(AssistWorldView world, string itemId) =>
        world.InventoryCounts.TryGetValue(itemId, out int n) && n > 0;

    private static int Chebyshev(int x1, int y1, int x2, int y2) =>
        Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    private static (int X, int Y) StepToward(int fromX, int fromY, int toX, int toY)
    {
        int dx = Math.Sign(toX - fromX);
        int dy = Math.Sign(toY - fromY);
        return (fromX + dx, fromY + dy);
    }
}
