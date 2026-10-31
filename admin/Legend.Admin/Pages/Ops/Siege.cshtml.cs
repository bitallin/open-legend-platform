using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Ops;

[Authorize(Policy = "OperatorPlus")]
public class SiegeModel(HostAdminClient host, AuditService audit) : PageModel
{
    public new string Response { get; set; } = "";
    public void OnGet() { }

    public async Task OnPostAsync(string action, CancellationToken ct)
    {
        var (ok, status, body) = await host.PostJsonAsync("admin/siege", new { action }, ct);
        Response = body;
        await audit.WriteAsync(User.Identity?.Name ?? "?", "ops.siege", action, new { status, body }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData[ok ? "Flash" : "Error"] = body;
    }
}
