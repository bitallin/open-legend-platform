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
        if (errors.Count > 0)
            return;

        switch (schemaId)
        {
            case ConfigSchemaIds.Map when root.ValueKind == JsonValueKind.Object:
                ApplyMap(root, errors);
                break;
            case ConfigSchemaIds.Items when root.ValueKind == JsonValueKind.Object:
                ApplyItems(root, errors);
                break;
            case ConfigSchemaIds.Skills when root.ValueKind == JsonValueKind.Array:
                RequireUnique(root, "skillId", "$", errors);
                break;
            case ConfigSchemaIds.Monsters when root.ValueKind == JsonValueKind.Object:
                if (root.TryGetProperty("templates", out var templates))
                    RequireUnique(templates, "monsterId", "$.templates", errors);
                break;
            case ConfigSchemaIds.LootTables when root.ValueKind == JsonValueKind.Array:
                RequireUnique(root, "lootTableId", "$", errors);
                ApplyLootCounts(root, errors);
                break;
            case ConfigSchemaIds.Npcs when root.ValueKind == JsonValueKind.Array:
                RequireUnique(root, "npcId", "$", errors);
                break;
            case ConfigSchemaIds.Shops when root.ValueKind == JsonValueKind.Array:
                RequireUnique(root, "shopId", "$", errors);
                break;
            case ConfigSchemaIds.Quests when root.ValueKind == JsonValueKind.Array:
                RequireUnique(root, "questId", "$", errors);
                break;
            case ConfigSchemaIds.Levels when root.ValueKind == JsonValueKind.Object:
                ApplyLevelEntries(root, errors);
                break;
            case ConfigSchemaIds.Trade when root.ValueKind == JsonValueKind.Object:
                ApplyTradePrices(root, errors);
                break;
        }
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

    private static void ApplyLootCounts(JsonElement tables, List<string> errors)
    {
        int tableIndex = 0;
        foreach (var table in tables.EnumerateArray())
        {
            if (!table.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                tableIndex++;
                continue;
            }

            int entryIndex = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                int min = entry.TryGetProperty("countMin", out var minEl) ? minEl.GetInt32() : 1;
                int max = entry.TryGetProperty("countMax", out var maxEl) ? maxEl.GetInt32() : 1;
                if (max < min)
                    errors.Add($"$[{tableIndex}].entries[{entryIndex}]: countMax {max} is below countMin {min}.");
                entryIndex++;
            }

            tableIndex++;
        }
    }

    private static void ApplyLevelEntries(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return;

        var seen = new HashSet<int>();
        int index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            int level = entry.GetProperty("level").GetInt32();
            if (!seen.Add(level))
                errors.Add($"$.entries[{index}].level: duplicate {level}.");
            index++;
        }
    }

    private static void ApplyTradePrices(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("minUnitPriceGold", out var minEl) || !root.TryGetProperty("maxUnitPriceGold", out var maxEl))
            return;
        if (maxEl.GetInt32() < minEl.GetInt32())
            errors.Add("$.maxUnitPriceGold: below minUnitPriceGold.");
    }

    private static void RequireUnique(JsonElement array, string idProperty, string path, List<string> errors)
    {
        if (array.ValueKind != JsonValueKind.Array)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(idProperty, out var idEl)
                && idEl.ValueKind == JsonValueKind.String)
            {
                string id = idEl.GetString() ?? "";
                if (id.Length > 0 && !seen.Add(id))
                    errors.Add($"{path}[{index}].{idProperty}: duplicate '{id}'.");
            }

            index++;
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
