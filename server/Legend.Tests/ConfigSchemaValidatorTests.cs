using System.Text.Json;
using Legend.Contracts;
using Legend.Core;
using Legend.Host;

namespace Legend.Tests;

public class ConfigSchemaValidatorTests
{
    private readonly ConfigSchemaValidator _validator = new();

    [Fact]
    public void SampleClassicPackLoads()
    {
        var pack = new VersionPackLoader().LoadFromManifest(RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.True(pack.Maps.Count >= 8);
        Assert.Contains("item_hp_potion", pack.Items.Keys);
    }

    [Fact]
    public void Sample176MapPassesSchema()
    {
        string json = File.ReadAllText(RepoFile("version-packs", "sample-176", "maps", "novice_plain.json"));
        _validator.Validate(ConfigSchemaIds.Map, json, "sample-176/novice_plain.json");
    }

    [Fact]
    public void MapAllowsCommentsAndTrailingCommas()
    {
        const string json = """
            {
              "mapId": "tiny",
              "mapName": "Tiny",
              "width": 4,
              "height": 4,
              "spawnX": 1,
              "spawnY": 1,
            }
            """;
        _validator.Validate(ConfigSchemaIds.Map, json, "tiny.json");
    }

    [Fact]
    public void MapRejectsMissingId()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            _validator.Validate(ConfigSchemaIds.Map, """{"mapName":"x","width":4,"height":4,"spawnX":0,"spawnY":0}""", "bad.json"));
        Assert.Equal(ConfigSchemaIds.Map, ex.SchemaId);
        Assert.Contains("mapId", ex.Message);
    }

    [Fact]
    public void MapRejectsZeroWidth()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, Map(width: 0), "w.json"));
        Assert.Contains("width", ex.Message);
    }

    [Fact]
    public void MapRejectsUnknownProperty()
    {
        string json = Map().TrimEnd('}', ' ', '\n') + ",\"noHang\":true}";
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, json, "flag.json"));
        Assert.Contains("noHang", ex.Message);
    }

    [Fact]
    public void MapRejectsFractionalWidth()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, Map(widthLiteral: "8.5"), "frac.json"));
        Assert.Contains("width", ex.Message);
    }

    [Fact]
    public void MapRejectsSpawnOutsideBounds()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, Map(spawnX: 10, spawnY: 10), "spawn.json"));
        Assert.Contains("spawnX/spawnY", ex.Message);
    }

    [Fact]
    public void MapRejectsBlockedCellOutsideBounds()
    {
        string json = Map(extra: "\"blockedCells\":[{\"x\":1,\"y\":9}]");
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, json, "block.json"));
        Assert.Contains("blockedCells", ex.Message);
    }

    [Fact]
    public void MapRejectsSafeZoneOutsideBounds()
    {
        string json = Map(extra: "\"safeZones\":[{\"name\":\"edge\",\"x1\":0,\"y1\":0,\"x2\":3,\"y2\":9}]");
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, json, "zone.json"));
        Assert.Contains("safeZones", ex.Message);
    }

    [Fact]
    public void MapRejectsPortalOutsideBounds()
    {
        string json = Map(extra: "\"portals\":[{\"portalId\":\"to_x\",\"x\":9,\"y\":0,\"targetMapId\":\"other\",\"targetX\":0,\"targetY\":0}]");
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, json, "portal.json"));
        Assert.Contains("portals", ex.Message);
    }

    [Fact]
    public void ItemsRejectsUnknownKind()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            _validator.Validate(ConfigSchemaIds.Items, Items("""{"itemId":"item_a","displayName":"A","kind":"currency"}"""), "items.json"));
        Assert.Contains("kind", ex.Message);
    }

    [Fact]
    public void ItemsRejectsDuplicateId()
    {
        string body = """
            {"itemId":"item_a","displayName":"A","kind":"material"},
            {"itemId":"item_a","displayName":"B","kind":"material"}
            """;
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Items, Items(body), "dup.json"));
        Assert.Contains("duplicate", ex.Message);
    }

    [Fact]
    public void ItemsRejectsEquipmentWithoutSlot()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            _validator.Validate(ConfigSchemaIds.Items, Items("""{"itemId":"item_blade","displayName":"Blade","kind":"equipment","slot":""}"""), "eq.json"));
        Assert.Contains("requires a slot", ex.Message);
    }

    [Fact]
    public void ItemsRejectsSuccessRateAboveOne()
    {
        string json = """{"items":[{"itemId":"item_a","displayName":"A","kind":"material"}],"enhanceRules":[{"targetLevel":1,"costGold":1,"successRate":1.5}]}""";
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Items, json, "rate.json"));
        Assert.Contains("successRate", ex.Message);
    }

    [Fact]
    public void ItemsRejectsDuplicateEnhanceLevel()
    {
        string json = """
            {"items":[{"itemId":"item_a","displayName":"A","kind":"material"}],
             "enhanceRules":[
               {"targetLevel":1,"costGold":1,"successRate":1},
               {"targetLevel":1,"costGold":2,"successRate":0.5}
             ]}
            """;
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Items, json, "enh.json"));
        Assert.Contains("duplicate 1", ex.Message);
    }

    [Fact]
    public void ItemsRejectsEmptyCatalog()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Items, """{"items":[]}""", "empty.json"));
        Assert.Contains("items", ex.Message);
    }

    [Fact]
    public void UnknownSchemaIdFails()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate("skills", "{}", "skills.json"));
        Assert.Contains("Unknown schema", ex.Message);
    }

    [Fact]
    public void InvalidJsonFails()
    {
        var ex = Assert.Throws<ConfigSchemaException>(() => _validator.Validate(ConfigSchemaIds.Map, "{", "broken.json"));
        Assert.Contains("JSON parse error", ex.Message);
    }

    [Fact]
    public void UnsupportedKeywordFailsAtSchemaLoad()
    {
        using var doc = JsonDocument.Parse("""{"type":"object","$ref":"#/defs/map"}""");
        var ex = Assert.Throws<InvalidDataException>(() => JsonSchemaSubsetEvaluator.EnsureSupported(doc.RootElement));
        Assert.Contains("$ref", ex.Message);
    }

    [Fact]
    public void SubsetEvaluatorReportsPatternAndLength()
    {
        using var schema = JsonDocument.Parse("""
            {"type":"object","additionalProperties":false,"properties":{
              "name":{"type":"string","minLength":2,"maxLength":4,"pattern":"^[a-z]+$"}
            }}
            """);
        using var tooLong = JsonDocument.Parse("""{"name":"ABCDE"}""");
        var errors = JsonSchemaSubsetEvaluator.Evaluate(schema.RootElement, tooLong.RootElement);
        Assert.Contains(errors, error => error.Contains("pattern") || error.Contains("longer"));
    }

    [Fact]
    public void AdditionalPropertiesTrueAllowsUnknownFields()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":true,"properties":{"id":{"type":"string"}}}""");
        using var instance = JsonDocument.Parse("""{"id":"a","extra":1}""");
        Assert.Empty(JsonSchemaSubsetEvaluator.Evaluate(schema.RootElement, instance.RootElement));
    }

    [Fact]
    public void BooleanTypeIsEnforced()
    {
        using var schema = JsonDocument.Parse("""{"type":"boolean"}""");
        using var instance = JsonDocument.Parse("0");
        var errors = JsonSchemaSubsetEvaluator.Evaluate(schema.RootElement, instance.RootElement);
        Assert.Contains(errors, error => error.Contains("boolean"));
    }

    [Fact]
    public void LoaderRejectsBadMapBeforeReturningPack()
    {
        string root = Path.Combine(Path.GetTempPath(), "legend-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        File.WriteAllText(Path.Combine(root, "maps", "bad.json"), """{"mapName":"no id"}""");
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "versionId": "bad-pack",
              "defaultMapFile": "maps/bad.json",
              "mapFiles": ["maps/bad.json"],
              "classesConfigFile": "configs/classes.json"
            }
            """);

        var ex = Assert.Throws<ConfigSchemaException>(() => new VersionPackLoader().LoadFromManifest(Path.Combine(root, "manifest.json")));
        Assert.Contains("bad.json", ex.SourceName);
    }

    [Fact]
    public async Task HostCreateAsyncFailsBeforeStorageWhenMapIsInvalid()
    {
        string root = Path.Combine(Path.GetTempPath(), "legend-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        File.WriteAllText(Path.Combine(root, "maps", "bad.json"), """{"width":4}""");
        string manifest = Path.Combine(root, "manifest.json");
        File.WriteAllText(manifest, """
            {
              "versionId": "bad-host",
              "defaultMapFile": "maps/bad.json",
              "mapFiles": ["maps/bad.json"],
              "classesConfigFile": "configs/classes.json"
            }
            """);

        await Assert.ThrowsAsync<ConfigSchemaException>(() => LegendServerEngine.CreateAsync(manifest));
    }

    private static string Map(int width = 4, int height = 4, int spawnX = 1, int spawnY = 1, string? widthLiteral = null, string? extra = null)
    {
        string widthText = widthLiteral ?? width.ToString();
        string tail = string.IsNullOrEmpty(extra) ? "" : "," + extra;
        return $$"""
            {"mapId":"tiny","mapName":"Tiny","width":{{widthText}},"height":{{height}},"spawnX":{{spawnX}},"spawnY":{{spawnY}}{{tail}}}
            """;
    }

    private static string Items(string itemObjects) => "{\"items\":[" + itemObjects + "]}";

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
