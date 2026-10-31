using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class FileModel(MediaLibraryService media) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string id, bool thumb = true, bool download = false, CancellationToken ct = default)
    {
        var asset = await media.FindAsync(id, ct);
        if (asset is null) return NotFound();
        var path = thumb ? media.ResolveThumbAbsolute(asset) : media.ResolveAbsolute(asset);
        if (path is null) return NotFound();
        var contentType = thumb && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : asset.ContentType;
        if (download)
            return PhysicalFile(path, contentType, asset.FileName);
        return PhysicalFile(path, contentType);
    }
}
