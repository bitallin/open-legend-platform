using System.Text.Json;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class BindMapModel(
    VersionPackService packs,
    MediaLibraryService media,
    LookBindingService looks) : PageModel
{
    public string PackId { get; set; } = "sample-classic";
    public string MapId { get; set; } = "";
    public string MapName { get; set; } = "";
    public List<MediaAsset> Assets { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    [BindProperty] public string? TileAssetId { get; set; }
    [BindProperty] public string? MinimapAssetId { get; set; }
    [BindProperty] public string? BackdropAssetId { get; set; }

    public async Task<IActionResult> OnGetAsync(string packId, string id, CancellationToken ct)
    {
        PackId = packId; MapId = id;
        var rel = $"maps/{id}.json";
        var (ok, _, content) = await packs.ReadAsync(packId, rel, ct);
        if (!ok || content is null) { TempData["Error"] = "读不到地图"; return RedirectToPage("/Entities/Maps", new { packId }); }
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        MapName = root.TryGetProperty("mapName", out var mn) ? (mn.GetString() ?? id) : id;
        if (root.TryGetProperty("look", out var look) && look.ValueKind == JsonValueKind.Object)
        {
            TileAssetId = Str(look, "tileAssetId");
            MinimapAssetId = Str(look, "minimapAssetId");
            BackdropAssetId = Str(look, "backdropAssetId");
        }
        Assets = (await media.QueryAsync(packId, "all", null, ct: ct)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string packId, string id, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await looks.BindMapLookAsync(packId, id, TileAssetId, MinimapAssetId, BackdropAssetId, actor, ip, ct);
        TempData[ok ? "Flash" : "Error"] = msg;
        return RedirectToPage(new { packId, id });
    }

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
