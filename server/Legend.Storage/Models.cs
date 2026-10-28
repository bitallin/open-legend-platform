using System.Text.Json;

namespace Legend.Storage;

public sealed class AccountRecord
{
    public string AccountId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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


public sealed class GuildRecord
{
    public string GuildId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LeaderCharacterId { get; set; } = string.Empty;
    public List<string> MemberCharacterIds { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public GuildRecord Clone() => new()
    {
        GuildId = GuildId,
        Name = Name,
        LeaderCharacterId = LeaderCharacterId,
        MemberCharacterIds = new List<string>(MemberCharacterIds),
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
