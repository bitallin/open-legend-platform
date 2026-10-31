using System.Security.Claims;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Realms;

[Authorize]
public class IndexModel(RealmService realms, AuditService audit) : PageModel
{
    public List<RealmInfo> Realms { get; set; } = new();
    public void OnGet() => Realms = realms.ListRealms().ToList();
    public async Task<IActionResult> OnPostStatusAsync(string fileName, string status, CancellationToken ct)
    {
        var (ok, msg) = await realms.SetStatusAsync(fileName, status, ct);
        if (!ok) TempData["Error"] = msg;
        else
        {
            await audit.WriteAsync(User.Identity?.Name ?? "?", "realm.status", fileName, new { status }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
            TempData["Flash"] = $"已设置 {fileName} => {status}";
        }
        return RedirectToPage();
    }
}
