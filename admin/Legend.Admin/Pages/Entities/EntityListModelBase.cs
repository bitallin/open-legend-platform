using System.Text.Json.Nodes;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Entities;

[Authorize(Policy = "AnyStaff")]
public abstract class EntityListModelBase(
    VersionPackService packs,
    JsonEntityStore store,
    LookThumbResolver thumbs) : PageModel
{
    protected abstract EntityDescriptor Descriptor { get; }

    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public string Q { get; set; } = "";
    public List<Row> Rows { get; set; } = new();
    public EntityDescriptor Desc => Descriptor;
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    public async Task OnGetAsync(string? packId, string? q, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        Q = q?.Trim() ?? "";
        var assetMap = await thumbs.LoadPackAssetsAsync(PackId, ct);
        var (ok, _, list) = await store.ListAsync(PackId, Descriptor, ct);
        if (!ok) return;
        foreach (var e in list)
        {
            var id = JsonEntityStore.GetStr(e, Descriptor.IdProperty);
            var name = JsonEntityStore.GetStr(e, Descriptor.DisplayProperty);
            if (!string.IsNullOrEmpty(Q))
            {
                var blob = e.ToJsonString();
                if (!id.Contains(Q, StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains(Q, StringComparison.OrdinalIgnoreCase) &&
                    !blob.Contains(Q, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            var cols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Descriptor.ListColumns)
                cols[c] = JsonEntityStore.GetStr(e, c);
            Rows.Add(new Row(id, name, cols, thumbs.ThumbUrlForEntity(e, Descriptor, assetMap)));
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(string packId, string id, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await store.DeleteAsync(packId, Descriptor, id, actor, ip, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId });
    }

    public record Row(string Id, string Name, Dictionary<string, string> Cols, string? ThumbUrl);
}
