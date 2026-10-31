using System.Text.Json;

namespace Legend.Contracts;

/// <summary>
/// Multi-currency ledger helpers (docs/site/11). Gold stays mirrored on CharacterRecord.Gold / protocol fields.
/// Bound currencies prefer the same deductGroup before unbound siblings.
/// </summary>
public static class CurrencyWallet
{
    public const string GoldId = "gold";

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static Dictionary<string, long> Parse(string? json)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out long v))
                    map[prop.Name] = Math.Max(0, v);
            }
        }
        catch
        {
            /* empty */
        }
        return map;
    }

    public static string Serialize(IReadOnlyDictionary<string, long> balances)
    {
        var obj = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in balances)
        {
            if (string.IsNullOrWhiteSpace(k) || v <= 0)
                continue;
            obj[k] = v;
        }
        return JsonSerializer.Serialize(obj, Opts);
    }

    public static void EnsureGoldMirror(Dictionary<string, long> wallet, int gold)
    {
        wallet[GoldId] = Math.Max(0, (long)gold);
    }

    public static int GetGold(IReadOnlyDictionary<string, long> wallet)
    {
        if (wallet.TryGetValue(GoldId, out long g))
            return (int)Math.Clamp(g, 0, int.MaxValue);
        return 0;
    }

    public static long GetBalance(IReadOnlyDictionary<string, long> wallet, string currencyId)
    {
        if (string.IsNullOrWhiteSpace(currencyId))
            return 0;
        return wallet.TryGetValue(currencyId, out long v) ? Math.Max(0, v) : 0;
    }

    public static void Credit(Dictionary<string, long> wallet, string currencyId, long amount)
    {
        if (string.IsNullOrWhiteSpace(currencyId) || amount <= 0)
            return;
        long cur = GetBalance(wallet, currencyId);
        long next = cur > long.MaxValue - amount ? long.MaxValue : cur + amount;
        wallet[currencyId] = next;
    }

    /// <summary>
    /// Debit <paramref name="amount"/> of <paramref name="currencyId"/>, preferring same deductGroup bound first.
    /// Returns false if insufficient across the group (or the single currency when no pack is provided).
    /// </summary>
    public static bool TryDebit(
        Dictionary<string, long> wallet,
        string currencyId,
        long amount,
        IReadOnlyDictionary<string, CurrencyConfigItem>? currencies = null)
    {
        if (amount <= 0)
            return true;
        if (string.IsNullOrWhiteSpace(currencyId))
            return false;

        if (currencies is null || currencies.Count == 0 ||
            !currencies.TryGetValue(currencyId, out var primary))
        {
            long have = GetBalance(wallet, currencyId);
            if (have < amount)
                return false;
            wallet[currencyId] = have - amount;
            return true;
        }

        string group = string.IsNullOrWhiteSpace(primary.DeductGroup) ? primary.CurrencyId : primary.DeductGroup;
        var ordered = currencies.Values
            .Where(c => string.Equals(
                string.IsNullOrWhiteSpace(c.DeductGroup) ? c.CurrencyId : c.DeductGroup,
                group,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CurrencyId.EndsWith("_bound", StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.CurrencyId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Prefer exact id first among equals; ensure primary unbound still participates.
        if (!ordered.Any(c => string.Equals(c.CurrencyId, currencyId, StringComparison.OrdinalIgnoreCase)))
            ordered.Insert(0, primary);

        long need = amount;
        long available = 0;
        foreach (var c in ordered)
            available += GetBalance(wallet, c.CurrencyId);
        if (available < need)
            return false;

        foreach (var c in ordered)
        {
            if (need <= 0)
                break;
            long have = GetBalance(wallet, c.CurrencyId);
            if (have <= 0)
                continue;
            long take = Math.Min(have, need);
            wallet[c.CurrencyId] = have - take;
            need -= take;
        }
        return need <= 0;
    }

    public static bool IsEnabled(IReadOnlyDictionary<string, CurrencyConfigItem> currencies, string currencyId)
    {
        if (string.IsNullOrWhiteSpace(currencyId))
            return false;
        if (currencies.Count == 0)
            return string.Equals(currencyId, GoldId, StringComparison.OrdinalIgnoreCase);
        return currencies.ContainsKey(currencyId);
    }
}

/// <summary>Per-character recycle daily counters (Asia/Shanghai day, same as dungeon progress).</summary>
public static class RecycleDailyJson
{
    public sealed class Entry
    {
        public int UsedToday { get; set; }
        public string DayKey { get; set; } = "";
    }

    public static Dictionary<string, Entry> Parse(string? json)
    {
        var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var e = new Entry();
                if (prop.Value.TryGetProperty("usedToday", out var u))
                    e.UsedToday = u.GetInt32();
                if (prop.Value.TryGetProperty("dayKey", out var d))
                    e.DayKey = d.GetString() ?? "";
                map[prop.Name] = e;
            }
        }
        catch
        {
            /* empty */
        }
        return map;
    }

    public static string Serialize(IReadOnlyDictionary<string, Entry> map)
    {
        var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in map)
        {
            obj[k] = new Dictionary<string, object>
            {
                ["usedToday"] = v.UsedToday,
                ["dayKey"] = v.DayKey ?? ""
            };
        }
        return JsonSerializer.Serialize(obj);
    }

    public static Entry GetOrCreate(Dictionary<string, Entry> map, string itemId, DateTimeOffset utcNow)
    {
        string dayKey = Legend.Storage.DungeonProgressJson.ComputeDayKey(utcNow, resetHourLocal: 0);
        if (!map.TryGetValue(itemId, out var e) || e is null)
        {
            e = new Entry { DayKey = dayKey, UsedToday = 0 };
            map[itemId] = e;
            return e;
        }
        if (!string.Equals(e.DayKey, dayKey, StringComparison.Ordinal))
        {
            e.DayKey = dayKey;
            e.UsedToday = 0;
        }
        return e;
    }
}
