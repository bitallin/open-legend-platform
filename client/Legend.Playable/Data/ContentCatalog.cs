using System.Text.Json;

namespace Legend.Playable.Data;

public sealed class SkillDef
{
    public string SkillId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Kind { get; init; } = "attack";
    public string School { get; init; } = "warrior";
    public int MpCost { get; init; }
    public int CooldownMs { get; init; }
    public int CastMs { get; init; }
    public int Range { get; init; } = 2;
    public bool RequiresLearn { get; init; }
    public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();

    public bool Supports(string classId)
        => Classes.Count == 0 || Classes.Any(c => string.Equals(c, classId, StringComparison.OrdinalIgnoreCase));
}

public static class ContentCatalog
{
    private static readonly Lazy<Catalog> Loaded = new(Load);

    public static IReadOnlyList<SkillDef> Skills => Loaded.Value.Skills;
    public static IReadOnlyDictionary<string, string> ItemNames => Loaded.Value.ItemNames;
    public static int ItemWeight(string itemId)
        => Loaded.Value.Weights.TryGetValue(itemId, out int w) ? w : 0;

    public static int BagWeight(IEnumerable<(string ItemId, int Count)> items)
    {
        int total = 0;
        foreach (var (id, count) in items)
            total += ItemWeight(id) * Math.Max(0, count);
        return total;
    }

    public static SkillDef? FindSkill(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Skills.FirstOrDefault(s => string.Equals(s.SkillId, id, StringComparison.OrdinalIgnoreCase));

    public static string ItemName(string itemId)
        => ItemNames.TryGetValue(itemId, out var name) ? name : itemId;

    public static string UnlockItemFor(string appearanceId)
        => Loaded.Value.Unlocks.TryGetValue(appearanceId, out var item) ? item : "";

    /// <summary>Sprite layer id for an equipped item. Empty when the item does not change the paperdoll.</summary>
    public static string Paperdoll(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return "";
        if (Loaded.Value.Paperdolls.TryGetValue(itemId, out var layer) && layer.Length > 0)
            return layer;
        string id = itemId.ToLowerInvariant();
        if (id.Contains("blade")) return "wep_blade";
        if (id.Contains("iron_sword")) return "wep_iron";
        if (id.Contains("sword") || id.Contains("staff")) return id.Contains("staff") ? "wep_staff" : "wep_sword";
        if (id.Contains("helmet") || id.Contains("helm")) return id.Contains("iron") ? "helm_iron" : "helm_cloth";
        if (id.Contains("armor") || id.Contains("robe") || id.Contains("mail"))
            return id.Contains("plate") || id.Contains("iron") ? "arm_plate" : id.Contains("leather") ? "arm_leather" : "arm_cloth";
        return "";
    }

    public static IReadOnlyList<string> DefaultBar(string? classId)
    {
        return (classId ?? "").ToLowerInvariant() switch
        {
            "mage" => new[] { "arcane_bolt", "repulse_ring", "lightning", "fire_wall", "fast_beam", "burst_flame", "blink", "mana_shield" },
            "taoist" => new[] { "spirit_hit", "heal", "soul_charm", "poison_charm", "holy_armor", "ghost_shield", "summon_bone", "hide" },
            _ => new[] { "basic_slash", "power_strike", "thrust_slash", "half_moon", "fatal_strike", "rush", "basic_slash", "power_strike" }
        };
    }

    public static readonly string[] DefaultPotions =
    {
        "item_hp_potion", "item_hp_potion", "item_mp_potion", "item_mp_potion", "item_dried_meat", "item_hp_potion"
    };

    private static Catalog Load()
    {
        var asm = typeof(ContentCatalog).Assembly;
        var skills = new List<SkillDef>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unlocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paperdolls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Read(asm, "configs.skills.json", json =>
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string id = Str(el, "skillId");
                if (id.Length == 0)
                    continue;
                var classes = new List<string>();
                if (el.TryGetProperty("allowedClasses", out var arr))
                {
                    foreach (var c in arr.EnumerateArray())
                    {
                        string? v = c.GetString();
                        if (!string.IsNullOrWhiteSpace(v))
                            classes.Add(v);
                    }
                }
                string school = classes.Count == 1 ? classes[0] : classes.FirstOrDefault() ?? "warrior";
                skills.Add(new SkillDef
                {
                    SkillId = id,
                    DisplayName = Str(el, "displayName", id),
                    Kind = Str(el, "kind", "attack"),
                    School = school,
                    MpCost = Int(el, "mpCost"),
                    CooldownMs = Int(el, "cooldownMs", 600),
                    CastMs = Int(el, "castTimeMs"),
                    Range = Int(el, "range", 2),
                    RequiresLearn = el.TryGetProperty("requiresLearn", out var learn) && learn.ValueKind == JsonValueKind.True,
                    Classes = classes
                });
            }
        });
        Read(asm, "configs.items.json", json =>
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items))
                return;
            foreach (var el in items.EnumerateArray())
            {
                string id = Str(el, "itemId");
                if (id.Length == 0)
                    continue;
                names[id] = Str(el, "displayName", id);
                weights[id] = Int(el, "weight");
                string doll = Str(el, "paperdoll");
                if (doll.Length > 0)
                    paperdolls[id] = doll;
                foreach (var key in new[] { "unlockMountId", "unlockFashionId", "unlockPetId" })
                {
                    string target = Str(el, key);
                    if (target.Length > 0)
                        unlocks[target] = id;
                }
            }
        });
        return new Catalog(skills, names, weights, unlocks, paperdolls);
    }

    private static void Read(System.Reflection.Assembly asm, string name, Action<string> use)
    {
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null)
            return;
        using var reader = new StreamReader(stream);
        use(reader.ReadToEnd());
    }

    private static string Str(JsonElement el, string name, string fallback = "")
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static int Int(JsonElement el, string name, int fallback = 0)
        => el.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) ? n : fallback;

    private sealed record Catalog(
        IReadOnlyList<SkillDef> Skills,
        IReadOnlyDictionary<string, string> ItemNames,
        IReadOnlyDictionary<string, int> Weights,
        IReadOnlyDictionary<string, string> Unlocks,
        IReadOnlyDictionary<string, string> Paperdolls);
}
