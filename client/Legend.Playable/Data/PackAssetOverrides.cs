using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Media.Imaging;

namespace Legend.Playable.Data;

/// <summary>
/// Optional overrides from a version-pack assets folder (admin uploads).
/// Falls back to embedded MirSheet/MirChrome when missing.
/// </summary>
public sealed class PackAssetOverrides
{
    public static PackAssetOverrides? Current { get; private set; }

    private readonly string _packRoot;
    private readonly Dictionary<string, string> _idToRelPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _monsterSprite = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _monsterIcon = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _itemIcon = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _itemDrop = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mapTile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mapMinimap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mapBackdrop = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _uiSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    private PackAssetOverrides(string packRoot) => _packRoot = packRoot;

    public static void TryInitialize(string? packRoot)
    {
        if (string.IsNullOrWhiteSpace(packRoot))
        {
            Current = null;
            return;
        }
        try
        {
            var root = Path.GetFullPath(packRoot);
            if (!Directory.Exists(root))
            {
                Current = null;
                return;
            }
            var ov = new PackAssetOverrides(root);
            ov.LoadManifest();
            ov.LoadLooks();
            ov.LoadUiSkin();
            Current = ov;
        }
        catch
        {
            Current = null;
        }
    }

    public Bitmap? GetByAssetId(string? assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return null;
        return _cache.GetOrAdd(assetId, id =>
        {
            if (!_idToRelPath.TryGetValue(id, out var rel)) return null;
            var abs = Path.Combine(_packRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(abs)) return null;
            try { return new Bitmap(abs); }
            catch { return null; }
        });
    }

    public Bitmap? MonsterSprite(string? monsterId)
        => monsterId is not null && _monsterSprite.TryGetValue(monsterId, out var id) ? GetByAssetId(id) : null;

    public Bitmap? MonsterIcon(string? monsterId)
        => monsterId is not null && _monsterIcon.TryGetValue(monsterId, out var id) ? GetByAssetId(id) : null;

    public Bitmap? ItemIcon(string? itemId)
        => itemId is not null && _itemIcon.TryGetValue(itemId, out var id) ? GetByAssetId(id) : null;

    public Bitmap? ItemDrop(string? itemId)
        => itemId is not null && _itemDrop.TryGetValue(itemId, out var id) ? GetByAssetId(id) : null;

    public Bitmap? MapTile(string? mapId)
        => mapId is not null && _mapTile.TryGetValue(mapId, out var id) ? GetByAssetId(id) : null;

    public Bitmap? MapMinimap(string? mapId)
        => mapId is not null && _mapMinimap.TryGetValue(mapId, out var id) ? GetByAssetId(id) : null;

    public Bitmap? MapBackdrop(string? mapId)
        => mapId is not null && _mapBackdrop.TryGetValue(mapId, out var id) ? GetByAssetId(id) : null;

    public Bitmap? UiSlot(string slot)
        => _uiSlots.TryGetValue(slot, out var id) ? GetByAssetId(id) : null;

    private void LoadManifest()
    {
        var path = Path.Combine(_packRoot, "configs", "assets_manifest.json");
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("assets", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var el in arr.EnumerateArray())
        {
            var id = Str(el, "id");
            var rel = Str(el, "path");
            if (id.Length > 0 && rel.Length > 0)
                _idToRelPath[id] = rel;
        }
    }

    private void LoadLooks()
    {
        LoadMonsters();
        LoadItems();
        LoadMaps();
    }

    private void LoadMonsters()
    {
        var path = Path.Combine(_packRoot, "configs", "monsters.json");
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        JsonElement arr = default;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("templates", out arr)) { }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("monsters", out arr)) { }
        else return;
        if (arr.ValueKind != JsonValueKind.Array) return;
        foreach (var el in arr.EnumerateArray())
        {
            var id = Str(el, "monsterId");
            if (id.Length == 0 || !el.TryGetProperty("look", out var look) || look.ValueKind != JsonValueKind.Object) continue;
            var sprite = Str(look, "spriteAssetId");
            var icon = Str(look, "iconAssetId");
            if (sprite.Length > 0) _monsterSprite[id] = sprite;
            if (icon.Length > 0) _monsterIcon[id] = icon;
        }
    }

    private void LoadItems()
    {
        var path = Path.Combine(_packRoot, "configs", "items.json");
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (!root.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var el in arr.EnumerateArray())
        {
            var id = Str(el, "itemId");
            if (id.Length == 0) continue;
            var icon = Str(el, "iconAssetId");
            var drop = Str(el, "dropSpriteAssetId");
            if (icon.Length > 0) _itemIcon[id] = icon;
            if (drop.Length > 0) _itemDrop[id] = drop;
        }
    }

    private void LoadMaps()
    {
        var mapsDir = Path.Combine(_packRoot, "maps");
        if (!Directory.Exists(mapsDir)) return;
        foreach (var file in Directory.EnumerateFiles(mapsDir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var id = Str(root, "mapId");
                if (id.Length == 0) id = Path.GetFileNameWithoutExtension(file);
                if (!root.TryGetProperty("look", out var look) || look.ValueKind != JsonValueKind.Object) continue;
                var tile = Str(look, "tileAssetId");
                var mini = Str(look, "minimapAssetId");
                var back = Str(look, "backdropAssetId");
                if (tile.Length > 0) _mapTile[id] = tile;
                if (mini.Length > 0) _mapMinimap[id] = mini;
                if (back.Length > 0) _mapBackdrop[id] = back;
            }
            catch { /* ignore bad map */ }
        }
    }

    private void LoadUiSkin()
    {
        var path = Path.Combine(_packRoot, "configs", "ui_skin.json");
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in slots.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                var id = Str(prop.Value, "assetId");
                if (id.Length > 0) _uiSlots[prop.Name] = id;
            }
        }
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
}
