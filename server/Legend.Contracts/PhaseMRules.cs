namespace Legend.Contracts;

/// <summary>Pure Phase M rules: PK colors, attack modes, shield, weight, durability.</summary>
public static class PhaseMRules
{
    public const int DurabilityMax = 100;
    public const int RedNameKills = 3;
    public const int WeightPerLevel = 5;
    public const int BaseCarryWeight = 80;

    public static string NormalizeMode(string? mode)
    {
        string m = (mode ?? "").Trim().ToLowerInvariant().Replace("-", "_");
        return m switch
        {
            "peace" or "和平" => "peace",
            "goodevil" or "good_evil" or "善恶" => "good_evil",
            "group" or "party" or "组队" => "group",
            "guild" or "行会" => "guild",
            "all" or "全体" => "all",
            _ => ""
        };
    }

    public static string NameColor(int pkKills)
    {
        if (pkKills >= RedNameKills)
            return "red";
        if (pkKills >= 1)
            return "yellow";
        return "white";
    }

    /// <summary>Safe zones are enforced by the caller. Force attack may hit players even in peace.</summary>
    public static bool AllowsPlayerTarget(string? mode, bool sameParty, bool sameGuild, int targetPkKills, bool force)
    {
        if (force)
            return true;
        return NormalizeMode(mode) switch
        {
            "good_evil" => targetPkKills >= RedNameKills,
            "group" => !sameParty,
            "guild" => !sameGuild,
            "all" => true,
            _ => false
        };
    }

    public static int AbsorbDamage(int raw, int pool)
    {
        if (raw <= 0 || pool <= 0)
            return 0;
        return Math.Min(raw, pool);
    }

    public static int MaxCarry(int level) => BaseCarryWeight + Math.Max(1, level) * WeightPerLevel;

    public static int StackWeight(ItemTemplate? item, int count)
    {
        if (item == null || count <= 0 || item.Weight <= 0)
            return 0;
        return item.Weight * count;
    }

    public static int BagWeight(IEnumerable<InventorySlot> bag, IReadOnlyDictionary<string, ItemTemplate> items)
    {
        int total = 0;
        foreach (var slot in bag)
        {
            items.TryGetValue(slot.ItemId, out var item);
            total += StackWeight(item, slot.Count);
        }
        return total;
    }

    public static (int Atk, int Def, int Hp) ApplyBroken(int atk, int def, int hp, int durability, int maxDurability)
    {
        if (maxDurability > 0 && durability <= 0)
            return (atk / 2, def / 2, hp / 2);
        return (atk, def, hp);
    }
}
