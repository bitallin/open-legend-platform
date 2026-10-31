using System.Text.Json;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class BindItemModel(
    VersionPackService packs,
    MediaLibraryService media,
    LookBindingService looks) : PageModel
{
    public string PackId { get; set; } = "sample-classic";
    public string ItemId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<MediaAsset> Assets { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    [BindProperty] public string? IconAssetId { get; set; }
    [BindProperty] public string? DropSpriteAssetId { get; set; }
    [BindProperty] public string? Paperdoll { get; set; }

    public async Task<IActionResult> OnGetAsync(string packId, string id, CancellationToken ct)
    {
        PackId = packId; ItemId = id;
        var (ok, _, content) = await packs.ReadAsync(packId, "configs/items.json", ct);
        if (!ok || content is null) { TempData["Error"] = "读不到 items.json"; return RedirectToPage("/Entities/Items", new { packId }); }
        var entities = packs.ExtractEntities(content, "items", "templates");
        var match = entities.FirstOrDefault(e => e.TryGetProperty("itemId", out var m) && m.GetString() == id);
        if (match.ValueKind == JsonValueKind.Undefined) { TempData["Error"] = "物品不存在"; return RedirectToPage("/Entities/Items", new { packId }); }
        DisplayName = match.TryGetProperty("displayName", out var dn) ? (dn.GetString() ?? id) : id;
        IconAssetId = Str(match, "iconAssetId");
        DropSpriteAssetId = Str(match, "dropSpriteAssetId");
        Paperdoll = Str(match, "paperdoll") ?? "";
        Assets = (await media.QueryAsync(packId, "all", null, ct: ct)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string packId, string id, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await looks.BindItemLookAsync(packId, id, IconAssetId, DropSpriteAssetId, Paperdoll, actor, ip, ct);
        TempData[ok ? "Flash" : "Error"] = msg;
        return RedirectToPage(new { packId, id });
    }

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
