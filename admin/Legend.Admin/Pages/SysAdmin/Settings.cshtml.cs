using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.SystemPages;

public class SettingsModel(RepoPaths paths, HostAdminClient host) : PageModel
{
    public string RepoRoot { get; set; } = "";
    public string VersionPacks { get; set; } = "";
    public string Realms { get; set; } = "";
    public string AdminDb { get; set; } = "";
    public string HostBase { get; set; } = "";
    public bool HasToken { get; set; }

    public void OnGet()
    {
        RepoRoot = paths.RepoRoot;
        VersionPacks = paths.VersionPacksRoot;
        Realms = paths.RealmsRoot;
        AdminDb = Path.Combine(paths.AdminDataDir, "admin.db");
        HostBase = host.BaseUrl;
        HasToken = host.HasToken;
    }
}
