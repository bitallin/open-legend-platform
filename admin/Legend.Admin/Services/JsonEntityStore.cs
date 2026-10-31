using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Legend.Admin.Services;

/// <summary>
/// Load/save JSON entity arrays (or per-file maps) while preserving unknown fields.
/// </summary>
public sealed class JsonEntityStore(VersionPackService packs, AuditService audit)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Regex IdOk = new(@"^[a-zA-Z][a-zA-Z0-9_\-]{1,63}$", RegexOptions.Compiled);

    public bool IsValidId(string id) => !string.IsNullOrWhiteSpace(id) && IdOk.IsMatch(id);

    public async Task<(bool ok, string message, List<JsonObject> entities)> ListAsync(
        string packId, EntityDescriptor desc, CancellationToken ct = default)
    {
        if (desc.IsMapFiles)
            return await ListMapsAsync(packId, ct);

        var (ok, msg, content) = await packs.ReadAsync(packId, desc.RelativePath, ct);
        if (!ok || content is null) return (false, msg, new());
        try
        {
            var root = JsonNode.Parse(content);
            var arr = FindArray(root, desc.ArrayKeys);
            if (arr is null)
            {
                // root array
                if (root is JsonArray ra)
                    return (true, "ok", ra.OfType<JsonObject>().Select(CloneObj).ToList());
                return (true, "empty", new());
            }
            return (true, "ok", arr.OfType<JsonObject>().Select(CloneObj).ToList());
        }
        catch (Exception ex) { return (false, ex.Message, new()); }
    }

    public async Task<(bool ok, string message, JsonObject? entity)> GetAsync(
        string packId, EntityDescriptor desc, string id, CancellationToken ct = default)
    {
        if (desc.IsMapFiles)
        {
            var rel = $"maps/{id}.json";
            var (ok, msg, content) = await packs.ReadAsync(packId, rel, ct);
            if (!ok || content is null) return (false, msg, null);
            var node = JsonNode.Parse(content) as JsonObject;
            return node is null ? (false, "invalid_map_json", null) : (true, "ok", CloneObj(node));
        }

        var (lok, lmsg, list) = await ListAsync(packId, desc, ct);
        if (!lok) return (false, lmsg, null);
        var hit = list.FirstOrDefault(e => string.Equals(GetStr(e, desc.IdProperty), id, StringComparison.OrdinalIgnoreCase));
        return hit is null ? (false, "not_found", null) : (true, "ok", hit);
    }

    public async Task<(bool ok, string message)> UpsertAsync(
        string packId, EntityDescriptor desc, JsonObject entity, string actor, string ip, bool isCreate, CancellationToken ct = default)
    {
        var id = GetStr(entity, desc.IdProperty);
        if (!IsValidId(id)) return (false, "ID 须以字母开头，仅含字母数字_-，长度 2–64");

        if (desc.IsMapFiles)
            return await UpsertMapAsync(packId, desc, entity, actor, ip, isCreate, ct);

        var (ok, msg, content) = await packs.ReadAsync(packId, desc.RelativePath, ct);
        JsonNode root;
        JsonArray arr;
        if (!ok || content is null)
        {
            // create new file with preferred key
            var key = desc.ArrayKeys.FirstOrDefault() ?? "templates";
            root = new JsonObject { [key] = new JsonArray() };
            arr = (JsonArray)((JsonObject)root)[key]!;
        }
        else
        {
            root = JsonNode.Parse(content) ?? new JsonObject();
            var found = FindArray(root, desc.ArrayKeys);
            if (found is null)
            {
                if (root is JsonArray ra) { arr = ra; }
                else
                {
                    var key = desc.ArrayKeys.FirstOrDefault() ?? "templates";
                    if (root is not JsonObject jo) return (false, "unsupported_root");
                    arr = new JsonArray();
                    jo[key] = arr;
                }
            }
            else arr = found;
        }

        var idx = IndexOfId(arr, desc.IdProperty, id);
        if (isCreate && idx >= 0) return (false, "ID 已存在：" + id);
        if (!isCreate && idx < 0) return (false, "找不到记录：" + id);

        var clone = CloneObj(entity);
        if (idx >= 0) arr[idx] = clone;
        else arr.Add(clone);

        return await SaveRootAsync(packId, desc.RelativePath, root, actor, $"{desc.Kind}.{(isCreate ? "create" : "update")}:{id}", ip, ct);
    }

    public async Task<(bool ok, string message)> DeleteAsync(
        string packId, EntityDescriptor desc, string id, string actor, string ip, CancellationToken ct = default)
    {
        if (desc.IsMapFiles)
        {
            var rel = $"maps/{id}.json";
            try
            {
                var path = Path.Combine(packs.GetPackRoot(packId), rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) return (false, "地图文件不存在");
                File.Delete(path);
                await audit.WriteAsync(actor, $"{desc.Kind}.delete", $"{packId}/{rel}", new { id }, ip, ct);
                // also record a config revision note via empty write skip — write a stub revision via packs by touching a note file? Use WriteAsync on a .deleted marker? Prefer audit only.
                return (true, "已删除地图 " + id);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        var (ok, msg, content) = await packs.ReadAsync(packId, desc.RelativePath, ct);
        if (!ok || content is null) return (false, msg);
        var root = JsonNode.Parse(content) ?? new JsonObject();
        var arr = FindArray(root, desc.ArrayKeys);
        if (arr is null && root is JsonArray ra) arr = ra;
        if (arr is null) return (false, "找不到实体数组");
        var idx = IndexOfId(arr, desc.IdProperty, id);
        if (idx < 0) return (false, "找不到记录：" + id);
        arr.RemoveAt(idx);
        return await SaveRootAsync(packId, desc.RelativePath, root, actor, $"{desc.Kind}.delete:{id}", ip, ct);
    }

    public async Task<(bool ok, string message, string? newId)> CloneAsync(
        string packId, EntityDescriptor desc, string sourceId, string newId, string actor, string ip, CancellationToken ct = default)
    {
        if (!IsValidId(newId)) return (false, "新 ID 非法", null);
        var (ok, msg, src) = await GetAsync(packId, desc, sourceId, ct);
        if (!ok || src is null) return (false, msg, null);
        var copy = CloneObj(src);
        copy[desc.IdProperty] = newId;
        if (desc.DisplayProperty != desc.IdProperty && copy[desc.DisplayProperty] is JsonValue)
        {
            var dn = GetStr(copy, desc.DisplayProperty);
            if (!string.IsNullOrEmpty(dn) && !dn.EndsWith("（副本）"))
                copy[desc.DisplayProperty] = dn + "（副本）";
        }
        var (uok, umsg) = await UpsertAsync(packId, desc, copy, actor, ip, isCreate: true, ct);
        return (uok, umsg, uok ? newId : null);
    }

    public JsonObject MergeForm(EntityDescriptor desc, JsonObject? existing, IDictionary<string, string?> form)
    {
        var node = existing is null ? new JsonObject() : CloneObj(existing);
        foreach (var field in desc.FormFields)
        {
            if (!form.TryGetValue(field.Key, out var raw)) continue;
            raw ??= "";
            JsonNode? value;
            if (field.InputKind is "number")
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (field.Key.Contains('.')) SetPath(node, field.Key, null);
                    else node.Remove(field.Key);
                    continue;
                }
                if (raw.Contains('.') && double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) value = d;
                else if (long.TryParse(raw, out var n)) value = n;
                else value = raw;
            }
            else if (field.InputKind is "select" && (field.SelectOptionsCsv?.Contains("true") ?? false) && (raw is "true" or "false"))
            {
                value = raw == "true";
            }
            else if (field.InputKind is "json")
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try { value = JsonNode.Parse(raw); }
                catch { value = raw; }
            }
            else if (field.InputKind is "asset")
            {
                value = string.IsNullOrWhiteSpace(raw) ? null : raw;
                if (value is null)
                {
                    if (field.Key.Contains('.')) SetPath(node, field.Key, null);
                    else node.Remove(field.Key);
                    continue;
                }
            }
            else
            {
                value = raw;
            }

            if (field.Key.Contains('.')) SetPath(node, field.Key, value);
            else node[field.Key] = value;
        }
        return node;
    }

    public JsonObject FromDefaults(EntityDescriptor desc, string id, string? displayName = null)
    {
        var seed = new Dictionary<string, string>
        {
            [desc.IdProperty] = id,
            ["displayName"] = displayName ?? id,
        };
        var dict = desc.BuildDefaults(seed);
        var obj = new JsonObject();
        foreach (var (k, v) in dict)
            obj[k] = ToNode(v);
        return obj;
    }

    public static string NodeToStr(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s)) return s ?? "";
            if (jv.TryGetValue<bool>(out var b)) return b ? "true" : "false";
            if (jv.TryGetValue<long>(out var l)) return l.ToString();
            if (jv.TryGetValue<double>(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (n is JsonObject or JsonArray)
            return n.ToJsonString();
        return n.ToJsonString().Trim('"');
    }

    public static string GetStr(JsonObject o, string key)
    {
        if (key.Contains('.'))
        {
            JsonNode? cur = o;
            foreach (var part in key.Split('.'))
            {
                if (cur is JsonObject jo && jo.TryGetPropertyValue(part, out var next)) cur = next;
                else return "";
            }
            return NodeToStr(cur);
        }
        if (!o.TryGetPropertyValue(key, out var n) || n is null) return "";
        return NodeToStr(n);
    }

    public static string GetLookAssetId(JsonObject entity, string? lookSpec)
    {
        if (string.IsNullOrWhiteSpace(lookSpec)) return "";
        foreach (var path in lookSpec.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var s = GetStr(entity, path);
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return "";
    }

    public static void SetPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;
        JsonObject cur = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (cur[parts[i]] is not JsonObject child)
            {
                child = new JsonObject();
                cur[parts[i]] = child;
            }
            cur = child;
        }
        var leaf = parts[^1];
        if (value is null) cur.Remove(leaf);
        else cur[leaf] = value;
    }

    private async Task<(bool ok, string message, List<JsonObject> entities)> ListMapsAsync(string packId, CancellationToken ct)
    {
        var root = packs.GetPackRoot(packId);
        var dir = Path.Combine(root, "maps");
        var list = new List<JsonObject>();
        if (!Directory.Exists(dir)) return (true, "ok", list);
        foreach (var f in Directory.EnumerateFiles(dir, "*.json").OrderBy(Path.GetFileName))
        {
            try
            {
                var text = await File.ReadAllTextAsync(f, ct);
                if (JsonNode.Parse(text) is JsonObject o) list.Add(CloneObj(o));
            }
            catch { /* skip bad */ }
        }
        return (true, "ok", list);
    }

    private async Task<(bool ok, string message)> UpsertMapAsync(
        string packId, EntityDescriptor desc, JsonObject entity, string actor, string ip, bool isCreate, CancellationToken ct)
    {
        var id = GetStr(entity, desc.IdProperty);
        var rel = $"maps/{id}.json";
        var path = Path.Combine(packs.GetPackRoot(packId), rel.Replace('/', Path.DirectorySeparatorChar));
        if (isCreate && File.Exists(path)) return (false, "地图已存在：" + id);
        if (!isCreate && !File.Exists(path)) return (false, "地图不存在：" + id);

        // merge with existing to preserve blockedCells/portals/spawns when form omits them
        if (!isCreate && File.Exists(path))
        {
            try
            {
                var old = JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) as JsonObject;
                if (old is not null)
                {
                    foreach (var prop in old)
                    {
                        if (!entity.ContainsKey(prop.Key))
                            entity[prop.Key] = prop.Value is null ? null : JsonNode.Parse(prop.Value.ToJsonString());
                    }
                }
            }
            catch { /* ignore */ }
        }

        return await SaveRootAsync(packId, rel, entity, actor, $"maps.{(isCreate ? "create" : "update")}:{id}", ip, ct);
    }

    private async Task<(bool ok, string message)> SaveRootAsync(
        string packId, string rel, JsonNode root, string actor, string note, string ip, CancellationToken ct)
    {
        var json = root.ToJsonString(Pretty) + "\n";
        var (ok, msg, hash) = await packs.WriteAsync(packId, rel, json, actor, note, ct);
        if (!ok) return (false, msg);
        await audit.WriteAsync(actor, "entity.save", $"{packId}/{rel}", new { note, hash }, ip, ct);
        return (true, "已保存");
    }

    private static JsonArray? FindArray(JsonNode? root, string[] keys)
    {
        if (root is JsonObject jo)
        {
            foreach (var k in keys)
                if (jo[k] is JsonArray arr) return arr;
            // empty object → create nothing yet
        }
        return null;
    }

    private static int IndexOfId(JsonArray arr, string idProp, string id)
    {
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is JsonObject o &&
                string.Equals(GetStr(o, idProp), id, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static JsonObject CloneObj(JsonObject o) =>
        JsonNode.Parse(o.ToJsonString()) as JsonObject ?? new JsonObject();

    private static JsonNode? ToNode(object? v) => v switch
    {
        null => null,
        JsonNode jn => jn,
        string s => s,
        bool b => b,
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        decimal m => m,
        _ => JsonSerializer.SerializeToNode(v)
    };
}
