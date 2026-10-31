using System.Text.Json.Nodes;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class UiSkinModel(
    VersionPackService packs,
    AssetManifestService manifest,
    MediaLibraryService media, PackPreference packPref) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public JsonObject Skin { get; set; } = new();
    public List<string> SlotNames { get; set; } = new();
    public List<MediaAsset> UiAssets { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    public async Task OnGetAsync(string? packId, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = packPref.Resolve(packId);
        Skin = await manifest.ReadUiSkinAsync(PackId, ct);
        if (Skin["slots"] is JsonObject slots)
            SlotNames = slots.Select(kv => kv.Key).OrderBy(k => k).ToList();
        UiAssets = (await media.QueryAsync(PackId, "ui", null, ct: ct)).ToList();
        if (UiAssets.Count == 0)
            UiAssets = (await media.QueryAsync(PackId, "all", null, ct: ct)).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string packId, string slot, string assetId, string note, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        await manifest.SaveUiSkinSlotAsync(packId, slot, assetId ?? "", note ?? "", actor, ip, ct);
        TempData["Flash"] = $"已绑定 UI 槽位 {slot}";
        return RedirectToPage(new { packId });
    }

    public async Task<IActionResult> OnPostAddSlotAsync(string packId, string newSlot, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await manifest.AddUiSkinSlotAsync(packId, newSlot, actor, ip, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId });
    }

    public async Task<IActionResult> OnPostRemoveSlotAsync(string packId, string slot, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await manifest.RemoveUiSkinSlotAsync(packId, slot, actor, ip, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId });
    }

    public string SlotAssetId(string slot)
    {
        if (Skin["slots"] is JsonObject slots && slots[slot] is JsonObject o)
            return o["assetId"]?.GetValue<string>() ?? "";
        return "";
    }

    public string SlotNote(string slot)
    {
        if (Skin["slots"] is JsonObject slots && slots[slot] is JsonObject o)
            return o["note"]?.GetValue<string>() ?? "";
        return "";
    }
}
