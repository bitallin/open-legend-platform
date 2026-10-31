using System.Text.Json.Nodes;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Studio;

[Authorize(Policy = "AnyStaff")]
public class IndexModel(
    VersionPackService packs,
    JsonEntityStore store,
    MediaLibraryService media,
    LookThumbResolver thumbs,
    AssetManifestService manifest) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public string Q { get; set; } = "";
    public Dictionary<string, int> Counts { get; set; } = new();
    public List<MediaAsset> RecentMedia { get; set; } = new();
    public List<StudioEntityRow> MonsterRows { get; set; } = new();
    public List<StudioEntityRow> ItemRows { get; set; } = new();
    public List<StudioEntityRow> MapRows { get; set; } = new();
    public List<StudioEntityRow> NpcRows { get; set; } = new();
    public List<StudioHit> SearchHits { get; set; } = new();
    public int UiSlotBound { get; set; }
    public int UiSlotTotal { get; set; }
    public int ConfigFileCount { get; set; }

    public async Task OnGetAsync(string? packId, string? q, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        Q = q?.Trim() ?? "";
        await media.EnsureDirsAsync(PackId, ct);
        await manifest.EnsureSeedFilesAsync(PackId, ct);

        var assetMap = await thumbs.LoadPackAssetsAsync(PackId, ct);
        RecentMedia = (await media.QueryAsync(PackId, "all", null, ct: ct)).Take(24).ToList();

        async Task<List<StudioEntityRow>> Load(EntityDescriptor d, int take)
        {
            var (ok, _, list) = await store.ListAsync(PackId, d, ct);
            Counts[d.Kind] = ok ? list.Count : 0;
            var rows = new List<StudioEntityRow>();
            foreach (var e in list.Take(take))
            {
                rows.Add(new StudioEntityRow(
                    JsonEntityStore.GetStr(e, d.IdProperty),
                    JsonEntityStore.GetStr(e, d.DisplayProperty),
                    thumbs.ThumbUrlForEntity(e, d, assetMap),
                    d.Kind));
            }
            return rows;
        }

        MonsterRows = await Load(EntityCatalog.Monsters, 12);
        ItemRows = await Load(EntityCatalog.Items, 12);
        MapRows = await Load(EntityCatalog.Maps, 12);
        NpcRows = await Load(EntityCatalog.Npcs, 12);
        foreach (var d in new[] { EntityCatalog.Skills, EntityCatalog.Quests, EntityCatalog.Shops, EntityCatalog.Loot })
            _ = await Load(d, 0);

        Counts["media"] = RecentMedia.Count > 0
            ? (await media.QueryAsync(PackId, "all", null, ct: ct)).Count
            : 0;
        // recount media properly
        Counts["media"] = (await media.QueryAsync(PackId, "all", null, ct: ct)).Count;

        var skin = await manifest.ReadUiSkinAsync(PackId, ct);
        if (skin["slots"] is JsonObject slots)
        {
            UiSlotTotal = slots.Count;
            UiSlotBound = slots.Count(kv => kv.Value is JsonObject o && !string.IsNullOrEmpty(o["assetId"]?.GetValue<string>()));
        }
        ConfigFileCount = packs.ListConfigFiles(PackId).Count;

        if (!string.IsNullOrEmpty(Q))
        {
            var term = Q;
            foreach (var d in EntityCatalog.All)
            {
                var (ok, _, list) = await store.ListAsync(PackId, d, ct);
                if (!ok) continue;
                foreach (var e in list)
                {
                    var id = JsonEntityStore.GetStr(e, d.IdProperty);
                    var name = JsonEntityStore.GetStr(e, d.DisplayProperty);
                    var blob = e.ToJsonString();
                    if (id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        blob.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        SearchHits.Add(new StudioHit(d.Kind, d.TitleZh, id, name, thumbs.ThumbUrlForEntity(e, d, assetMap)));
                    }
                }
            }
            foreach (var a in await media.QueryAsync(PackId, "all", term, ct: ct))
                SearchHits.Add(new StudioHit("media", "素材", a.Id, a.FileName, thumbs.ThumbUrl(a)));
        }
    }

    public record StudioEntityRow(string Id, string Name, string? ThumbUrl, string Kind);
    public record StudioHit(string Kind, string KindZh, string Id, string Name, string? ThumbUrl);
}
