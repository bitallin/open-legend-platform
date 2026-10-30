namespace Legend.Contracts;

/// <summary>
/// Project-reference allow list for production layers.
/// The scanner in Legend.Core fails the build tests when a listed project gains an edge that is not here.
/// Persistence ports still live in Legend.Storage, so Game and Login may reference that assembly until E2.
/// </summary>
public static class EngineLayerRules
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedProjectReferences { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Legend.Contracts"] = Set(),
            ["Legend.Core"] = Set("Legend.Contracts"),
            ["Legend.Storage"] = Set("Legend.Contracts"),
            ["Legend.Gate"] = Set("Legend.Contracts", "Legend.Core"),
            ["Legend.Script"] = Set("Legend.Contracts", "Legend.Core"),
            ["Legend.Login"] = Set("Legend.Contracts", "Legend.Core", "Legend.Storage"),
            ["Legend.Game"] = Set("Legend.Contracts", "Legend.Core", "Legend.Storage"),
            ["Legend.Host"] = Set(
                "Legend.Contracts",
                "Legend.Core",
                "Legend.Gate",
                "Legend.Login",
                "Legend.Game",
                "Legend.Script",
                "Legend.Storage"),
            ["Legend.CliClient"] = Set("Legend.Contracts", "Legend.Storage"),
            ["Legend.ClientCore"] = Set(),
            ["Legend.Playable"] = Set("Legend.ClientCore", "Legend.Contracts"),
            ["Legend.Launcher.Core"] = Set(),
            ["Legend.Launcher"] = Set("Legend.Launcher.Core"),
            ["Legend.Launcher.Desktop"] = Set("Legend.Launcher.Core"),
            ["Legend.ClientStub"] = Set(),
            ["Legend.MapValidate"] = Set("Legend.Contracts", "Legend.Core"),
            ["Legend.Merge"] = Set("Legend.Storage"),
        };

    /// <summary>Server projects that must appear in <see cref="AllowedProjectReferences"/>. Tests are exempt.</summary>
    public static IReadOnlySet<string> ExemptServerProjects { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Legend.Tests" };

    private static IReadOnlySet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}
