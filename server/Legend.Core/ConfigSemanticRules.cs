using System.Text.Json;
using Legend.Contracts;

namespace Legend.Core;

/// <summary>
/// Constraints that a single document can decide, but a flat schema cannot
/// (coordinates versus that document's width, duplicate ids).
/// Cross-file references stay in <see cref="VersionPackLoader"/>.
/// </summary>
public static class ConfigSemanticRules
{
    public static void Apply(string schemaId, JsonElement root, List<string> errors)
    {
        if (errors.Count > 0 || root.ValueKind != JsonValueKind.Object)
            return;

        if (schemaId == ConfigSchemaIds.Map)
            ApplyMap(root, errors);
        else if (schemaId == ConfigSchemaIds.Items)
            ApplyItems(root, errors);
    }

    private static void ApplyMap(JsonElement root, List<string> errors)
    {
        int width = root.GetProperty("width").GetInt32();
        int height = root.GetProperty("height").GetInt32();
        int spawnX = root.GetProperty("spawnX").GetInt32();
        int spawnY = root.GetProperty("spawnY").GetInt32();
        if (!Inside(spawnX, spawnY, width, height))
            errors.Add($"$.spawnX/spawnY: ({spawnX},{spawnY}) is outside {width}x{height}.");

        if (root.TryGetProperty("blockedCells", out var blocked) && blocked.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var cell in blocked.EnumerateArray())
            {
                int x = cell.GetProperty("x").GetInt32();
                int y = cell.GetProperty("y").GetInt32();
                if (!Inside(x, y, width, height))
                    errors.Add($"$.blockedCells[{index}]: ({x},{y}) is outside {width}x{height}.");
                index++;
            }
        }

        if (root.TryGetProperty("safeZones", out var zones) && zones.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var zone in zones.EnumerateArray())
            {
                CheckCorner(errors, zone, "x1", "y1", width, height, $"$.safeZones[{index}]");
                CheckCorner(errors, zone, "x2", "y2", width, height, $"$.safeZones[{index}]");
                index++;
            }
        }

        if (root.TryGetProperty("portals", out var portals) && portals.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var portal in portals.EnumerateArray())
            {
                int x = portal.GetProperty("x").GetInt32();
                int y = portal.GetProperty("y").GetInt32();
                if (!Inside(x, y, width, height))
                    errors.Add($"$.portals[{index}]: ({x},{y}) is outside {width}x{height}.");
                index++;
            }
        }
    }

    private static void ApplyItems(JsonElement root, List<string> errors)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            string itemId = item.GetProperty("itemId").GetString() ?? "";
            if (!seenIds.Add(itemId))
                errors.Add($"$.items[{index}].itemId: duplicate '{itemId}'.");

            string kind = item.GetProperty("kind").GetString() ?? "";
            string slot = item.TryGetProperty("slot", out var slotEl) && slotEl.ValueKind == JsonValueKind.String
                ? slotEl.GetString() ?? ""
                : "";
            if (kind == "equipment" && slot.Length == 0)
                errors.Add($"$.items[{index}].slot: equipment '{itemId}' requires a slot.");
            index++;
        }

        if (!root.TryGetProperty("enhanceRules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return;

        var seenLevels = new HashSet<int>();
        int ruleIndex = 0;
        foreach (var rule in rules.EnumerateArray())
        {
            int level = rule.GetProperty("targetLevel").GetInt32();
            if (!seenLevels.Add(level))
                errors.Add($"$.enhanceRules[{ruleIndex}].targetLevel: duplicate {level}.");
            ruleIndex++;
        }
    }

    private static void CheckCorner(List<string> errors, JsonElement zone, string xName, string yName, int width, int height, string path)
    {
        int x = zone.GetProperty(xName).GetInt32();
        int y = zone.GetProperty(yName).GetInt32();
        if (!Inside(x, y, width, height))
            errors.Add($"{path}: {xName}/{yName} ({x},{y}) is outside {width}x{height}.");
    }

    private static bool Inside(int x, int y, int width, int height) =>
        x >= 0 && y >= 0 && x < width && y < height;
}
