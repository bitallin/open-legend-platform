using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Realms;

[Authorize(Policy = "OperatorPlus")]
public class EditModel(RealmService realms, AuditService audit) : PageModel
{
    [BindProperty] public string RealmId { get; set; } = "realm-classic-03";
    [BindProperty] public new string Content { get; set; } = """
{
  "realmId": "realm-classic-03",
  "realmName": "新开区示例",
  "host": "127.0.0.1",
  "port": 7200,
  "adminPort": 7201,
  "sqlitePath": "data/realms/realm-classic-03.db",
  "manifestPath": "version-packs/sample-classic/manifest.json",
  "status": "OPEN",
  "versionPackId": "sample-classic-1.21.0"
}
""";

    public async Task OnGetAsync(string? file, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file)) return;
        var (ok, _, content) = await realms.ReadAsync(file, ct);
        if (ok && content is not null)
        {
            Content = content;
            RealmId = Path.GetFileNameWithoutExtension(file);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (ok, msg) = await realms.UpsertAsync(RealmId, Content, ct);
        if (!ok) { TempData["Error"] = msg; return Page(); }
        await audit.WriteAsync(User.Identity?.Name ?? "?", "realm.upsert", RealmId, ip: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "区服已保存: " + msg;
        return RedirectToPage("/Realms/Index");
    }
}
