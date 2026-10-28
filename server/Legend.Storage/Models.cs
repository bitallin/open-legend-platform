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
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public CharacterRecord Clone() => new()
    {
        CharacterId = CharacterId,
        AccountId = AccountId,
        Name = Name,
        ClassId = ClassId,
        Level = Level,
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
