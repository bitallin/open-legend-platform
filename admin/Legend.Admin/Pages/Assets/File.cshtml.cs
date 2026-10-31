using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class FileModel : PageModel
{
    private readonly MediaLibraryService _media;

    public FileModel(MediaLibraryService media) => _media = media;

    public async Task<IActionResult> OnGetAsync(string id, bool thumb = true, bool download = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var asset = await _media.FindAsync(id, ct);
        if (asset is null || asset.SoftDeleted)
            return NotFound();

        var path = thumb ? _media.ResolveThumbAbsolute(asset) : _media.ResolveAbsolute(asset);
        if (path is null || !System.IO.File.Exists(path))
            return NotFound();

        var contentType = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : (string.IsNullOrWhiteSpace(asset.ContentType) ? "application/octet-stream" : asset.ContentType);

        if (download)
            return PhysicalFile(path, contentType, asset.FileName);
        return PhysicalFile(path, contentType);
    }
}
