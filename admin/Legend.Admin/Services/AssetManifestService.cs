using System.Text.Json;
using System.Text.Json.Nodes;
using Legend.Admin.Data;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Services;

public sealed class AssetManifestService(VersionPackService packs, AdminDbContext db, AuditService audit)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static readonly string[] UiSlots =
    [
        "login_bg", "hud_frame", "hp_orb", "mp_orb", "menu_ring", "window_frame",
        "item_icon_atlas", "minimap_frame", "chat_frame", "bag_window", "equip_window",
        "hotbar_slot", "btn_primary", "panel_bg"
    ];

    public async Task EnsureSeedFilesAsync(string packId, CancellationToken ct = default)
    {
        var root = packs.GetPackRoot(packId);
        Directory.CreateDirectory(Path.Combine(root, "assets", "library"));
        Directory.CreateDirectory(Path.Combine(root, "configs"));
        var manifestPath = Path.Combine(root, "configs", "assets_manifest.json");
        if (!File.Exists(manifestPath))
        {
            var seed = new
            {
                version = 1,
                packId,
                updatedAt = DateTimeOffset.UtcNow,
                assets = Array.Empty<object>()
            };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(seed, Pretty) + "\n", ct);
        }
        var skinPath = Path.Combine(root, "configs", "ui_skin.json");
        if (!File.Exists(skinPath))
        {
            var slots = new JsonObject();
            foreach (var s in UiSlots)
                slots[s] = new JsonObject { ["assetId"] = "", ["note"] = "" };
            var skin = new JsonObject
            {
                ["version"] = 1,
                ["packId"] = packId,
                ["slots"] = slots
            };
            await File.WriteAllTextAsync(skinPath, skin.ToJsonString(Pretty) + "\n", ct);
        }
    }

    public async Task RebuildManifestAsync(string packId, string actor, string ip, CancellationToken ct = default)
    {
        await EnsureSeedFilesAsync(packId, ct);
        var assets = (await db.MediaAssets
            .Where(a => a.PackId == packId && !a.SoftDeleted)
            .ToListAsync(ct))
            .OrderBy(a => a.CreatedAt)
            .ToList();
        var entries = assets.Select(a => new Dictionary<string, object?>
        {
            ["id"] = a.Id,
            ["kind"] = a.Kind,
            ["fileName"] = a.FileName,
            ["path"] = a.RelPath,
            ["thumb"] = a.ThumbRelPath,
            ["contentType"] = a.ContentType,
            ["bytes"] = a.Bytes,
            ["width"] = a.Width,
            ["height"] = a.Height,
            ["sha256"] = a.Sha256,
            ["tags"] = a.Tags
        }).ToList();
        var doc = new
        {
            version = 1,
            packId,
            updatedAt = DateTimeOffset.UtcNow,
            updatedBy = actor,
            assets = entries
        };
        var json = JsonSerializer.Serialize(doc, Pretty) + "\n";
        var (ok, msg, hash) = await packs.WriteAsync(packId, "configs/assets_manifest.json", json, actor, "rebuild assets_manifest", ct);
        if (!ok) throw new InvalidOperationException(msg);
        await audit.WriteAsync(actor, "assets.manifest.rebuild", packId, new { count = entries.Count, hash }, ip, ct);
    }

    public async Task<JsonObject> ReadUiSkinAsync(string packId, CancellationToken ct = default)
    {
        await EnsureSeedFilesAsync(packId, ct);
        var (ok, _, content) = await packs.ReadAsync(packId, "configs/ui_skin.json", ct);
        if (!ok || string.IsNullOrWhiteSpace(content))
            return new JsonObject();
        return JsonNode.Parse(content) as JsonObject ?? new JsonObject();
    }

    public async Task SaveUiSkinSlotAsync(string packId, string slot, string assetId, string note, string actor, string ip, CancellationToken ct = default)
    {
        var root = await ReadUiSkinAsync(packId, ct);
        var slots = root["slots"] as JsonObject ?? new JsonObject();
        root["slots"] = slots;
        slots[slot] = new JsonObject
        {
            ["assetId"] = assetId ?? "",
            ["note"] = note ?? ""
        };
        root["version"] = 1;
        root["packId"] = packId;
        root["updatedAt"] = DateTimeOffset.UtcNow.ToString("o");
        root["updatedBy"] = actor;
        var json = root.ToJsonString(Pretty) + "\n";
        var (ok, msg, hash) = await packs.WriteAsync(packId, "configs/ui_skin.json", json, actor, $"ui_skin:{slot}", ct);
        if (!ok) throw new InvalidOperationException(msg);

        // publish copy into assets/ui/{slot}.ext when asset bound
        if (!string.IsNullOrWhiteSpace(assetId))
        {
            var media = await db.MediaAssets.FindAsync([assetId], ct);
            if (media is not null && !media.SoftDeleted)
            {
                var packRoot = packs.GetPackRoot(packId);
                var src = Path.Combine(packRoot, media.RelPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(src))
                {
                    var ext = Path.GetExtension(src);
                    var destRel = $"assets/ui/{slot}{ext}";
                    var dest = Path.Combine(packRoot, destRel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(src, dest, overwrite: true);
                    if (slots[slot] is JsonObject so)
                        so["publishedPath"] = destRel;
                    json = root.ToJsonString(Pretty) + "\n";
                    await packs.WriteAsync(packId, "configs/ui_skin.json", json, actor, $"ui_skin publish:{slot}", ct);
                }
            }
        }

        await audit.WriteAsync(actor, "ui_skin.bind", $"{packId}/{slot}", new { assetId, note, hash }, ip, ct);
    }
}
