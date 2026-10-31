using System.Text.Json;
using Legend.Contracts;

namespace Legend.Core;

public sealed class ConfigSchemaValidator : IConfigSchemaValidator
{
    public static ConfigSchemaValidator Instance { get; } = new();

    private readonly Dictionary<string, JsonDocument> _schemas;

    public ConfigSchemaValidator()
    {
        _schemas = new Dictionary<string, JsonDocument>(StringComparer.Ordinal)
        {
            [ConfigSchemaIds.Map] = LoadEmbedded("Legend.Core.Schemas.map.schema.json"),
            [ConfigSchemaIds.Items] = LoadEmbedded("Legend.Core.Schemas.items.schema.json"),
            [ConfigSchemaIds.Skills] = LoadEmbedded("Legend.Core.Schemas.skills.schema.json"),
            [ConfigSchemaIds.Monsters] = LoadEmbedded("Legend.Core.Schemas.monsters.schema.json"),
            [ConfigSchemaIds.LootTables] = LoadEmbedded("Legend.Core.Schemas.loot.schema.json"),
            [ConfigSchemaIds.Npcs] = LoadEmbedded("Legend.Core.Schemas.npcs.schema.json"),
            [ConfigSchemaIds.Shops] = LoadEmbedded("Legend.Core.Schemas.shops.schema.json"),
            [ConfigSchemaIds.Quests] = LoadEmbedded("Legend.Core.Schemas.quests.schema.json"),
            [ConfigSchemaIds.Levels] = LoadEmbedded("Legend.Core.Schemas.levels.schema.json"),
            [ConfigSchemaIds.Guild] = LoadEmbedded("Legend.Core.Schemas.guild.schema.json"),
            [ConfigSchemaIds.Trade] = LoadEmbedded("Legend.Core.Schemas.trade.schema.json"),
            [ConfigSchemaIds.Currencies] = LoadEmbedded("Legend.Core.Schemas.currencies.schema.json"),
            [ConfigSchemaIds.Recycle] = LoadEmbedded("Legend.Core.Schemas.recycle.schema.json")
        };

        foreach (var pair in _schemas)
            JsonSchemaSubsetEvaluator.EnsureSupported(pair.Value.RootElement, pair.Key);
    }

    public void Validate(string schemaId, string json, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(schemaId) || !_schemas.TryGetValue(schemaId, out var schema))
        {
            throw new ConfigSchemaException(
                schemaId ?? "",
                sourceName,
                new[] { $"Unknown schema id '{schemaId}'." });
        }

        JsonDocument instance;
        try
        {
            instance = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            throw new ConfigSchemaException(schemaId, sourceName, new[] { $"JSON parse error: {ex.Message}" });
        }

        using (instance)
        {
            var errors = JsonSchemaSubsetEvaluator.Evaluate(schema.RootElement, instance.RootElement);
            if (errors.Count == 0)
                ConfigSemanticRules.Apply(schemaId, instance.RootElement, errors);
            if (errors.Count > 0)
                throw new ConfigSchemaException(schemaId, sourceName, errors);
        }
    }

    private static JsonDocument LoadEmbedded(string resourceName)
    {
        var assembly = typeof(ConfigSchemaValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded schema '{resourceName}' is missing. Found: {string.Join(", ", assembly.GetManifestResourceNames())}");
        return JsonDocument.Parse(stream);
    }
}
