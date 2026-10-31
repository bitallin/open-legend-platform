using System.Text.Json;
using System.Text.Json.Nodes;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Encodings.Web;

namespace Legend.Admin.Pages.Entities;

[Authorize(Policy = "AnyStaff")]
public class GameplayModel(VersionPackService packs, AuditService audit, PackPreference packPref) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<ModuleLink> Modules { get; set; } = new();
    public List<ToggleItem> Toggles { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);

    public async Task OnGetAsync(string? packId, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = packPref.Resolve(packId);
        Modules = BuildModules(PackId);
        Toggles = await LoadTogglesAsync(PackId, ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(
        string packId, string file, string path, string value, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg, content) = await packs.ReadAsync(packId, file, ct);
        if (!ok || content is null)
        {
            TempData["Error"] = msg;
            return RedirectToPage(new { packId });
        }
        try
        {
            var root = JsonNode.Parse(content) ?? new JsonObject();
            SetPath(root, path, value);
            var pretty = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
            var (wok, wmsg, hash) = await packs.WriteAsync(packId, file, pretty, actor, $"toggle:{path}={value}", ct);
            if (!wok) { TempData["Error"] = wmsg; return RedirectToPage(new { packId }); }
            await audit.WriteAsync(actor, "gameplay.toggle", $"{packId}/{file}:{path}", new { value, hash }, ip, ct);
            TempData["Flash"] = $"已更新 {file} · {path} = {value}";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage(new { packId });
    }

    public async Task<IActionResult> OnPostAddFlagAsync(
        string packId, string file, string path, string value, CancellationToken ct)
        => await OnPostToggleAsync(packId, file, path, value, ct);

    public async Task<IActionResult> OnPostRemoveFlagAsync(
        string packId, string file, string path, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg, content) = await packs.ReadAsync(packId, file, ct);
        if (!ok || content is null) { TempData["Error"] = msg; return RedirectToPage(new { packId }); }
        try
        {
            var root = JsonNode.Parse(content) ?? new JsonObject();
            RemovePath(root, path);
            var pretty = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
            var (wok, wmsg, hash) = await packs.WriteAsync(packId, file, pretty, actor, $"toggle.remove:{path}", ct);
            if (!wok) { TempData["Error"] = wmsg; return RedirectToPage(new { packId }); }
            await audit.WriteAsync(actor, "gameplay.toggle.remove", $"{packId}/{file}:{path}", new { hash }, ip, ct);
            TempData["Flash"] = $"已删除开关 {path}";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { packId });
    }

    private List<ModuleLink> BuildModules(string packId)
    {
        var list = new (string Title, string Path, string Hint)[]
        {
            ("内功/经络", "configs/internal.json", "等级条目与经络"),
            ("客户端辅助", "configs/client_assist.json", "内挂总开关与策略"),
            ("拾取过滤", "configs/pickup_filters.json", "拾取规则档案"),
            ("职业", "configs/classes.json", "职业数值"),
            ("经验表", "configs/levels.json", "升级经验"),
            ("货币", "configs/currencies.json", "多货币定义"),
            ("Buff", "configs/buffs.json", "状态效果"),
            ("行会", "configs/guild.json", "行会规则"),
            ("攻城", "configs/siege.json", "沙巴克"),
            ("回收", "configs/recycle.json", "装备回收"),
            ("交易", "configs/trade.json", "交易规则"),
            ("组队", "configs/party.json", "组队"),
            ("邮件规则", "configs/mail.json", "邮件上限"),
            ("副本", "configs/dungeons.json", "副本"),
            ("世界 Boss", "configs/world_boss.json", "世界 Boss"),
            ("坐骑", "configs/mounts.json", "坐骑"),
            ("时装", "configs/fashions.json", "时装"),
            ("宠物", "configs/pets.json", "宠物"),
            ("天赋", "configs/talents.json", "天赋"),
            ("神器", "configs/artifacts.json", "神器"),
            ("英雄", "configs/heroes.json", "英雄"),
            ("合击", "configs/combos.json", "合击"),
            ("装备槽", "configs/equipment_slots.json", "装备槽"),
        };
        return list.Select(m =>
        {
            var exists = false;
            try { exists = System.IO.File.Exists(Path.Combine(packs.GetPackRoot(packId), m.Path.Replace('/', Path.DirectorySeparatorChar))); }
            catch { }
            return new ModuleLink(m.Title, m.Path, m.Hint, exists);
        }).ToList();
    }

    private async Task<List<ToggleItem>> LoadTogglesAsync(string packId, CancellationToken ct)
    {
        var items = new List<ToggleItem>();
        async Task AddFile(string file, params (string path, string label)[] keys)
        {
            var (ok, _, content) = await packs.ReadAsync(packId, file, ct);
            if (!ok || content is null) return;
            JsonNode? root;
            try { root = JsonNode.Parse(content); } catch { return; }
            foreach (var (path, label) in keys)
            {
                var node = GetPath(root, path);
                var kind = node switch
                {
                    JsonValue jv when jv.TryGetValue<bool>(out _) => "bool",
                    JsonValue jv when jv.TryGetValue<long>(out _) || jv.TryGetValue<double>(out _) => "number",
                    _ => "text"
                };
                var val = node is null ? "" : (node is JsonValue ? node.ToString() : node.ToJsonString());
                items.Add(new ToggleItem(file, path, label, kind, val ?? ""));
            }
        }

        await AddFile("configs/client_assist.json",
            ("enabled", "内挂总开关"),
            ("stopOnPortal", "遇传送停挂"),
            ("stopOnMapChange", "换图停挂"),
            ("stopOnDeath", "死亡停挂"),
            ("stopOnManualMove", "手动移动停挂"),
            ("pickup.enabled", "辅助拾取"),
            ("poison.enabled", "自动施毒"),
            ("hotkey", "热键"),
            ("mode", "模式"),
            ("anchorRadiusCells", "锚点半径"),
            ("searchRangeCells", "索敌范围"),
            ("maxPullCount", "最大拉怪数"),
            ("globalCastGapMs", "施法间隔 ms"),
            ("comboPriority", "合击优先"));

        await AddFile("configs/internal.json",
            ("maxLevel", "内功最大等级"));

        // pickup profiles defaultForAssist flags
        {
            var (ok, _, content) = await packs.ReadAsync(packId, "configs/pickup_filters.json", ct);
            if (ok && content is not null)
            {
                try
                {
                    var root = JsonNode.Parse(content);
                    if (root?["profiles"] is JsonArray arr)
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            if (arr[i] is not JsonObject o) continue;
                            var id = o["profileId"]?.GetValue<string>() ?? $"#{i}";
                            items.Add(new ToggleItem(
                                "configs/pickup_filters.json",
                                $"profiles[{i}].defaultForAssist",
                                $"拾取档案 {id} · 默认辅助",
                                "bool",
                                (o["defaultForAssist"]?.GetValue<bool>() ?? false).ToString().ToLowerInvariant()));
                        }
                    }
                }
                catch { }
            }
        }

        return items;
    }

    private static JsonNode? GetPath(JsonNode? root, string path)
    {
        JsonNode? cur = root;
        foreach (var part in SplitPath(path))
        {
            if (cur is null) return null;
            if (part.Index is int i)
            {
                if (cur is JsonArray arr && i >= 0 && i < arr.Count) cur = arr[i];
                else return null;
            }
            else
            {
                if (cur is JsonObject jo && jo.TryGetPropertyValue(part.Key!, out var next)) cur = next;
                else return null;
            }
        }
        return cur;
    }

    private static void SetPath(JsonNode root, string path, string value)
    {
        var parts = SplitPath(path);
        JsonNode cur = root;
        for (int i = 0; i < parts.Count - 1; i++)
        {
            var part = parts[i];
            if (part.Index is int idx)
            {
                var arr = (JsonArray)cur;
                cur = arr[idx]!;
            }
            else
            {
                var jo = (JsonObject)cur;
                if (jo[part.Key!] is null)
                {
                    // create object or array depending on next
                    var next = parts[i + 1];
                    jo[part.Key!] = next.Index is not null ? new JsonArray() : new JsonObject();
                }
                cur = jo[part.Key!]!;
            }
        }
        var last = parts[^1];
        JsonNode? valNode = value switch
        {
            "true" => true,
            "false" => false,
            _ when long.TryParse(value, out var n) => n,
            _ when double.TryParse(value, out var d) => d,
            _ => value
        };
        if (last.Index is int li)
            ((JsonArray)cur)[li] = valNode;
        else
            ((JsonObject)cur)[last.Key!] = valNode;
    }

    private static void RemovePath(JsonNode root, string path)
    {
        var parts = SplitPath(path);
        if (parts.Count == 0) return;
        JsonNode cur = root;
        for (int i = 0; i < parts.Count - 1; i++)
        {
            var part = parts[i];
            if (part.Index is int idx) cur = ((JsonArray)cur)[idx]!;
            else cur = ((JsonObject)cur)[part.Key!]!;
        }
        var last = parts[^1];
        if (last.Index is int li) ((JsonArray)cur).RemoveAt(li);
        else ((JsonObject)cur).Remove(last.Key!);
    }

    private static List<(string? Key, int? Index)> SplitPath(string path)
    {
        var list = new List<(string? Key, int? Index)>();
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = raw;
            while (true)
            {
                var b = s.IndexOf('[');
                if (b < 0)
                {
                    if (!string.IsNullOrEmpty(s)) list.Add((s, null));
                    break;
                }
                if (b > 0) list.Add((s[..b], null));
                var e = s.IndexOf(']', b);
                var num = s[(b + 1)..e];
                list.Add((null, int.Parse(num)));
                s = s[(e + 1)..];
            }
        }
        return list;
    }

    public record ModuleLink(string Title, string Path, string Hint, bool Exists);
    public record ToggleItem(string File, string Path, string Label, string Kind, string Value);
}
