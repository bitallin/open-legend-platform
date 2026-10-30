namespace Legend.Contracts;

public static class PhaseNRules
{
    public static int DirDelta(int from, int to)
    {
        int d = Math.Abs(((to % 8) + 8) % 8 - ((from % 8) + 8) % 8);
        return Math.Min(d, 8 - d);
    }

    public static int DirToward(int fromX, int fromY, int toX, int toY)
    {
        int dx = Math.Sign(toX - fromX);
        int dy = Math.Sign(toY - fromY);
        if (dx == 0 && dy > 0) return 0;
        if (dx > 0 && dy > 0) return 1;
        if (dx > 0 && dy == 0) return 2;
        if (dx > 0 && dy < 0) return 3;
        if (dx == 0 && dy < 0) return 4;
        if (dx < 0 && dy < 0) return 5;
        if (dx < 0 && dy == 0) return 6;
        if (dx < 0 && dy > 0) return 7;
        return fromX == toX && fromY == toY ? 0 : 4;
    }

    /// <summary>Front half: facing and two steps either side. The cell directly behind is excluded.</summary>
    public static bool InFacingArc(int fromX, int fromY, int facing, int toX, int toY)
    {
        if (fromX == toX && fromY == toY)
            return false;
        int toward = DirToward(fromX, fromY, toX, toY);
        return DirDelta(facing, toward) <= 2;
    }

    public static bool InHourWindow(int hour, int openHour, int closeHour)
    {
        if (openHour < 0 || closeHour < 0)
            return false;
        hour = ((hour % 24) + 24) % 24;
        openHour = ((openHour % 24) + 24) % 24;
        closeHour = ((closeHour % 24) + 24) % 24;
        if (openHour == closeHour)
            return false;
        if (openHour < closeHour)
            return hour >= openHour && hour < closeHour;
        return hour >= openHour || hour < closeHour;
    }

    public static int DeathGoldLoss(int gold, int percent)
    {
        if (gold <= 0 || percent <= 0)
            return 0;
        return Math.Max(1, gold * Math.Min(100, percent) / 100);
    }

    public static bool CanRepeatQuest(string? repeatable, string? completedOn, string today)
    {
        if (string.IsNullOrWhiteSpace(repeatable))
            return false;
        if (string.Equals(repeatable, "ring", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(repeatable, "daily", StringComparison.OrdinalIgnoreCase))
            return !string.Equals(completedOn ?? "", today, StringComparison.Ordinal);
        return false;
    }

    public static bool WorldLinked(string fromMap, string toMap)
    {
        if (string.Equals(fromMap, toMap, StringComparison.OrdinalIgnoreCase))
            return false;
        var links = new (string A, string B)[]
        {
            ("novice_plain", "oath_city"),
            ("novice_plain", "marsh_den"),
            ("novice_plain", "dark_forest"),
            ("marsh_den", "bone_path"),
            ("bone_path", "sealed_temple"),
            ("oath_city", "sand_gate"),
            ("sand_gate", "dark_forest")
        };
        foreach (var (a, b) in links)
        {
            if ((string.Equals(a, fromMap, StringComparison.OrdinalIgnoreCase) && string.Equals(b, toMap, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(b, fromMap, StringComparison.OrdinalIgnoreCase) && string.Equals(a, toMap, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
