using System.Text.Json;
using System.Text.Json.Nodes;
using Legend.Admin.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;

namespace Legend.Admin.Services;

public sealed class LookBindingService(VersionPackService packs, AdminDbContext db, AuditService audit)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<(bool ok, string message)> BindMonsterLookAsync(
        string packId, string monsterId,
        string? spriteAssetId, string? iconAssetId, string? sheetKey,
        int? frameW, int? frameH, int? directions, int? framesPerDir,
        string actor, string ip, CancellationToken ct = default)
    {
        var (ok, msg, content) = await packs.ReadAsync(packId, "configs/monsters.json", ct);
        if (!ok || content is null) return (false, msg);
        var root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
        var arr = FindArray(root, "templates", "monsters");
        if (arr is null) return (false, "monsters.json 缺少 templates 数组");
        var node = FindById(arr, "monsterId", monsterId);
        if (node is null) return (false, "找不到怪物 " + monsterId);
        node["look"] = BuildMonsterLook(spriteAssetId, iconAssetId, sheetKey, frameW, frameH, directions, framesPerDir);
        PublishCopy(packId, spriteAssetId, $"assets/monsters/{monsterId}_sprite");
        PublishCopy(packId, iconAssetId, $"assets/monsters/{monsterId}_icon");
        return await SaveJsonAsync(packId, "configs/monsters.json", root, actor, $"monster.look:{monsterId}", ip, "monster.look", ct);
    }

    public async Task<(bool ok, string message)> BindItemLookAsync(
        string packId, string itemId,
        string? iconAssetId, string? dropSpriteAssetId, string? paperdoll,
        string actor, string ip, CancellationToken ct = default)
    {
        var (ok, msg, content) = await packs.ReadAsync(packId, "configs/items.json", ct);
        if (!ok || content is null) return (false, msg);
        var root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
        var arr = FindArray(root, "items", "templates");
        if (arr is null) return (false, "items.json 缺少 items 数组");
        var node = FindById(arr, "itemId", itemId);
        if (node is null) return (false, "找不到物品 " + itemId);
        if (!string.IsNullOrWhiteSpace(iconAssetId)) node["iconAssetId"] = iconAssetId;
        else node.Remove("iconAssetId");
        if (!string.IsNullOrWhiteSpace(dropSpriteAssetId)) node["dropSpriteAssetId"] = dropSpriteAssetId;
        else node.Remove("dropSpriteAssetId");
        if (paperdoll is not null)
        {
            if (paperdoll.Length == 0) node.Remove("paperdoll");
            else node["paperdoll"] = paperdoll;
        }
        PublishCopy(packId, iconAssetId, $"assets/items/{itemId}_icon");
        PublishCopy(packId, dropSpriteAssetId, $"assets/items/{itemId}_drop");
        return await SaveJsonAsync(packId, "configs/items.json", root, actor, $"item.look:{itemId}", ip, "item.look", ct);
    }

    public async Task<(bool ok, string message)> BindMapLookAsync(
        string packId, string mapId,
        string? tileAssetId, string? minimapAssetId, string? backdropAssetId,
        string actor, string ip, CancellationToken ct = default)
    {
        var rel = $"maps/{mapId}.json";
        var (ok, msg, content) = await packs.ReadAsync(packId, rel, ct);
        if (!ok || content is null) return (false, msg);
        var root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
        var look = new JsonObject();
        if (!string.IsNullOrWhiteSpace(tileAssetId)) look["tileAssetId"] = tileAssetId;
        if (!string.IsNullOrWhiteSpace(minimapAssetId)) look["minimapAssetId"] = minimapAssetId;
        if (!string.IsNullOrWhiteSpace(backdropAssetId)) look["backdropAssetId"] = backdropAssetId;
        if (look.Count == 0) root.Remove("look");
        else root["look"] = look;
        Directory.CreateDirectory(Path.Combine(packs.GetPackRoot(packId), "assets", "maps", mapId));
        PublishCopy(packId, tileAssetId, $"assets/maps/{mapId}/tile");
        PublishCopy(packId, minimapAssetId, $"assets/maps/{mapId}/minimap");
        PublishCopy(packId, backdropAssetId, $"assets/maps/{mapId}/backdrop");
        return await SaveJsonAsync(packId, rel, root, actor, $"map.look:{mapId}", ip, "map.look", ct);
    }

    public (string? sprite, string? icon, string? sheetKey, int? fw, int? fh, int? dirs, int? fpd) ReadMonsterLook(JsonObject node)
    {
        if (node["look"] is not JsonObject look) return (null, null, null, null, null, null, null);
        return (
            look["spriteAssetId"]?.GetValue<string>(),
            look["iconAssetId"]?.GetValue<string>(),
            look["sheetKey"]?.GetValue<string>(),
            look["frameW"]?.GetValue<int?>(),
            look["frameH"]?.GetValue<int?>(),
            look["directions"]?.GetValue<int?>(),
            look["framesPerDir"]?.GetValue<int?>()
        );
    }

    private static JsonObject BuildMonsterLook(string? sprite, string? icon, string? sheetKey, int? fw, int? fh, int? dirs, int? fpd)
    {
        var look = new JsonObject();
        if (!string.IsNullOrWhiteSpace(sprite)) look["spriteAssetId"] = sprite;
        if (!string.IsNullOrWhiteSpace(icon)) look["iconAssetId"] = icon;
        if (!string.IsNullOrWhiteSpace(sheetKey)) look["sheetKey"] = sheetKey;
        if (fw is > 0) look["frameW"] = fw;
        if (fh is > 0) look["frameH"] = fh;
        if (dirs is > 0) look["directions"] = dirs;
        if (fpd is > 0) look["framesPerDir"] = fpd;
        return look;
    }

    private void PublishCopy(string packId, string? assetId, string destStemWithoutExt)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return;
        var row = db.MediaAssets.AsNoTracking().FirstOrDefault(a => a.Id == assetId && !a.SoftDeleted);
        if (row is null) return;
        var packRoot = packs.GetPackRoot(packId);
        var src = Path.Combine(packRoot, row.RelPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(src)) return;
        var ext = Path.GetExtension(src);
        var dest = Path.Combine(packRoot, (destStemWithoutExt + ext).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: true);
    }

    private async Task<(bool ok, string message)> SaveJsonAsync(
        string packId, string rel, JsonObject root, string actor, string note, string ip, string action, CancellationToken ct)
    {
        var json = root.ToJsonString(Pretty) + "\n";
        var (ok, msg, hash) = await packs.WriteAsync(packId, rel, json, actor, note, ct);
        if (!ok) return (false, msg);
        await audit.WriteAsync(actor, action, $"{packId}/{rel}", new { note, hash }, ip, ct);
        return (true, "已保存外观绑定");
    }

    private static JsonArray? FindArray(JsonObject root, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (root[k] is JsonArray arr) return arr;
        }
        return null;
    }

    private static JsonObject? FindById(JsonArray arr, string idProp, string id)
    {
        foreach (var el in arr)
        {
            if (el is JsonObject o && string.Equals(o[idProp]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
                return o;
        }
        return null;
    }
}
