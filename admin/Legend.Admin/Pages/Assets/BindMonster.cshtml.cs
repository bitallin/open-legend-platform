using System.Text.Json;
using System.Text.Json.Nodes;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class BindMonsterModel(
    VersionPackService packs,
    MediaLibraryService media,
    LookBindingService looks) : PageModel
{
    public string PackId { get; set; } = "sample-classic";
    public string MonsterId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<MediaAsset> Assets { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    [BindProperty] public string? SpriteAssetId { get; set; }
    [BindProperty] public string? IconAssetId { get; set; }
    [BindProperty] public string? SheetKey { get; set; }
    [BindProperty] public int? FrameW { get; set; }
    [BindProperty] public int? FrameH { get; set; }
    [BindProperty] public int? Directions { get; set; }
    [BindProperty] public int? FramesPerDir { get; set; }

    public async Task<IActionResult> OnGetAsync(string packId, string id, CancellationToken ct)
    {
        PackId = packId; MonsterId = id;
        var (ok, _, content) = await packs.ReadAsync(packId, "configs/monsters.json", ct);
        if (!ok || content is null) { TempData["Error"] = "读不到 monsters.json"; return RedirectToPage("/Entities/Monsters", new { packId }); }
        using var doc = JsonDocument.Parse(content);
        var entities = packs.ExtractEntities(content, "templates", "monsters");
        var match = entities.FirstOrDefault(e => e.TryGetProperty("monsterId", out var m) && m.GetString() == id);
        if (match.ValueKind == JsonValueKind.Undefined) { TempData["Error"] = "怪物不存在"; return RedirectToPage("/Entities/Monsters", new { packId }); }
        DisplayName = match.TryGetProperty("displayName", out var dn) ? (dn.GetString() ?? id) : id;
        if (match.TryGetProperty("look", out var look) && look.ValueKind == JsonValueKind.Object)
        {
            SpriteAssetId = Str(look, "spriteAssetId");
            IconAssetId = Str(look, "iconAssetId");
            SheetKey = Str(look, "sheetKey");
            FrameW = Int(look, "frameW"); FrameH = Int(look, "frameH");
            Directions = Int(look, "directions"); FramesPerDir = Int(look, "framesPerDir");
        }
        Assets = (await media.QueryAsync(packId, "all", null, ct: ct)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string packId, string id, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await looks.BindMonsterLookAsync(packId, id, SpriteAssetId, IconAssetId, SheetKey, FrameW, FrameH, Directions, FramesPerDir, actor, ip, ct);
        TempData[ok ? "Flash" : "Error"] = msg;
        return RedirectToPage(new { packId, id });
    }

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : null;
}
