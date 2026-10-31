using System.Text.Json;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Entities;

public class SkillsModel(VersionPackService packs) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<string> Columns { get; } = new() { "skillId", "displayName", "classId", "cooldownMs" };
    public List<Dictionary<string, string>> Rows { get; set; } = new();

    public async Task OnGetAsync(string? packId, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        var (ok, _, content) = await packs.ReadAsync(PackId, "configs/skills.json", ct);
        if (!ok || content is null) return;
        var entities = packs.ExtractEntities(content, "skills", "templates");
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
            Rows.Add(dict);
        }
    }
}
