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
    AssetManifestService manifest) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public string Kind { get; set; } = "all";
    public string Q { get; set; } = "";
    public List<MediaAsset> Items { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    public async Task OnGetAsync(string? packId, string? kind, string? q, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        Kind = string.IsNullOrWhiteSpace(kind) ? "all" : kind;
        Q = q ?? "";
        await media.EnsureDirsAsync(PackId, ct);
        await manifest.EnsureSeedFilesAsync(PackId, ct);
        Items = (await media.QueryAsync(PackId, Kind, Q, ct: ct)).ToList();
    }

    public async Task<IActionResult> OnPostUploadAsync(
        string packId, string kind, string tags, List<IFormFile> files, CancellationToken ct)
    {
        if (!(User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator)))
            return Forbid();
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
        var manifestSvc = HttpContext.RequestServices.GetRequiredService<AssetManifestService>();
        await manifestSvc.RebuildManifestAsync(packId, actor, ip, ct);
        TempData["Flash"] = $"上传完成：成功 {okCount} 个" + (errors.Count > 0 ? "；失败：" + string.Join("；", errors) : "");
        return RedirectToPage(new { packId, kind });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, string packId, string kind, CancellationToken ct)
    {
        if (!(User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator)))
            return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await media.SoftDeleteAsync(id, actor, ip, ct);
        if (ok)
        {
            var manifestSvc = HttpContext.RequestServices.GetRequiredService<AssetManifestService>();
            await manifestSvc.RebuildManifestAsync(packId, actor, ip, ct);
            TempData["Flash"] = msg;
        }
        else TempData["Error"] = msg;
        return RedirectToPage(new { packId, kind });
    }

    public async Task<IActionResult> OnPostRebuildAsync(string packId, CancellationToken ct)
    {
        if (!(User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator)))
            return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var manifestSvc = HttpContext.RequestServices.GetRequiredService<AssetManifestService>();
        await manifestSvc.RebuildManifestAsync(packId, actor, ip, ct);
        TempData["Flash"] = "已重建 assets_manifest.json";
        return RedirectToPage(new { packId });
    }
}
