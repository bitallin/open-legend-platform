using System.Text.Json;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Entities;

public class MonstersModel(VersionPackService packs) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<string> Columns { get; } = new() { "monsterId", "displayName", "maxHp", "atk", "def", "lootTableId", "expReward" };
    public List<Dictionary<string, string>> Rows { get; set; } = new();

    public async Task OnGetAsync(string? packId, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        var (ok, _, content) = await packs.ReadAsync(PackId, "configs/monsters.json", ct);
        if (!ok || content is null) return;
        var entities = packs.ExtractEntities(content, "templates", "monsters");
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
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("look", out var look) && look.ValueKind == JsonValueKind.Object)
            {
                var bits = new List<string>();
                if (look.TryGetProperty("spriteAssetId", out var s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(s.GetString())) bits.Add("sprite");
                if (look.TryGetProperty("iconAssetId", out var ic) && ic.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(ic.GetString())) bits.Add("icon");
                dict["_look"] = string.Join(",", bits);
            }
            else dict["_look"] = "";
            Rows.Add(dict);
        }
    }
}
