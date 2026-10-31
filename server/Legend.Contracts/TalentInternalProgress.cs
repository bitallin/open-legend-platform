using System.Text.Json;

namespace Legend.Contracts;

/// <summary>
/// Whitelist of statIds allowed on talent / meridian effects (docs/site/03).
/// Scripts must not invent attribute names outside this set.
/// </summary>
public static class StatIdWhitelist
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "atk", "def", "maxHp", "maxMp", "accuracy", "agility", "magicAtk", "magicDef",
        "critRate", "critDamage", "lifesteal", "moveSpeed"
    };

    public static bool IsAllowed(string? statId)
        => !string.IsNullOrWhiteSpace(statId) && Allowed.Contains(statId);

    public static IReadOnlyCollection<string> All => Allowed;
}

/// <summary>
/// Talent unlock / available-points helpers. No network handlers in E10 (MsgIds TBD).
/// </summary>
public static class TalentProgress
{
    public static int AvailablePoints(int characterLevel, int pointsPerLevel, int spentPoints)
    {
        int earned = Math.Max(0, characterLevel) * Math.Max(0, pointsPerLevel);
        return Math.Max(0, earned - Math.Max(0, spentPoints));
    }

    public static int CountSpentPoints(TalentTreeConfig tree, IReadOnlyCollection<string> unlockedNodeIds)
    {
        if (tree.Nodes.Count == 0 || unlockedNodeIds.Count == 0)
            return 0;
        var set = new HashSet<string>(unlockedNodeIds, StringComparer.OrdinalIgnoreCase);
        int sum = 0;
        foreach (var n in tree.Nodes)
        {
            if (set.Contains(n.NodeId))
                sum += Math.Max(0, n.CostPoints);
        }
        return sum;
    }

    public static int CountSpentPoints(TalentsConfigFile pack, IReadOnlyDictionary<string, List<string>> unlockedByTree)
    {
        int sum = 0;
        foreach (var tree in pack.Trees)
        {
            if (!unlockedByTree.TryGetValue(tree.TreeId, out var nodes) || nodes is null)
                continue;
            sum += CountSpentPoints(tree, nodes);
        }
        return sum;
    }

    /// <summary>
    /// Validates prerequisites and point cost. Does not mutate state.
    /// </summary>
    public static bool CanUnlock(
        TalentTreeConfig tree,
        string nodeId,
        IReadOnlyCollection<string> alreadyUnlocked,
        int availablePoints,
        out string reason)
    {
        reason = "";
        var node = tree.Nodes.FirstOrDefault(n =>
            string.Equals(n.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            reason = "Unknown talent node";
            return false;
        }
        var have = new HashSet<string>(alreadyUnlocked, StringComparer.OrdinalIgnoreCase);
        if (have.Contains(node.NodeId))
        {
            reason = "Already unlocked";
            return false;
        }
        foreach (var req in node.Requires ?? new List<string>())
        {
            if (!have.Contains(req))
            {
                reason = $"Missing prerequisite {req}";
                return false;
            }
        }
        if (availablePoints < Math.Max(0, node.CostPoints))
        {
            reason = "Not enough talent points";
            return false;
        }
        return true;
    }

    public static Dictionary<string, int> SumStatEffects(
        TalentsConfigFile pack,
        IReadOnlyDictionary<string, List<string>> unlockedByTree)
    {
        var sum = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tree in pack.Trees)
        {
            if (!unlockedByTree.TryGetValue(tree.TreeId, out var ids) || ids is null)
                continue;
            var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            foreach (var n in tree.Nodes)
            {
                if (!set.Contains(n.NodeId))
                    continue;
                foreach (var e in n.Effects ?? new List<TalentEffectConfig>())
                {
                    if (string.IsNullOrWhiteSpace(e.StatId) || !StatIdWhitelist.IsAllowed(e.StatId))
                        continue;
                    sum.TryGetValue(e.StatId, out int cur);
                    sum[e.StatId] = cur + e.Value;
                }
            }
        }
        return sum;
    }
}

/// <summary>Internal (neigong) level curve helpers. No spellcasting in E10.</summary>
public static class InternalProgress
{
    public static InternalLevelEntry? GetEntry(InternalConfigFile cfg, int level)
        => cfg.Entries.FirstOrDefault(e => e.Level == level);

    public static Dictionary<string, int> SumMeridianStats(
        InternalConfigFile cfg,
        IReadOnlyCollection<string> unlockedMeridians)
    {
        var set = new HashSet<string>(unlockedMeridians, StringComparer.OrdinalIgnoreCase);
        var sum = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in cfg.Meridians)
        {
            if (!set.Contains(m.MeridianId))
                continue;
            foreach (var e in m.Effects ?? new List<TalentEffectConfig>())
            {
                if (string.IsNullOrWhiteSpace(e.StatId) || !StatIdWhitelist.IsAllowed(e.StatId))
                    continue;
                sum.TryGetValue(e.StatId, out int cur);
                sum[e.StatId] = cur + e.Value;
            }
        }
        return sum;
    }

    public static bool CanUnlockMeridian(
        InternalConfigFile cfg,
        string meridianId,
        int internalLevel,
        IReadOnlyCollection<string> alreadyUnlocked,
        out MeridianConfigItem? meridian,
        out string reason)
    {
        meridian = cfg.Meridians.FirstOrDefault(m =>
            string.Equals(m.MeridianId, meridianId, StringComparison.OrdinalIgnoreCase));
        reason = "";
        if (meridian is null)
        {
            reason = "Unknown meridian";
            return false;
        }
        if (alreadyUnlocked.Contains(meridian.MeridianId, StringComparer.OrdinalIgnoreCase))
        {
            reason = "Already unlocked";
            return false;
        }
        if (internalLevel < meridian.RequiresInternalLevel)
        {
            reason = "Internal level too low";
            return false;
        }
        return true;
    }
}
