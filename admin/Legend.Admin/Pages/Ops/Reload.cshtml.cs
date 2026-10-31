using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Ops;

[Authorize(Policy = "OperatorPlus")]
public class ReloadModel(HostAdminClient host, AuditService audit) : PageModel
{
    [BindProperty] public string ManifestPath { get; set; } = "version-packs/sample-classic/manifest.json";
    public new string Response { get; set; } = "";

    public void OnGet() { }

    public async Task OnPostAsync(CancellationToken ct)
    {
        var (ok, status, body) = await host.PostJsonAsync("admin/reload-pack", new { manifestPath = ManifestPath }, ct);
        Response = body;
        await audit.WriteAsync(User.Identity?.Name ?? "?", "ops.reload", ManifestPath, new { status, body }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData[ok ? "Flash" : "Error"] = ok ? "热重载请求已发送" : body;
    }
}
