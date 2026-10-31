using System.Security.Claims;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Configs;

[Authorize(Policy = "OperatorPlus")]
public class EditModel(VersionPackService packs, AuditService audit) : PageModel
{
    [BindProperty] public string PackId { get; set; } = "";
    [BindProperty] public string RelativePath { get; set; } = "";
    [BindProperty] public new string Content { get; set; } = "";
    [BindProperty] public string Note { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(string packId, string path, CancellationToken ct)
    {
        PackId = packId; RelativePath = path;
        var (ok, msg, content) = await packs.ReadAsync(packId, path, ct);
        if (!ok) { TempData["Error"] = msg; return RedirectToPage("/Configs/Index", new { packId }); }
        Content = content ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? "?";
        var (ok, msg, hash) = await packs.WriteAsync(PackId, RelativePath, Content, actor, Note, ct);
        if (!ok) { TempData["Error"] = msg; return Page(); }
        await audit.WriteAsync(actor, "config.save", $"{PackId}/{RelativePath}", new { hash, Note }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = $"已保存，hash={hash}";
        return RedirectToPage(new { packId = PackId, path = RelativePath });
    }
}
