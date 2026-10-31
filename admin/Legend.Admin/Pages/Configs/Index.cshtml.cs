using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Configs;

[Authorize(Policy = "AnyStaff")]
public class IndexModel(VersionPackService packs) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<ConfigFileInfo> Files { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    public void OnGet(string? packId)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        if (Packs.Any(p => p.Id == PackId))
            Files = packs.ListConfigFiles(PackId).ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync(string packId, string relativePath, string initialJson, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var (ok, msg) = await packs.CreateConfigFileAsync(packId, relativePath, initialJson ?? "{}\n", actor, "create", ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string packId, string path, bool allowNonEmpty, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var (ok, msg) = await packs.DeleteConfigFileAsync(packId, path, actor, allowNonEmpty, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId });
    }

    public async Task<IActionResult> OnPostCopyAsync(string packId, string fromRel, string toRel, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var (ok, msg) = await packs.CopyConfigFileAsync(packId, fromRel, toRel, actor, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId });
    }
}
