using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Entities;

public class MapsModel(VersionPackService packs) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<ConfigFileInfo> Maps { get; set; } = new();

    public void OnGet(string? packId)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        Maps = packs.ListConfigFiles(PackId).Where(f => f.Kind == "map").ToList();
    }
}
