namespace Legend.Admin.Services;

public sealed class RepoPaths
{
    public string RepoRoot { get; }
    public string VersionPacksRoot { get; }
    public string RealmsRoot { get; }
    public string AdminDataDir { get; }

    public RepoPaths(IConfiguration config, IHostEnvironment env)
    {
        // Prefer configured absolute/relative path; default walk up from ContentRoot to find version-packs/
        var configured = config["Legend:RepoRoot"];
        string root;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            root = Path.GetFullPath(configured);
        else
            root = FindRepoRoot(env.ContentRootPath) ?? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));

        RepoRoot = root;
        VersionPacksRoot = Path.Combine(RepoRoot, "version-packs");
        RealmsRoot = Path.Combine(RepoRoot, "realms");
        AdminDataDir = Path.Combine(RepoRoot, "data", "admin");
        Directory.CreateDirectory(AdminDataDir);
    }

    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "version-packs")) &&
                Directory.Exists(Path.Combine(dir.FullName, "server")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
