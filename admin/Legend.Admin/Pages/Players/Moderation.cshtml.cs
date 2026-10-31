using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Players;

public class ModerationModel(HostAdminClient host, AuditService audit) : PageModel
{
    public string LastResponse { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostBanAsync(string? username, string? characterName, int durationSeconds, string reason, bool banned, CancellationToken ct)
    {
        var payload = new { username, characterName, durationSeconds, reason, banned };
        var (ok, status, body) = await host.PostJsonAsync("admin/ban", payload, ct);
        await audit.WriteAsync(User.Identity?.Name ?? "?", "player.ban", username ?? characterName ?? "", new { status, body }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData[ok ? "Flash" : "Error"] = body;
        LastResponse = body;
        return Page();
    }

    public async Task<IActionResult> OnPostMuteAsync(string? username, string? characterName, int durationSeconds, string reason, bool muted, CancellationToken ct)
    {
        var payload = new { username, characterName, durationSeconds, reason, muted };
        var (ok, status, body) = await host.PostJsonAsync("admin/mute", payload, ct);
        await audit.WriteAsync(User.Identity?.Name ?? "?", "player.mute", username ?? characterName ?? "", new { status, body }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData[ok ? "Flash" : "Error"] = body;
        LastResponse = body;
        return Page();
    }
}
