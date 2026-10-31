using Legend.Admin.Data;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "OperatorPlus")]
public class EditModel(AdminDbContext db, AuditService audit) : PageModel
{
    [BindProperty] public int Id { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Kind { get; set; } = "";
    [BindProperty] public string VersionTag { get; set; } = "";
    [BindProperty] public string EntriesJson { get; set; } = "";
    [BindProperty] public string Notes { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var row = await db.AssetManifests.FindAsync([id], ct);
        if (row is null) return RedirectToPage("/Assets/Index");
        Id = row.Id; Name = row.Name; Kind = row.Kind; VersionTag = row.VersionTag; EntriesJson = row.EntriesJson; Notes = row.Notes;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var row = await db.AssetManifests.FindAsync([Id], ct);
        if (row is null) return RedirectToPage("/Assets/Index");
        row.Name = Name; row.Kind = Kind; row.VersionTag = VersionTag; row.EntriesJson = EntriesJson; row.Notes = Notes;
        row.Actor = User.Identity?.Name ?? "?"; row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(row.Actor, "asset.update", row.Name, row, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "已更新";
        return RedirectToPage("/Assets/Index");
    }
}
