using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "OperatorPlus")]
public class CatalogModel(AdminDbContext db, AuditService audit) : PageModel
{
    public List<AssetManifestRecord> Items { get; set; } = new();
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Kind { get; set; } = "ui";
    [BindProperty] public string VersionTag { get; set; } = "1.0.0";
    [BindProperty] public string EntriesJson { get; set; } = "[]";
    [BindProperty] public string Notes { get; set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
        => Items = await db.AssetManifests.OrderByDescending(a => a.Id).ToListAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var row = new AssetManifestRecord
        {
            Name = Name, Kind = Kind, VersionTag = VersionTag,
            EntriesJson = EntriesJson, Notes = Notes,
            Actor = User.Identity?.Name ?? "?", UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AssetManifests.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(row.Actor, "asset.create", row.Name, row, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "资源清单已保存";
        return RedirectToPage();
    }
}
