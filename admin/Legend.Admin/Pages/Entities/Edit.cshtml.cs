using System.Text.Json.Nodes;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Encodings.Web;

namespace Legend.Admin.Pages.Entities;

[Authorize(Policy = "AnyStaff")]
public class EditModel(
    VersionPackService packs,
    JsonEntityStore store,
    MediaLibraryService media,
    LookThumbResolver thumbs, PackPreference packPref) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public string Kind { get; set; } = "monsters";
    public EntityDescriptor? Desc { get; set; }
    public string? EntityId { get; set; }
    public bool IsCreate { get; set; }
    public JsonObject Entity { get; set; } = new();
    public Dictionary<string, string> FormValues { get; set; } = new();
    public string? ThumbUrl { get; set; }
    public List<MediaAsset> MediaChoices { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);
    public string AdvancedJson { get; set; } = "{}";

    public async Task<IActionResult> OnGetAsync(string kind, string? packId, string? id, CancellationToken ct)
    {
        Desc = EntityCatalog.Get(kind);
        if (Desc is null) return NotFound();
        Kind = Desc.Kind;
        Packs = packs.ListPacks().ToList();
        PackId = packPref.Resolve(packId);
        EntityId = id;
        IsCreate = string.IsNullOrWhiteSpace(id);

        if (IsCreate)
        {
            Entity = store.FromDefaults(Desc, "new_" + Desc.Kind.TrimEnd('s'), "新建" + Desc.TitleZh);
            // clear id for user to fill
            Entity[Desc.IdProperty] = "";
        }
        else
        {
            var (ok, msg, ent) = await store.GetAsync(PackId, Desc, id!, ct);
            if (!ok || ent is null)
            {
                TempData["Error"] = msg;
                return RedirectToPage(ListPage(Desc.Kind), new { packId = PackId });
            }
            Entity = ent;
        }

        FillFormFromEntity();
        var assetMap = await thumbs.LoadPackAssetsAsync(PackId, ct);
        ThumbUrl = thumbs.ThumbUrlForEntity(Entity, Desc, assetMap);
        MediaChoices = (await media.QueryAsync(PackId, "all", null, ct: ct)).Take(200).ToList();
        AdvancedJson = Entity.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        string kind, string packId, string? entityId, bool isCreate,
        Dictionary<string, string?> fields, string? advancedJson, bool useAdvanced, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        Desc = EntityCatalog.Get(kind);
        if (Desc is null) return NotFound();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        JsonObject entity;
        if (useAdvanced && !string.IsNullOrWhiteSpace(advancedJson))
        {
            try { entity = JsonNode.Parse(advancedJson!) as JsonObject ?? new JsonObject(); }
            catch (Exception ex)
            {
                TempData["Error"] = "高级 JSON 无效：" + ex.Message;
                return RedirectToPage(new { kind, packId, id = entityId });
            }
        }
        else
        {
            JsonObject? existing = null;
            if (!isCreate && !string.IsNullOrEmpty(entityId))
            {
                var (ok, _, ent) = await store.GetAsync(packId, Desc, entityId, ct);
                if (ok) existing = ent;
            }
            entity = store.MergeForm(Desc, existing, fields ?? new());
        }

        var id = JsonEntityStore.GetStr(entity, Desc.IdProperty);
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Error"] = "ID 不能为空";
            return RedirectToPage(new { kind, packId, id = entityId });
        }

        var (uok, umsg) = await store.UpsertAsync(packId, Desc, entity, actor, ip, isCreate, ct);
        if (!uok) { TempData["Error"] = umsg; return RedirectToPage(new { kind, packId, id = entityId }); }
        TempData["Flash"] = umsg + " · " + id;
        return RedirectToPage(new { kind, packId, id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string kind, string packId, string id, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        Desc = EntityCatalog.Get(kind);
        if (Desc is null) return NotFound();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await store.DeleteAsync(packId, Desc, id, actor, ip, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(ListPage(kind), new { packId });
    }

    public async Task<IActionResult> OnPostCloneAsync(string kind, string packId, string id, string newId, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        Desc = EntityCatalog.Get(kind);
        if (Desc is null) return NotFound();
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg, nid) = await store.CloneAsync(packId, Desc, id, newId, actor, ip, ct);
        if (ok) { TempData["Flash"] = msg; return RedirectToPage(new { kind, packId, id = nid }); }
        TempData["Error"] = msg;
        return RedirectToPage(new { kind, packId, id });
    }

    private void FillFormFromEntity()
    {
        FormValues.Clear();
        if (Desc is null) return;
        foreach (var f in Desc.FormFields)
        {
            if (f.InputKind == "json")
            {
                // resolve dotted or top-level
                JsonNode? n = null;
                if (f.Key.Contains('.'))
                {
                    JsonNode? cur = Entity;
                    foreach (var part in f.Key.Split('.'))
                    {
                        if (cur is JsonObject jo && jo.TryGetPropertyValue(part, out var next)) cur = next;
                        else { cur = null; break; }
                    }
                    n = cur;
                }
                else Entity.TryGetPropertyValue(f.Key, out n);
                FormValues[f.Key] = n is null ? (f.Key.EndsWith("entries") || f.Key == "offers" ? "[]" : "{}")
                    : n.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                continue;
            }
            FormValues[f.Key] = JsonEntityStore.GetStr(Entity, f.Key);
        }
    }

    public static string ListPage(string kind) => kind switch
    {
        "monsters" => "/Entities/Monsters",
        "items" => "/Entities/Items",
        "maps" => "/Entities/Maps",
        "npcs" => "/Entities/Npcs",
        "skills" => "/Entities/Skills",
        "quests" => "/Entities/Quests",
        "shops" => "/Entities/Shops",
        "loot" => "/Entities/Loot",
        _ => "/Studio"
    };
}
