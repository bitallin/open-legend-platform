using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages;

public class IndexModel(VersionPackService packs, RealmService realms, AdminDbContext db, HostAdminClient host) : PageModel
{
    public int PackCount { get; set; }
    public int RealmCount { get; set; }
    public int ConfigFileCount { get; set; }
    public int AuditCount { get; set; }
    public int MailQueued { get; set; }
    public string OnlineHint { get; set; } = "—";
    public string HostBase { get; set; } = "";
    public bool HasToken { get; set; }
    public string HostStatus { get; set; } = "";
    public List<AuditLog> RecentAudits { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var list = packs.ListPacks();
        PackCount = list.Count;
        RealmCount = realms.ListRealms().Count;
        ConfigFileCount = list.Sum(p => packs.ListConfigFiles(p.Id).Count);
        AuditCount = await db.AuditLogs.CountAsync(ct);
        MailQueued = await db.MailJobs.CountAsync(m => m.Status == "Queued", ct);
        RecentAudits = await db.AuditLogs.OrderByDescending(a => a.Id).Take(15).ToListAsync(ct);
        HostBase = host.BaseUrl;
        HasToken = host.HasToken;
        var (ok, status, body) = await host.GetOnlineAsync(ct);
        if (ok)
        {
            HostStatus = $"连通 HTTP {status}";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("online", out var o))
                    OnlineHint = o.GetRawText();
                else OnlineHint = "ok";
            }
            catch { OnlineHint = "ok"; }
        }
        else
        {
            HostStatus = status == 0 ? "未连接（可先配配置再连 Host）" : $"HTTP {status}";
            OnlineHint = "离线";
        }
    }
}
