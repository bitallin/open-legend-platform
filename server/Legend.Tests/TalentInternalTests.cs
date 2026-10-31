using Legend.Contracts;
using Legend.Core;
using Legend.Storage;

namespace Legend.Tests;

public class TalentInternalTests
{
    [Fact]
    public void Talents_And_Internal_SamplePack_Load()
    {
        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.NotEmpty(pack.Talents.Trees);
        Assert.Equal(1, pack.Talents.PointsPerLevel);
        Assert.Equal("gold", pack.Talents.ResetCost.CurrencyId);
        Assert.Contains(pack.Talents.Trees, t => t.TreeId == "warrior_basic");
        var tree = pack.Talents.Trees.First(t => t.TreeId == "warrior_basic");
        Assert.Equal(2, tree.Nodes.Count);

        Assert.Equal(5, pack.Internal.MaxLevel);
        Assert.Equal(5, pack.Internal.Entries.Count);
        Assert.Contains(pack.Internal.Meridians, m => m.MeridianId == "meridian_ren");
    }

    [Fact]
    public void Talents_Loader_RejectsUnknownStatId()
    {
        string source = Path.GetDirectoryName(RepoFile("version-packs", "sample-classic", "manifest.json"))!;
        string dest = Path.Combine(Path.GetTempPath(), "legend-tal-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, dest);
        try
        {
            File.WriteAllText(Path.Combine(dest, "configs", "talents.json"), """
                {
                  "pointsPerLevel": 1,
                  "resetCost": { "currencyId": "gold", "amount": 0 },
                  "trees": [{
                    "treeId": "bad",
                    "nodes": [{
                      "nodeId": "n1",
                      "requires": [],
                      "costPoints": 1,
                      "effects": [{ "statId": "not_a_real_stat", "value": 1 }]
                    }]
                  }]
                }
                """);
            var ex = Assert.ThrowsAny<Exception>(() =>
                new VersionPackLoader().LoadFromManifest(Path.Combine(dest, "manifest.json")));
            Assert.Contains("not_a_real_stat", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dest, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TalentProgress_UnlockOrder_And_Points()
    {
        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        var tree = pack.Talents.Trees.First(t => t.TreeId == "warrior_basic");
        var unlocked = new List<string>();
        int available = TalentProgress.AvailablePoints(characterLevel: 2, pointsPerLevel: 1, spentPoints: 0);
        Assert.Equal(2, available);
        Assert.True(TalentProgress.CanUnlock(tree, "w_atk_1", unlocked, available, out _));
        unlocked.Add("w_atk_1");
        int spent = TalentProgress.CountSpentPoints(tree, unlocked);
        Assert.Equal(1, spent);
        available = TalentProgress.AvailablePoints(2, 1, spent);
        Assert.True(TalentProgress.CanUnlock(tree, "w_hp_1", unlocked, available, out _));
        Assert.False(TalentProgress.CanUnlock(tree, "w_hp_1", new List<string>(), availablePoints: 5, out var reason));
        Assert.Contains("prerequisite", reason, StringComparison.OrdinalIgnoreCase);

        unlocked.Add("w_hp_1");
        var byTree = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["warrior_basic"] = unlocked
        };
        var stats = TalentProgress.SumStatEffects(pack.Talents, byTree);
        Assert.Equal(2, stats["atk"]);
        Assert.Equal(20, stats["maxHp"]);
    }

    [Fact]
    public void AppearanceJson_Persists_Internal_And_Talents()
    {
        var state = new AppearanceJson.State
        {
            InternalLevel = 3,
            InternalExp = 42,
            UnlockedMeridians = new List<string> { "meridian_ren" },
            TalentUnlocked = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["warrior_basic"] = new List<string> { "w_atk_1" }
            }
        };
        string json = AppearanceJson.Serialize(state);
        var round = AppearanceJson.Parse(json);
        Assert.Equal(3, round.InternalLevel);
        Assert.Equal(42, round.InternalExp);
        Assert.Contains("meridian_ren", round.UnlockedMeridians);
        Assert.True(round.TalentUnlocked.ContainsKey("warrior_basic"));
        Assert.Contains("w_atk_1", round.TalentUnlocked["warrior_basic"]);
    }

    [Fact]
    public void Internal_Meridian_RequiresLevel()
    {
        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.False(InternalProgress.CanUnlockMeridian(
            pack.Internal, "meridian_ren", internalLevel: 1, alreadyUnlocked: Array.Empty<string>(),
            out _, out var reason));
        Assert.Contains("level", reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(InternalProgress.CanUnlockMeridian(
            pack.Internal, "meridian_ren", internalLevel: 2, alreadyUnlocked: Array.Empty<string>(),
            out var m, out _));
        Assert.Equal("meridian_ren", m!.MeridianId);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(dest, Path.GetFileName(directory)));
    }
}
