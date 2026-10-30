using System.Text.Json;
using Legend.Contracts;
using Legend.Core;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Legend.MapValidate <map.json|manifest.json> [...]");
    return 1;
}

int errors = 0;
foreach (string path in args)
{
    string full = Path.GetFullPath(path);
    if (!File.Exists(full))
    {
        Console.Error.WriteLine($"MISSING: {full}");
        errors++;
        continue;
    }

    if (Path.GetFileName(full).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var pack = new VersionPackLoader().LoadFromManifest(full);
            Console.WriteLine($"OK manifest {pack.Manifest.VersionId}: maps={pack.Maps.Count} classes={pack.Classes.Count} hash={pack.PackHash}");
            foreach (var map in pack.Maps.Values)
                errors += ValidateMap(map, full);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL manifest {full}: {ex.Message}");
            errors++;
        }
        continue;
    }

    try
    {
        string json = File.ReadAllText(full);
        ConfigSchemaValidator.Instance.Validate(ConfigSchemaIds.Map, json, full);
        var map = JsonSerializer.Deserialize<MapConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidDataException("null map");
        map.EnsureBlockedCache();
        errors += ValidateMap(map, full);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {full}: {ex.Message}");
        errors++;
    }
}

return errors == 0 ? 0 : 2;

static int ValidateMap(MapConfig map, string source)
{
    int err = 0;
    if (map.Width <= 0 || map.Height <= 0)
    {
        Console.Error.WriteLine($"FAIL {map.MapId}: invalid size {map.Width}x{map.Height}");
        err++;
    }
    if (map.IsBlocked(map.SpawnX, map.SpawnY))
    {
        Console.Error.WriteLine($"FAIL {map.MapId}: spawn ({map.SpawnX},{map.SpawnY}) is blocked");
        err++;
    }
    foreach (var p in map.Portals)
    {
        if (map.IsBlocked(p.X, p.Y))
        {
            Console.Error.WriteLine($"FAIL {map.MapId}: portal {p.PortalId} at ({p.X},{p.Y}) is blocked");
            err++;
        }
        if (string.IsNullOrWhiteSpace(p.TargetMapId))
        {
            Console.Error.WriteLine($"FAIL {map.MapId}: portal {p.PortalId} missing targetMapId");
            err++;
        }
    }
    foreach (var z in map.SafeZones)
    {
        if (z.X1 == z.X2 && z.Y1 == z.Y2)
        {
            Console.Error.WriteLine($"WARN {map.MapId}: safe zone '{z.Name}' is a single cell");
        }
    }
    int blocked = 0;
    for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.IsBlocked(x, y)) blocked++;

    if (err == 0)
        Console.WriteLine($"OK map {map.MapId} ({map.MapName}) {map.Width}x{map.Height} blocked={blocked} safeZones={map.SafeZones.Count} portals={map.Portals.Count} src={source}");
    return err;
}
