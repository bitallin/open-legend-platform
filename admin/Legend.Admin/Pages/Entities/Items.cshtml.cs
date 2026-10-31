using System.Text.Json;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Entities;

public class ItemsModel(VersionPackService packs) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<string> Columns { get; } = new() { "itemId", "displayName", "kind", "slot", "paperdoll" };
    public List<Dictionary<string, string>> Rows { get; set; } = new();

    public async Task OnGetAsync(string? packId, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        var (ok, _, content) = await packs.ReadAsync(PackId, "configs/items.json", ct);
        if (!ok || content is null) return;
        var entities = packs.ExtractEntities(content, "items", "templates");
        foreach (var e in entities)
        {
            var dict = new Dictionary<string, string>();
            foreach (var c in Columns)
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(c, out var v))
                    dict[c] = v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
                else dict[c] = "";
            }
            dict["_preview"] = e.ToString();
            if (dict["_preview"].Length > 160) dict["_preview"] = dict["_preview"][..160] + "…";
            var looks = new List<string>();
            if (e.TryGetProperty("iconAssetId", out var ic) && ic.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(ic.GetString())) looks.Add("icon");
            if (e.TryGetProperty("dropSpriteAssetId", out var ds) && ds.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(ds.GetString())) looks.Add("drop");
            dict["_look"] = string.Join(",", looks);
            Rows.Add(dict);
        }
    }
}
