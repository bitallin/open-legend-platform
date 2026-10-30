namespace Legend.Contracts;

/// <summary>
/// Phase P rules shared by the server and the playable client.
/// Profanity words also live in version-packs/sample-classic/configs/profanity.txt.
/// </summary>
public static class PhasePRules
{
    public const int ApprenticeBonusPercent = 10;
    public const int MentorSharePercent = 20;

    private static readonly string[] FallbackWords =
    {
        "傻逼", "傻b", "他妈", "妈的", "操你", "滚蛋", "废物", "狗屎", "婊子", "去死", "测试脏话"
    };

    private static string[] _words = FallbackWords;

    public static IReadOnlyList<string> Words => _words;

    public static void UseWordList(IEnumerable<string> words)
    {
        var list = words
            .Select(w => (w ?? "").Trim())
            .Where(w => w.Length > 0 && !w.StartsWith("#"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length > 0)
            _words = list;
    }

    public static void EnsureLoaded(Stream? stream)
    {
        if (stream == null)
            return;
        using var reader = new StreamReader(stream);
        UseWordList(reader.ReadToEnd().Split('\n', '\r'));
    }

    public static string MaskProfanity(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? "";
        string result = content;
        foreach (var word in _words.OrderByDescending(w => w.Length))
        {
            if (word.Length == 0)
                continue;
            int idx = 0;
            while (true)
            {
                int found = result.IndexOf(word, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    break;
                result = string.Concat(result.AsSpan(0, found), "**", result.AsSpan(found + word.Length));
                idx = found + 2;
            }
        }
        return result;
    }

    public static int ApprenticeBonus(int baseExp)
        => baseExp <= 0 ? 0 : Math.Max(1, baseExp * ApprenticeBonusPercent / 100);

    public static int MentorShare(int baseExp)
        => baseExp <= 0 ? 0 : Math.Max(1, baseExp * MentorSharePercent / 100);

    /// <summary>Guild office wins over the level title. Always returns a short title.</summary>
    public static string Title(int level, string? guildRank)
    {
        string rank = (guildRank ?? "").Trim().ToLowerInvariant();
        if (rank is "leader" or "会长")
            return "会长";
        if (rank is "vice_leader" or "副会长" or "副会")
            return "副会长";
        if (rank is "warehouse_admin" or "仓管")
            return "仓管";
        if (level >= 35)
            return "勇者";
        if (level >= 15)
            return "新兵";
        return "旅人";
    }
}
