using System.Text.Json.Nodes;
using Legend.Admin.Data;
using Legend.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Services;

public sealed class LookThumbResolver(AdminDbContext db)
{
    public async Task<Dictionary<string, MediaAsset>> LoadPackAssetsAsync(string packId, CancellationToken ct = default)
    {
        var list = await db.MediaAssets.AsNoTracking()
            .Where(a => a.PackId == packId && !a.SoftDeleted)
            .ToListAsync(ct);
        return list.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string? ThumbUrl(MediaAsset? asset)
    {
        if (asset is null) return null;
        return $"/api/media/{Uri.EscapeDataString(asset.Id)}?thumb=true";
    }

    public string? ThumbUrlForEntity(JsonObject entity, EntityDescriptor desc, IReadOnlyDictionary<string, MediaAsset> byId)
    {
        var assetId = JsonEntityStore.GetLookAssetId(entity, desc.LookAssetProperty);
        if (string.IsNullOrEmpty(assetId)) return null;
        return byId.TryGetValue(assetId, out var a) ? ThumbUrl(a) : null;
    }
}
