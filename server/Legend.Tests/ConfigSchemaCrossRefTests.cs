using System.Net;
using Legend.Contracts;
using Legend.Core;
using Legend.Host;

namespace Legend.Tests;

public class ConfigSchemaCrossRefTests
{
    [Fact]
    public void SampleClassicPackLoadsProductionConfigs()
    {
        var pack = new VersionPackLoader().LoadFromManifest(RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.True(pack.Skills.Count > 0);
        Assert.True(pack.Monsters.Count > 0);
        Assert.True(pack.LootTables.Count > 0);
        Assert.True(pack.Npcs.Count > 0);
        Assert.True(pack.Shops.Count > 0);
        Assert.True(pack.Quests.Count > 0);
        Assert.True(pack.Levels.Entries.Count > 0);
        Assert.Contains("item_slime_gel", pack.Items.Keys);
        Assert.Contains("item_boar_hide", pack.Items.Keys);
    }

    [Fact]
    public void UnknownLootItemIdFailsLoad()
    {
        string dest = CopySampleClassic();
        string lootPath = Path.Combine(dest, "configs", "loot_tables.json");
        string loot = File.ReadAllText(lootPath).Replace("item_slime_gel", "item_not_in_catalog", StringComparison.Ordinal);
        File.WriteAllText(lootPath, loot);

        var ex = Assert.Throws<InvalidDataException>(() =>
            new VersionPackLoader().LoadFromManifest(Path.Combine(dest, "manifest.json")));
        Assert.Contains("item_not_in_catalog", ex.Message);
        Assert.Contains("is not in items", ex.Message);
    }

    [Fact]
    public void UnknownSkillKindFailsSchema()
    {
        const string json = """[{"skillId":"slash","displayName":"斩","kind":"combo"}]""";
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            new ConfigSchemaValidator().Validate(ConfigSchemaIds.Skills, json, "skills.json"));
        Assert.Contains("kind", ex.Message);
    }

    [Fact]
    public void DuplicateSkillIdFailsSchema()
    {
        const string json = """
            [
              {"skillId":"slash","displayName":"斩"},
              {"skillId":"slash","displayName":"再斩"}
            ]
            """;
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            new ConfigSchemaValidator().Validate(ConfigSchemaIds.Skills, json, "skills.json"));
        Assert.Contains("duplicate", ex.Message);
    }

    [Fact]
    public void LootCountMaxBelowMinFailsSchema()
    {
        const string json = """[{"lootTableId":"t","entries":[{"itemId":"item_a","countMin":3,"countMax":1}]}]""";
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            new ConfigSchemaValidator().Validate(ConfigSchemaIds.LootTables, json, "loot.json"));
        Assert.Contains("countMax", ex.Message);
    }

    [Fact]
    public void TradePriceOrderFailsSchema()
    {
        const string json = """{"minUnitPriceGold":10,"maxUnitPriceGold":1}""";
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            new ConfigSchemaValidator().Validate(ConfigSchemaIds.Trade, json, "trade.json"));
        Assert.Contains("maxUnitPriceGold", ex.Message);
    }

    [Fact]
    public async Task ConfigSchema_ReloadFailureRollsBack()
    {
        await using var engine = new LegendServerEngine(
            RepoFile("version-packs", "sample-classic", "manifest.json"),
            0,
            IPAddress.Loopback);
        string hash = engine.VersionPack.PackHash;
        string versionId = engine.VersionPack.Manifest.VersionId;

        var result = await engine.ReloadVersionPackAsync(
            Path.Combine(Path.GetTempPath(), $"legend-missing-{Guid.NewGuid():N}.json"));

        Assert.False(result.Ok);
        Assert.Contains("rolled back", result.Message);
        Assert.Equal(hash, engine.VersionPack.PackHash);
        Assert.Equal(versionId, engine.VersionPack.Manifest.VersionId);
    }

    private static string CopySampleClassic()
    {
        string source = Path.GetDirectoryName(RepoFile("version-packs", "sample-classic", "manifest.json"))!;
        string dest = Path.Combine(Path.GetTempPath(), "legend-xref-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, dest);
        return dest;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(dest, Path.GetFileName(directory)));
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
}
