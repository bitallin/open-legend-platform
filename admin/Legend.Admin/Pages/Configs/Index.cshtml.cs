using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Configs;

public class IndexModel(VersionPackService packs) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<ConfigFileInfo> Files { get; set; } = new();

    public void OnGet(string? packId)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        if (Packs.Any(p => p.Id == PackId))
            Files = packs.ListConfigFiles(PackId).ToList();
    }
}
