namespace Legend.Contracts;

/// <summary>
/// Schema ids understood by <see cref="IConfigSchemaValidator"/>.
/// New ids are added with a schema file and a loader call site, not by overloading these strings.
/// </summary>
public static class ConfigSchemaIds
{
    public const string Map = "map";
    public const string Items = "items";
    public const string Skills = "skills";
    public const string Monsters = "monsters";
    public const string LootTables = "lootTables";
    public const string Npcs = "npcs";
    public const string Shops = "shops";
    public const string Quests = "quests";
    public const string Levels = "levels";
    public const string Guild = "guild";
    public const string Trade = "trade";
    public const string Currencies = "currencies";
    public const string Recycle = "recycle";
    public const string Talents = "talents";
    public const string Internal = "internal";
    public const string EquipmentSlots = "equipmentSlots";
    public const string Artifacts = "artifacts";
    public const string Heroes = "heroes";
    public const string Combos = "combos";
    public const string ClientAssist = "client_assist";
    public const string PickupFilters = "pickup_filters";
}

/// <summary>
/// Validates one JSON document before it is deserialized into a version-pack model.
/// Failures are process errors (startup abort or hot-reload rollback), not player-facing protocol codes.
/// </summary>
public interface IConfigSchemaValidator
{
    /// <summary>
    /// Throws <see cref="ConfigSchemaException"/> when <paramref name="json"/> does not satisfy <paramref name="schemaId"/>.
    /// </summary>
    void Validate(string schemaId, string json, string sourceName);
}

/// <summary>
/// Structured config failure. <see cref="InvalidDataException"/> is sealed, so this is a sibling type.
/// Hot reload already catches <see cref="Exception"/> and rolls back. Host startup catches this type by name.
/// </summary>
public sealed class ConfigSchemaException : Exception
{
    public string SchemaId { get; }
    public string SourceName { get; }
    public IReadOnlyList<string> Errors { get; }

    public ConfigSchemaException(string schemaId, string sourceName, IReadOnlyList<string> errors)
        : base(Format(schemaId, sourceName, errors))
    {
        SchemaId = schemaId ?? string.Empty;
        SourceName = sourceName ?? string.Empty;
        Errors = errors ?? Array.Empty<string>();
    }

    private static string Format(string schemaId, string sourceName, IReadOnlyList<string>? errors)
    {
        string detail = errors is null || errors.Count == 0
            ? "invalid"
            : string.Join("; ", errors);
        string where = string.IsNullOrWhiteSpace(sourceName) ? schemaId : sourceName;
        return $"Config schema '{schemaId}' rejected {where}: {detail}";
    }
}
