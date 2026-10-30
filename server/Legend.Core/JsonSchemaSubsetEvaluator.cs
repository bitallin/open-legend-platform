using System.Text.Json;
using System.Text.RegularExpressions;

namespace Legend.Core;

/// <summary>
/// JSON Schema draft 2020-12 subset used by version-pack documents.
/// Unknown keywords fail at schema load so a later author cannot think <c>$ref</c> or <c>allOf</c> is enforced.
/// </summary>
public static class JsonSchemaSubsetEvaluator
{
    public const int MaxErrors = 32;

    private static readonly HashSet<string> IgnoredKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "title", "description", "$comment"
    };

    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "items",
        "enum", "pattern", "minLength", "maxLength", "minimum", "maximum",
        "minItems", "maxItems"
    };

    public static void EnsureSupported(JsonElement schema, string path = "#")
    {
        if (schema.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path}: schema must be a JSON object.");

        foreach (var prop in schema.EnumerateObject())
        {
            if (IgnoredKeywords.Contains(prop.Name))
                continue;
            if (!SupportedKeywords.Contains(prop.Name))
                throw new InvalidDataException($"{path}: unsupported JSON Schema keyword '{prop.Name}'.");
        }

        if (schema.TryGetProperty("type", out var typeEl) && typeEl.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{path}: type must be a single string in this subset.");

        if (schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"{path}: additionalProperties must be a boolean in this subset.");
        }

        if (schema.TryGetProperty("pattern", out var patternEl))
        {
            if (patternEl.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{path}: pattern must be a string.");
            try
            {
                _ = new Regex(patternEl.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException($"{path}: invalid pattern ({ex.Message}).");
            }
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{path}: properties must be an object.");
            foreach (var property in properties.EnumerateObject())
                EnsureSupported(property.Value, $"{path}/properties/{property.Name}");
        }

        if (schema.TryGetProperty("items", out var items))
            EnsureSupported(items, $"{path}/items");
    }

    public static List<string> Evaluate(JsonElement schema, JsonElement instance)
    {
        var errors = new List<string>(8);
        EvaluateInto(schema, instance, "$", errors);
        return errors;
    }

    private static void EvaluateInto(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        if (errors.Count >= MaxErrors)
            return;

        if (schema.TryGetProperty("type", out var typeEl))
        {
            string typeName = typeEl.GetString() ?? "";
            if (!MatchesType(instance, typeName))
            {
                Add(errors, $"{path}: expected {typeName}.");
                return;
            }
        }

        if (schema.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
        {
            bool matched = false;
            foreach (var allowed in enumEl.EnumerateArray())
            {
                if (EnumEquals(instance, allowed))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
                Add(errors, $"{path}: value is not in the allowed set.");
        }

        if (instance.ValueKind == JsonValueKind.String)
            EvaluateString(schema, instance.GetString() ?? "", path, errors);

        if (instance.ValueKind == JsonValueKind.Number)
            EvaluateNumber(schema, instance, path, errors);

        if (instance.ValueKind == JsonValueKind.Array)
            EvaluateArray(schema, instance, path, errors);

        if (instance.ValueKind == JsonValueKind.Object)
            EvaluateObject(schema, instance, path, errors);
    }

    private static void EvaluateString(JsonElement schema, string value, string path, List<string> errors)
    {
        if (schema.TryGetProperty("minLength", out var minLength) && value.Length < minLength.GetInt32())
            Add(errors, $"{path}: shorter than {minLength.GetInt32()}.");
        if (schema.TryGetProperty("maxLength", out var maxLength) && value.Length > maxLength.GetInt32())
            Add(errors, $"{path}: longer than {maxLength.GetInt32()}.");
        if (schema.TryGetProperty("pattern", out var patternEl))
        {
            var regex = new Regex(patternEl.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            if (!regex.IsMatch(value))
                Add(errors, $"{path}: does not match pattern.");
        }
    }

    private static void EvaluateNumber(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        double actual = instance.GetDouble();
        if (schema.TryGetProperty("minimum", out var minimum) && actual < minimum.GetDouble())
            Add(errors, $"{path}: below minimum {minimum.GetRawText()}.");
        if (schema.TryGetProperty("maximum", out var maximum) && actual > maximum.GetDouble())
            Add(errors, $"{path}: above maximum {maximum.GetRawText()}.");
    }

    private static void EvaluateArray(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        int count = instance.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var minItems) && count < minItems.GetInt32())
            Add(errors, $"{path}: fewer than {minItems.GetInt32()} items.");
        if (schema.TryGetProperty("maxItems", out var maxItems) && count > maxItems.GetInt32())
            Add(errors, $"{path}: more than {maxItems.GetInt32()} items.");

        if (!schema.TryGetProperty("items", out var itemSchema))
            return;

        int index = 0;
        foreach (var item in instance.EnumerateArray())
        {
            if (errors.Count >= MaxErrors)
                return;
            EvaluateInto(itemSchema, item, $"{path}[{index}]", errors);
            index++;
        }
    }

    private static void EvaluateObject(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        JsonElement properties = default;
        bool hasProperties = schema.TryGetProperty("properties", out properties)
            && properties.ValueKind == JsonValueKind.Object;
        if (hasProperties)
        {
            foreach (var property in properties.EnumerateObject())
                defined.Add(property.Name);
        }

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var nameEl in required.EnumerateArray())
            {
                string name = nameEl.GetString() ?? "";
                if (!instance.TryGetProperty(name, out _))
                    Add(errors, $"{Join(path, name)}: required.");
            }
        }

        bool rejectAdditional = schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False;

        foreach (var property in instance.EnumerateObject())
        {
            if (errors.Count >= MaxErrors)
                return;
            if (hasProperties && defined.Contains(property.Name))
            {
                EvaluateInto(properties.GetProperty(property.Name), property.Value, Join(path, property.Name), errors);
                continue;
            }
            if (rejectAdditional)
                Add(errors, $"{Join(path, property.Name)}: unknown property.");
        }
    }

    private static bool MatchesType(JsonElement instance, string typeName) => typeName switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => IsInteger(instance),
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static bool IsInteger(JsonElement instance)
    {
        if (instance.ValueKind != JsonValueKind.Number)
            return false;
        string raw = instance.GetRawText();
        if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E'))
            return false;
        return instance.TryGetInt64(out _);
    }

    private static bool EnumEquals(JsonElement instance, JsonElement allowed)
    {
        if (instance.ValueKind != allowed.ValueKind)
            return false;
        return instance.ValueKind switch
        {
            JsonValueKind.String => instance.GetString() == allowed.GetString(),
            JsonValueKind.Number => instance.GetRawText() == allowed.GetRawText(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => instance.GetRawText() == allowed.GetRawText()
        };
    }

    private static string Join(string path, string property) =>
        path == "$" ? "$." + property : path + "." + property;

    private static void Add(List<string> errors, string message)
    {
        if (errors.Count < MaxErrors)
            errors.Add(message);
    }
}
