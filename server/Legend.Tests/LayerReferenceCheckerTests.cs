using Legend.Contracts;
using Legend.Core;

namespace Legend.Tests;

public class LayerReferenceCheckerTests
{
    [Fact]
    public void RepositoryProjectReferencesMatchTheAllowList()
    {
        var violations = LayerReferenceScanner.FindViolations(FindRepoRoot());
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void PlayableCannotReferenceGame()
    {
        var actual = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Legend.Playable"] = new[] { "Legend.ClientCore", "Legend.Game" }
        };
        var violations = LayerReferenceScanner.Compare(actual, EngineLayerRules.AllowedProjectReferences);
        Assert.Contains("Legend.Playable -> Legend.Game", violations);
    }

    [Fact]
    public void CoreCannotReferenceStorage()
    {
        var actual = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Legend.Core"] = new[] { "Legend.Contracts", "Legend.Storage" }
        };
        var violations = LayerReferenceScanner.Compare(actual, EngineLayerRules.AllowedProjectReferences);
        Assert.Contains("Legend.Core -> Legend.Storage", violations);
    }

    [Fact]
    public void UnknownProjectIsReported()
    {
        var actual = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Legend.Future"] = Array.Empty<string>()
        };
        var violations = LayerReferenceScanner.Compare(actual, EngineLayerRules.AllowedProjectReferences);
        Assert.Contains(violations, violation => violation.Contains("Legend.Future"));
    }

    [Fact]
    public void ReadProjectReferencesUsesTheFileName()
    {
        string dir = Path.Combine(Path.GetTempPath(), "legend-layer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string csproj = Path.Combine(dir, "Sample.csproj");
        File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Legend.Contracts\Legend.Contracts.csproj" />
                <ProjectReference Include="..\Legend.Game\Legend.Game.csproj" />
              </ItemGroup>
            </Project>
            """);

        var refs = LayerReferenceScanner.ReadProjectReferences(csproj);
        Assert.Equal(new[] { "Legend.Contracts", "Legend.Game" }, refs);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "server", "LegendPlatform.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }
}
