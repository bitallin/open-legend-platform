using System.Linq;
using System.Text.Json;
using Legend.Contracts;

namespace Legend.Storage;

public sealed class AccountRecord
{
    public string AccountId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ModerationRecord
{
    public string AccountId { get; set; } = string.Empty;
    public bool IsBanned { get; set; }
    public string BanReason { get; set; } = string.Empty;
    /// <summary>0 = permanent when banned.</summary>
    public long BanExpiresUnixMs { get; set; }
    public bool IsMuted { get; set; }
    public string MuteReason { get; set; } = string.Empty;
    /// <summary>0 = permanent when muted.</summary>
    public long MuteExpiresUnixMs { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsBanActive(long nowUnixMs = 0)
    {
        if (!IsBanned) return false;
        if (BanExpiresUnixMs <= 0) return true;
        if (nowUnixMs <= 0) nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return nowUnixMs < BanExpiresUnixMs;
    }

    public bool IsMuteActive(long nowUnixMs = 0)
    {
        if (!IsMuted) return false;
        if (MuteExpiresUnixMs <= 0) return true;
        if (nowUnixMs <= 0) nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return nowUnixMs < MuteExpiresUnixMs;
    }
}

public sealed class CharacterRecord
{
    public string CharacterId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public string MapId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public int MaxHp { get; set; }
    public int MaxMp { get; set; }
    public string InventoryJson { get; set; } = "[]";
    public int Gold { get; set; } = 100;
    public string QuestJson { get; set; } = "[]";
    public string EquipmentJson { get; set; } = "[]";
    public string GuildId { get; set; } = string.Empty;
    public string SkillsJson { get; set; } = "[]";
    public string DungeonProgressJson { get; set; } = "{}";
    public string AppearanceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public CharacterRecord Clone() => new()
    {
        CharacterId = CharacterId,
        AccountId = AccountId,
        Name = Name,
        ClassId = ClassId,
        Level = Level,
        Exp = Exp,
        MapId = MapId,
        X = X,
        Y = Y,
        Hp = Hp,
        Mp = Mp,
        MaxHp = MaxHp,
        MaxMp = MaxMp,
        InventoryJson = InventoryJson,
        Gold = Gold,
        QuestJson = QuestJson,
        EquipmentJson = EquipmentJson,
        GuildId = GuildId,
        SkillsJson = SkillsJson,
        DungeonProgressJson = DungeonProgressJson,
        AppearanceJson = AppearanceJson,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

public sealed class SessionRecord
{
    public string Token { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? CharacterId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public static class InventoryJson
{
    public static string Empty => "[]";
    public static bool IsValid(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { using var _ = JsonDocument.Parse(json); return true; }
        catch { return false; }
    }
}

public static class QuestJson
{
    public static string Empty => "[]";
    public const int StartingGold = 100;
}

public static class EquipmentJson
{
    public const string Empty = "[]";
}

public static class AppearanceJson
{
    public const string Empty = "{}";

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public sealed class State
    {
        public List<string> OwnedMounts { get; set; } = new();
        public string ActiveMountId { get; set; } = "";
        public List<string> OwnedFashions { get; set; } = new();
        public string EquippedFashionId { get; set; } = "";
        public List<string> OwnedPets { get; set; } = new();
        public string ActivePetId { get; set; } = "";
        public List<InventorySlot> Storage { get; set; } = new();
        public int PkKills { get; set; }
        public string AttackMode { get; set; } = "peace";
    }

    public static State Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new State();
        try
        {
            var s = JsonSerializer.Deserialize<State>(json, Opts) ?? new State();
            s.OwnedMounts ??= new();
            s.OwnedFashions ??= new();
            s.OwnedPets ??= new();
            s.ActiveMountId ??= "";
            s.EquippedFashionId ??= "";
            s.ActivePetId ??= "";
            s.Storage ??= new();
            if (string.IsNullOrWhiteSpace(s.AttackMode))
                s.AttackMode = "peace";
            return s;
        }
        catch
        {
            return new State();
        }
    }

    public static string Serialize(State state)
    {
        state ??= new State();
        // Deduplicate owned lists (case-insensitive)
        var mounts = state.OwnedMounts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var fashions = state.OwnedFashions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pets = (state.OwnedPets ?? new())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var payload = new State
        {
            OwnedMounts = mounts,
            ActiveMountId = state.ActiveMountId ?? "",
            OwnedFashions = fashions,
            EquippedFashionId = state.EquippedFashionId ?? "",
            OwnedPets = pets,
            ActivePetId = state.ActivePetId ?? "",
            Storage = (state.Storage ?? new())
                .Where(s => !string.IsNullOrWhiteSpace(s.ItemId) && s.Count > 0)
                .Select(s => new InventorySlot { ItemId = s.ItemId, Count = s.Count, Identified = s.Identified })
                .ToList(),
            PkKills = Math.Max(0, state.PkKills),
            AttackMode = string.IsNullOrWhiteSpace(state.AttackMode) ? "peace" : state.AttackMode
        };
        return JsonSerializer.Serialize(payload, Opts);
    }
}


public sealed class GuildRecord
{
    public string GuildId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LeaderCharacterId { get; set; } = string.Empty;
    public List<string> MemberCharacterIds { get; set; } = new();
    /// <summary>JSON array of {itemId,count} shared warehouse stacks.</summary>
    public string WarehouseJson { get; set; } = "[]";
    /// <summary>JSON object map characterId -> rank (vice_leader|warehouse_admin). Leader is always LeaderCharacterId; absent = member.</summary>
    public string RanksJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public GuildRecord Clone() => new()
    {
        GuildId = GuildId,
        Name = Name,
        LeaderCharacterId = LeaderCharacterId,
        MemberCharacterIds = new List<string>(MemberCharacterIds),
        WarehouseJson = WarehouseJson,
        RanksJson = RanksJson,
        CreatedAt = CreatedAt
    };
}

public sealed class GuildWarehouseAuditRecord
{
    public string AuditId { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string ActorCharacterId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // deposit | withdraw | clear
    public string ItemId { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public GuildWarehouseAuditRecord Clone() => new()
    {
        AuditId = AuditId,
        GuildId = GuildId,
        ActorCharacterId = ActorCharacterId,
        ActorName = ActorName,
        Action = Action,
        ItemId = ItemId,
        Count = Count,
        CreatedAt = CreatedAt
    };
}

public sealed class TradeListingRecord
{
    public string ListingId { get; set; } = string.Empty;
    public string SellerCharacterId { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public int Count { get; set; }
    public int UnitPriceGold { get; set; }
    public int TotalPriceGold { get; set; }
    /// <summary>active | sold | cancelled</summary>
    public string Status { get; set; } = "active";
    public string BuyerCharacterId { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TradeListingRecord Clone() => new()
    {
        ListingId = ListingId,
        SellerCharacterId = SellerCharacterId,
        SellerName = SellerName,
        ItemId = ItemId,
        Count = Count,
        UnitPriceGold = UnitPriceGold,
        TotalPriceGold = TotalPriceGold,
        Status = Status,
        BuyerCharacterId = BuyerCharacterId,
        BuyerName = BuyerName,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

public sealed class FriendRecord
{
    public string OwnerCharacterId { get; set; } = string.Empty;
    public string FriendCharacterId { get; set; } = string.Empty;
    public string FriendName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public FriendRecord Clone() => new()
    {
        OwnerCharacterId = OwnerCharacterId,
        FriendCharacterId = FriendCharacterId,
        FriendName = FriendName,
        CreatedAt = CreatedAt
    };
}

public sealed class SiegeDeclaredGuildEntry
{
    public string GuildId { get; set; } = string.Empty;
    public string GuildName { get; set; } = string.Empty;
}

public sealed class SiegeStateRecord
{
    public string CastleId { get; set; } = "sabuk_castle";
    public string OwnerGuildId { get; set; } = string.Empty;
    public string OwnerGuildName { get; set; } = string.Empty;
    public string DeclaredGuildsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SiegeStateRecord Clone() => new()
    {
        CastleId = CastleId,
        OwnerGuildId = OwnerGuildId,
        OwnerGuildName = OwnerGuildName,
        DeclaredGuildsJson = DeclaredGuildsJson,
        UpdatedAt = UpdatedAt
    };
}

public sealed class MailAttachmentItemRecord
{
    public string ItemId { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class MailRecord
{
    public string MailId { get; set; } = string.Empty;
    public string OwnerCharacterId { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int AttachGold { get; set; }
    public string AttachItemsJson { get; set; } = "[]";
    public bool IsRead { get; set; }
    public bool Claimed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool HasUnclaimedAttachment =>
        !Claimed && (AttachGold > 0 || (!string.IsNullOrWhiteSpace(AttachItemsJson) && AttachItemsJson != "[]"));

    public MailRecord Clone() => new()
    {
        MailId = MailId,
        OwnerCharacterId = OwnerCharacterId,
        FromName = FromName,
        Subject = Subject,
        Body = Body,
        AttachGold = AttachGold,
        AttachItemsJson = AttachItemsJson,
        IsRead = IsRead,
        Claimed = Claimed,
        CreatedAt = CreatedAt
    };
}

public static class MailItemsJson
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(IEnumerable<MailAttachmentItemRecord> items)
        => JsonSerializer.Serialize(items?.ToList() ?? new List<MailAttachmentItemRecord>(), Opts);

    public static List<MailAttachmentItemRecord> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<MailAttachmentItemRecord>();
        try
        {
            return JsonSerializer.Deserialize<List<MailAttachmentItemRecord>>(json, Opts)
                   ?? new List<MailAttachmentItemRecord>();
        }
        catch
        {
            return new List<MailAttachmentItemRecord>();
        }
    }
}

public sealed class DungeonProgressEntry
{
    public int UsedToday { get; set; }
    public long LastEnterUnix { get; set; }
    public string DayKey { get; set; } = "";
}

/// <summary>
/// Per-character dungeon daily/CD progress.
/// Day boundary: Asia/Shanghai local clock, reset at ResetHourLocal (default 0 = midnight).
/// dayKey format: yyyy-MM-dd of the active game-day in Shanghai.
/// </summary>
public static class DungeonProgressJson
{
    public static readonly TimeZoneInfo ShanghaiTz = ResolveShanghai();

    private static TimeZoneInfo ResolveShanghai()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
            catch { return TimeZoneInfo.CreateCustomTimeZone("Asia/Shanghai", TimeSpan.FromHours(8), "Asia/Shanghai", "Asia/Shanghai"); }
        }
    }

    public static string ComputeDayKey(DateTimeOffset utcNow, int resetHourLocal)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, ShanghaiTz);
        int hour = Math.Clamp(resetHourLocal, 0, 23);
        var day = local.DateTime;
        if (day.Hour < hour)
            day = day.AddDays(-1);
        return day.ToString("yyyy-MM-dd");
    }

    public static Dictionary<string, DungeonProgressEntry> Parse(string? json)
    {
        var map = new Dictionary<string, DungeonProgressEntry>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var e = new DungeonProgressEntry();
                if (prop.Value.TryGetProperty("usedToday", out var u)) e.UsedToday = u.GetInt32();
                if (prop.Value.TryGetProperty("lastEnterUnix", out var l)) e.LastEnterUnix = l.GetInt64();
                if (prop.Value.TryGetProperty("dayKey", out var d)) e.DayKey = d.GetString() ?? "";
                map[prop.Name] = e;
            }
        }
        catch { /* treat as empty */ }
        return map;
    }

    public static string Serialize(Dictionary<string, DungeonProgressEntry> map)
    {
        var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in map)
        {
            obj[k] = new Dictionary<string, object>
            {
                ["usedToday"] = v.UsedToday,
                ["lastEnterUnix"] = v.LastEnterUnix,
                ["dayKey"] = v.DayKey ?? ""
            };
        }
        return JsonSerializer.Serialize(obj);
    }

    public static DungeonProgressEntry GetOrCreate(
        Dictionary<string, DungeonProgressEntry> map, string dungeonId, int resetHourLocal, DateTimeOffset utcNow)
    {
        string dayKey = ComputeDayKey(utcNow, resetHourLocal);
        if (!map.TryGetValue(dungeonId, out var e) || e is null)
        {
            e = new DungeonProgressEntry { DayKey = dayKey };
            map[dungeonId] = e;
        }
        if (!string.Equals(e.DayKey, dayKey, StringComparison.Ordinal))
        {
            e.UsedToday = 0;
            e.DayKey = dayKey;
        }
        return e;
    }
}


public sealed class PaymentOrderRecord
{
    public string OrderId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public int RewardGold { get; set; }
    public string RewardItemId { get; set; } = string.Empty;
    public int RewardItemCount { get; set; }
    /// <summary>Pending | Paid | Fulfilled | Failed</summary>
    public string Status { get; set; } = "Pending";
    public string Provider { get; set; } = "mock";
    public string ProviderTxnId { get; set; } = string.Empty;
    public string MockPayToken { get; set; } = string.Empty;
    public string FulfilledMailId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PaymentOrderRecord Clone() => new()
    {
        OrderId = OrderId,
        AccountId = AccountId,
        CharacterId = CharacterId,
        ProductId = ProductId,
        PriceCents = PriceCents,
        RewardGold = RewardGold,
        RewardItemId = RewardItemId,
        RewardItemCount = RewardItemCount,
        Status = Status,
        Provider = Provider,
        ProviderTxnId = ProviderTxnId,
        MockPayToken = MockPayToken,
        FulfilledMailId = FulfilledMailId,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}
