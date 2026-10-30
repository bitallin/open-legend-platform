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
            [ConfigSchemaIds.Items] = LoadEmbedded("Legend.Core.Schemas.items.schema.json")
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
