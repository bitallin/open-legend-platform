namespace Legend.Admin.Models;

public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Operator = "Operator";
    public const string Support = "Support";
    public static readonly string[] All = [SuperAdmin, Operator, Support];
}

public sealed class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = AdminRoles.Support;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string DetailJson { get; set; } = "{}";
    public string Ip { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ConfigRevision
{
    public long Id { get; set; }
    public string PackId { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string Note { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MailJob
{
    public long Id { get; set; }
    public string Scope { get; set; } = "All"; // All | Account | Character
    public string Target { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string AttachmentsJson { get; set; } = "[]";
    public string Status { get; set; } = "Queued"; // Queued | Sent | Failed
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CdkBatch
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string RewardsJson { get; set; } = "[]";
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CdkCode> Codes { get; set; } = new();
}

public sealed class CdkCode
{
    public long Id { get; set; }
    public int BatchId { get; set; }
    public CdkBatch? Batch { get; set; }
    public string Code { get; set; } = "";
    public bool Redeemed { get; set; }
    public bool Voided { get; set; }
    public string? RedeemedBy { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }
}

public sealed class MergePlan
{
    public int Id { get; set; }
    public string SourceRealmIdsJson { get; set; } = "[]";
    public string TargetRealmId { get; set; } = "";
    public DateTimeOffset? ScheduleAt { get; set; }
    public string Status { get; set; } = "Draft"; // Draft | Scheduled | Running | Done | Cancelled
    public string Notes { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CurrencyAdjustment
{
    public long Id { get; set; }
    public string AccountOrCharacter { get; set; } = "";
    public string CurrencyId { get; set; } = "";
    public long Delta { get; set; }
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssetManifestRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "ui"; // ui | sfx | vfx | map_tile | other
    public string VersionTag { get; set; } = "1.0.0";
    public string EntriesJson { get; set; } = "[]";
    public string Notes { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RechargeOrderNote
{
    public long Id { get; set; }
    public string OrderId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public long AmountCents { get; set; }
    public string CurrencyId { get; set; } = "yuanbao";
    public long GrantAmount { get; set; }
    public string Status { get; set; } = "Manual"; // Manual | Sandbox | Paid | Failed
    public string Provider { get; set; } = "manual";
    public string Actor { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
