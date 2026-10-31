using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class IndexModel(
    VersionPackService packs,
    MediaLibraryService media,
    AssetManifestService manifest, PackPreference packPref) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public string Kind { get; set; } = "all";
    public string Q { get; set; } = "";
    public bool ShowDeleted { get; set; }
    public List<MediaAsset> Items { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);
    public bool IsSuper => User.IsInRole(AdminRoles.SuperAdmin);

    public async Task OnGetAsync(string? packId, string? kind, string? q, bool showDeleted = false, CancellationToken ct = default)
    {
        Packs = packs.ListPacks().ToList();
        PackId = packPref.Resolve(packId);
        Kind = string.IsNullOrWhiteSpace(kind) ? "all" : kind;
        Q = q ?? "";
        ShowDeleted = showDeleted;
        await media.EnsureDirsAsync(PackId, ct);
        await manifest.EnsureSeedFilesAsync(PackId, ct);
        Items = (await media.QueryAsync(PackId, Kind, Q, includeDeleted: ShowDeleted, ct: ct)).ToList();
        if (ShowDeleted)
            Items = Items.Where(a => a.SoftDeleted).Concat(Items.Where(a => !a.SoftDeleted)).ToList();
    }

    public async Task<IActionResult> OnPostUploadAsync(
        string packId, string kind, string tags, List<IFormFile> files, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        if (files is null || files.Count == 0)
        {
            TempData["Error"] = "请选择文件";
            return RedirectToPage(new { packId, kind });
        }
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        int okCount = 0;
        var errors = new List<string>();
        foreach (var f in files)
        {
            var (ok, msg, _) = await media.UploadAsync(packId, kind, tags ?? "", f, actor, ip, ct);
            if (ok) okCount++;
            else errors.Add($"{f.FileName}: {msg}");
        }
        await manifest.RebuildManifestAsync(packId, actor, ip, ct);
        TempData["Flash"] = $"上传完成：成功 {okCount} 个" + (errors.Count > 0 ? "；失败：" + string.Join("；", errors) : "");
        return RedirectToPage(new { packId, kind });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, string packId, string kind, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await media.SoftDeleteAsync(id, actor, ip, ct);
        if (ok) { await manifest.RebuildManifestAsync(packId, actor, ip, ct); TempData["Flash"] = msg; }
        else TempData["Error"] = msg;
        return RedirectToPage(new { packId, kind });
    }

    public async Task<IActionResult> OnPostRestoreAsync(string id, string packId, string kind, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await media.RestoreAsync(id, actor, ip, ct);
        if (ok) { await manifest.RebuildManifestAsync(packId, actor, ip, ct); TempData["Flash"] = msg; }
        else TempData["Error"] = msg;
        return RedirectToPage(new { packId, kind, showDeleted = true });
    }

    public async Task<IActionResult> OnPostHardDeleteAsync(string id, string packId, string kind, CancellationToken ct)
    {
        if (!IsSuper) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await media.HardDeleteAsync(id, actor, ip, ct);
        if (ok) { await manifest.RebuildManifestAsync(packId, actor, ip, ct); TempData["Flash"] = msg; }
        else TempData["Error"] = msg;
        return RedirectToPage(new { packId, kind, showDeleted = true });
    }

    public async Task<IActionResult> OnPostMetaAsync(string id, string packId, string kind, string newKind, string tags, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await media.UpdateMetaAsync(id, newKind, tags, actor, ip, ct);
        if (ok) { await manifest.RebuildManifestAsync(packId, actor, ip, ct); TempData["Flash"] = msg; }
        else TempData["Error"] = msg;
        return RedirectToPage(new { packId, kind });
    }

    public async Task<IActionResult> OnPostReplaceAsync(string id, string packId, string kind, IFormFile file, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        if (file is null)
        {
            TempData["Error"] = "请选择替换文件";
            return RedirectToPage(new { packId, kind });
        }
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await media.ReplaceFileAsync(id, file, actor, ip, ct);
        if (ok) { await manifest.RebuildManifestAsync(packId, actor, ip, ct); TempData["Flash"] = msg; }
        else TempData["Error"] = msg;
        return RedirectToPage(new { packId, kind });
    }

    public async Task<IActionResult> OnPostBulkDeleteAsync(string packId, string kind, List<string> ids, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        ids ??= new();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (okCount, errors) = await media.BulkSoftDeleteAsync(ids, actor, ip, ct);
        await manifest.RebuildManifestAsync(packId, actor, ip, ct);
        TempData["Flash"] = $"批量软删 {okCount} 个" + (errors.Count > 0 ? "；" + string.Join("；", errors) : "");
        return RedirectToPage(new { packId, kind });
    }

    public async Task<IActionResult> OnPostRebuildAsync(string packId, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        await manifest.RebuildManifestAsync(packId, actor, ip, ct);
        TempData["Flash"] = "已重建 assets_manifest.json";
        return RedirectToPage(new { packId });
    }
}
