using System.Text.Json;

namespace Legend.Playable.World;

public sealed class MapLayout
{
    public string MapId { get; init; } = "unknown";
    public string MapName { get; init; } = "未知地图";
    public int Width { get; init; } = 64;
    public int Height { get; init; } = 64;
    public HashSet<(int X, int Y)> Blocked { get; init; } = new();
    public List<(int X1, int Y1, int X2, int Y2, string Name)> SafeZones { get; init; } = new();
    public List<(int X, int Y, string Id)> Portals { get; init; } = new();
    /// <summary>Hand-authored ground tile overrides (path/plaza/pond). Empty = procedural only.</summary>
    public Dictionary<(int X, int Y), string> GroundOverrides { get; init; } = new();
    /// <summary>Hand-authored decor props (lamp/fence/bush/rocks). Empty = hash scatter fallback.</summary>
    public Dictionary<(int X, int Y), string> DecorOverrides { get; init; } = new();
    public bool HasAuthoredDecor => DecorOverrides.Count > 0;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public bool IsBlocked(int x, int y) => !InBounds(x, y) || Blocked.Contains((x, y));

    public bool IsSafe(int x, int y)
    {
        foreach (var z in SafeZones)
        {
            if (x >= z.X1 && x <= z.X2 && y >= z.Y1 && y <= z.Y2)
                return true;
        }
        return false;
    }

    public bool IsPortal(int x, int y) => Portals.Any(p => p.X == x && p.Y == y);

    public static MapLayout Fallback(string mapId) => new()
    {
        MapId = string.IsNullOrWhiteSpace(mapId) ? "unknown" : mapId,
        MapName = string.IsNullOrWhiteSpace(mapId) ? "未知地图" : mapId,
        Width = 64,
        Height = 64
    };
}

public static class MapCatalog
{
    private static readonly Dictionary<string, MapLayout> Maps = Load();

    public static IReadOnlyCollection<string> Ids => Maps.Keys;

    public static MapLayout Get(string? mapId)
    {
        if (!string.IsNullOrWhiteSpace(mapId) && Maps.TryGetValue(mapId, out var map))
            return map;
        return MapLayout.Fallback(mapId ?? "");
    }

    private static Dictionary<string, MapLayout> Load()
    {
        var dict = new Dictionary<string, MapLayout>(StringComparer.OrdinalIgnoreCase);
        var asm = typeof(MapCatalog).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("maps.", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
                continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null)
                continue;
            using var reader = new StreamReader(stream);
            var layout = Parse(reader.ReadToEnd());
            dict[layout.MapId] = layout;
        }
        return dict;
    }

    public static MapLayout Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string id = root.TryGetProperty("mapId", out var idEl) ? idEl.GetString() ?? "unknown" : "unknown";
        string mapName = root.TryGetProperty("mapName", out var nameEl) ? nameEl.GetString() ?? id : id;
        int width = root.TryGetProperty("width", out var wEl) ? wEl.GetInt32() : 64;
        int height = root.TryGetProperty("height", out var hEl) ? hEl.GetInt32() : 64;
        var blocked = new HashSet<(int, int)>();
        if (root.TryGetProperty("blockedCells", out var blocks))
        {
            foreach (var cell in blocks.EnumerateArray())
                blocked.Add((cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32()));
        }
        var safes = new List<(int, int, int, int, string)>();
        if (root.TryGetProperty("safeZones", out var zones))
        {
            foreach (var z in zones.EnumerateArray())
            {
                string zn = z.TryGetProperty("name", out var znEl) ? znEl.GetString() ?? "" : "";
                safes.Add((z.GetProperty("x1").GetInt32(), z.GetProperty("y1").GetInt32(),
                    z.GetProperty("x2").GetInt32(), z.GetProperty("y2").GetInt32(), zn));
            }
        }
        var portals = new List<(int, int, string)>();
        if (root.TryGetProperty("portals", out var portalEl))
        {
            foreach (var p in portalEl.EnumerateArray())
            {
                string pid = p.TryGetProperty("portalId", out var pidEl) ? pidEl.GetString() ?? "" : "";
                portals.Add((p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32(), pid));
            }
        }
        var ground = new Dictionary<(int, int), string>();
        if (root.TryGetProperty("clientGround", out var groundEl))
        {
            foreach (var g in groundEl.EnumerateArray())
            {
                string kind = g.TryGetProperty("kind", out var kEl) ? kEl.GetString() ?? "" : "";
                if (kind.Length == 0) continue;
                ground[(g.GetProperty("x").GetInt32(), g.GetProperty("y").GetInt32())] = kind;
            }
        }
        var decor = new Dictionary<(int, int), string>();
        if (root.TryGetProperty("clientDecor", out var decorEl))
        {
            foreach (var d in decorEl.EnumerateArray())
            {
                string kind = d.TryGetProperty("kind", out var kEl) ? kEl.GetString() ?? "" : "";
                if (kind.Length == 0) continue;
                decor[(d.GetProperty("x").GetInt32(), d.GetProperty("y").GetInt32())] = kind;
            }
        }
        return new MapLayout
        {
            MapId = id,
            MapName = mapName,
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Blocked = blocked,
            SafeZones = safes,
            Portals = portals,
            GroundOverrides = ground,
            DecorOverrides = decor
        };
    }
}
