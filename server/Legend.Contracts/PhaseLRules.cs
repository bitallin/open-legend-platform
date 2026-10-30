namespace Legend.Contracts;

/// <summary>Pure rules for Phase L shop sell, repair, and quest chains.</summary>
public static class PhaseLRules
{
    public const int StorageCapacity = 40;

    public static int SellUnitPrice(ItemTemplate? item, ShopConfig? shop)
    {
        if (item != null && item.SellPriceGold > 0)
            return item.SellPriceGold;
        if (item != null && shop != null)
        {
            var offer = shop.Offers.FirstOrDefault(o =>
                string.Equals(o.ItemId, item.ItemId, StringComparison.OrdinalIgnoreCase));
            if (offer != null && offer.PriceGold > 0)
                return Math.Max(1, offer.PriceGold / 2);
        }
        return 1;
    }

    public static int RepairCost(IEnumerable<EquippedItemState> gear, string? slot)
    {
        int cost = 0;
        bool any = false;
        foreach (var piece in gear)
        {
            if (!string.IsNullOrWhiteSpace(slot) &&
                !string.Equals(piece.Slot, slot, StringComparison.OrdinalIgnoreCase))
                continue;
            any = true;
            cost += 8 + Math.Max(0, piece.EnhanceLevel) * 6;
        }
        return any ? cost : 0;
    }

    public static bool PrerequisiteMet(QuestTemplate quest, IEnumerable<QuestProgressState> quests)
    {
        if (string.IsNullOrWhiteSpace(quest.PrerequisiteQuestId))
            return true;
        return quests.Any(s =>
            string.Equals(s.QuestId, quest.PrerequisiteQuestId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Status, "completed", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSupportSkill(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return false;
        return kind.Equals("heal", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("buff", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("summon", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("teleport", StringComparison.OrdinalIgnoreCase);
    }
}
