using System.Text.Json;
using Legend.Admin.Data;
using Legend.Admin.Models;

namespace Legend.Admin.Services;

public sealed class AuditService(AdminDbContext db)
{
    public async Task WriteAsync(string actor, string action, string target, object? detail = null, string ip = "", CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Actor = actor,
            Action = action,
            Target = target,
            DetailJson = detail is null ? "{}" : JsonSerializer.Serialize(detail),
            Ip = ip,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
