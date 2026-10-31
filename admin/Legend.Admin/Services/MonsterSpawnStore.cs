using System.Text.Json.Nodes;

namespace Legend.Admin.Services;

/// <summary>
/// CRUD for monsters.json "spawns" array (mapId + monsterId + x/y + count).
/// Host loads these as MonsterSpawnPoint; keep fields aligned with monsters.schema.json.
/// </summary>
public sealed class MonsterSpawnStore(VersionPackService packs, AuditService audit)
{
    public const string RelativePath = "configs/monsters.json";

    public async Task<(bool ok, string message, List<SpawnRow> rows)> ListAsync(
        string packId, string? mapIdFilter, CancellationToken ct = default)
    {
        var (ok, msg, root) = await LoadRootAsync(packId, ct);
        if (!ok || root is null) return (false, msg, new());
        var spawns = EnsureSpawns(root);
        var templates = FindTemplates(root);
        var nameById = templates
            .OfType<JsonObject>()
            .ToDictionary(
                t => JsonEntityStore.GetStr(t, "monsterId"),
                t => JsonEntityStore.GetStr(t, "displayName"),
                StringComparer.OrdinalIgnoreCase);
        var respawnById = templates
            .OfType<JsonObject>()
            .ToDictionary(
                t => JsonEntityStore.GetStr(t, "monsterId"),
                t => JsonEntityStore.GetStr(t, "respawnMs"),
                StringComparer.OrdinalIgnoreCase);

        var rows = new List<SpawnRow>();
        for (var i = 0; i < spawns.Count; i++)
        {
            if (spawns[i] is not JsonObject o) continue;
            var mapId = JsonEntityStore.GetStr(o, "mapId");
            if (!string.IsNullOrWhiteSpace(mapIdFilter) &&
                !string.Equals(mapId, mapIdFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            var mid = JsonEntityStore.GetStr(o, "monsterId");
            nameById.TryGetValue(mid, out var dn);
            respawnById.TryGetValue(mid, out var respawn);
            rows.Add(new SpawnRow(
                Index: i,
                MonsterId: mid,
                DisplayName: string.IsNullOrEmpty(dn) ? mid : dn!,
                MapId: mapId,
                X: ParseInt(o, "x"),
                Y: ParseInt(o, "y"),
                Count: Math.Max(1, ParseInt(o, "count", 1)),
                TemplateRespawnMs: respawn ?? ""));
        }
        return (true, "ok", rows);
    }

    public async Task<Dictionary<string, int>> CountByMapAsync(string packId, CancellationToken ct = default)
    {
        var (ok, _, rows) = await ListAsync(packId, null, ct);
        if (!ok) return new();
        return rows.GroupBy(r => r.MapId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(bool ok, string message)> AddAsync(
        string packId, string mapId, string monsterId, int x, int y, int count, string actor, string ip, CancellationToken ct = default)
    {
        var err = Validate(mapId, monsterId, x, y, count);
        if (err is not null) return (false, err);

        var (ok, msg, root) = await LoadRootAsync(packId, ct);
        if (!ok || root is null) return (false, msg);
        if (!TemplateExists(root, monsterId)) return (false, "怪物模板不存在：" + monsterId);

        var spawns = EnsureSpawns(root);
        spawns.Add(new JsonObject
        {
            ["monsterId"] = monsterId,
            ["mapId"] = mapId,
            ["x"] = x,
            ["y"] = y,
            ["count"] = count
        });
        return await SaveAsync(packId, root, actor, $"spawns.add:{mapId}/{monsterId}@{x},{y}", ip, ct);
    }

    public async Task<(bool ok, string message)> UpdateAsync(
        string packId, int index, string mapId, string monsterId, int x, int y, int count, string actor, string ip, CancellationToken ct = default)
    {
        var err = Validate(mapId, monsterId, x, y, count);
        if (err is not null) return (false, err);

        var (ok, msg, root) = await LoadRootAsync(packId, ct);
        if (!ok || root is null) return (false, msg);
        if (!TemplateExists(root, monsterId)) return (false, "怪物模板不存在：" + monsterId);

        var spawns = EnsureSpawns(root);
        if (index < 0 || index >= spawns.Count) return (false, "刷怪索引无效");
        spawns[index] = new JsonObject
        {
            ["monsterId"] = monsterId,
            ["mapId"] = mapId,
            ["x"] = x,
            ["y"] = y,
            ["count"] = count
        };
        return await SaveAsync(packId, root, actor, $"spawns.update:{index}:{mapId}/{monsterId}", ip, ct);
    }

    public async Task<(bool ok, string message)> DeleteAsync(
        string packId, int index, string actor, string ip, CancellationToken ct = default)
    {
        var (ok, msg, root) = await LoadRootAsync(packId, ct);
        if (!ok || root is null) return (false, msg);
        var spawns = EnsureSpawns(root);
        if (index < 0 || index >= spawns.Count) return (false, "刷怪索引无效");
        var snap = spawns[index]?.ToJsonString() ?? "";
        spawns.RemoveAt(index);
        return await SaveAsync(packId, root, actor, $"spawns.delete:{index}:{snap}", ip, ct);
    }

    private async Task<(bool ok, string message, JsonObject? root)> LoadRootAsync(string packId, CancellationToken ct)
    {
        var (ok, msg, content) = await packs.ReadAsync(packId, RelativePath, ct);
        if (!ok || content is null) return (false, msg == "not_found" ? "找不到 configs/monsters.json" : msg, null);
        try
        {
            var node = JsonNode.Parse(content);
            if (node is not JsonObject jo) return (false, "monsters.json 根须为对象", null);
            return (true, "ok", jo);
        }
        catch (Exception ex) { return (false, ex.Message, null); }
    }

    private async Task<(bool ok, string message)> SaveAsync(
        string packId, JsonObject root, string actor, string action, string ip, CancellationToken ct)
    {
        var json = root.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }) + "\n";
        var (wok, wmsg, _) = await packs.WriteAsync(packId, RelativePath, json, actor, action, ct);
        if (!wok) return (false, wmsg);
        await audit.WriteAsync(actor, action, $"{packId}/{RelativePath}", null, ip, ct);
        return (true, "已保存");
    }

    private static JsonArray EnsureSpawns(JsonObject root)
    {
        if (root["spawns"] is JsonArray arr) return arr;
        arr = new JsonArray();
        root["spawns"] = arr;
        return arr;
    }

    private static JsonArray FindTemplates(JsonObject root)
    {
        if (root["templates"] is JsonArray t) return t;
        if (root["monsters"] is JsonArray m) return m;
        return new JsonArray();
    }

    private static bool TemplateExists(JsonObject root, string monsterId)
    {
        foreach (var n in FindTemplates(root).OfType<JsonObject>())
        {
            if (string.Equals(JsonEntityStore.GetStr(n, "monsterId"), monsterId, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? Validate(string mapId, string monsterId, int x, int y, int count)
    {
        if (string.IsNullOrWhiteSpace(mapId) || !System.Text.RegularExpressions.Regex.IsMatch(mapId, @"^[a-z][a-z0-9_]*$"))
            return "地图 ID 非法";
        if (string.IsNullOrWhiteSpace(monsterId) || !System.Text.RegularExpressions.Regex.IsMatch(monsterId, @"^[a-z][a-z0-9_]*$"))
            return "怪物 ID 非法";
        if (x < 0 || x > 511 || y < 0 || y > 511) return "坐标须在 0–511";
        if (count < 1 || count > 100) return "数量须在 1–100";
        return null;
    }

    private static int ParseInt(JsonObject o, string key, int fallback = 0)
    {
        var s = JsonEntityStore.GetStr(o, key);
        return int.TryParse(s, out var n) ? n : fallback;
    }

    public record SpawnRow(int Index, string MonsterId, string DisplayName, string MapId, int X, int Y, int Count, string TemplateRespawnMs);
}
