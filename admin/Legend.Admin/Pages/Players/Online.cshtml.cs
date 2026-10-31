using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Players;

public class OnlineModel(HostAdminClient host, AuditService audit) : PageModel
{
    public string RawJson { get; set; } = "";
    public string Status { get; set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var (ok, status, body) = await host.GetOnlineAsync(ct);
        Status = ok ? $"OK HTTP {status}" : $"失败 HTTP {status}";
        RawJson = body;
    }

    public async Task<IActionResult> OnPostKickAsync(string characterName, string reason, CancellationToken ct)
    {
        var (ok, status, body) = await host.PostJsonAsync("admin/ban", new { characterName, banned = true, reason, durationSeconds = 60 }, ct);
        await audit.WriteAsync(User.Identity?.Name ?? "?", "player.kickban", characterName, new { status, body, reason }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData[ok ? "Flash" : "Error"] = body;
        return RedirectToPage();
    }
}
