using System.Text.Json.Nodes;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Entities;

[Authorize(Policy = "AnyStaff")]
public class MapSpawnsModel(
    VersionPackService packs,
    JsonEntityStore store,
    MonsterSpawnStore spawns,
    PackPreference packPref) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = PackPreference.PreferredId;
    public string MapId { get; set; } = "";
    public string MapName { get; set; } = "";
    public List<MapOption> Maps { get; set; } = new();
    public List<MonsterOption> Monsters { get; set; } = new();
    public List<MonsterSpawnStore.SpawnRow> Rows { get; set; } = new();
    public Dictionary<string, int> CountsByMap { get; set; } = new();
    public bool CanWrite => User.IsInRole(AdminRoles.SuperAdmin) || User.IsInRole(AdminRoles.Operator);
    public string? LoadError { get; set; }

    public async Task OnGetAsync(string? packId, string? mapId, CancellationToken ct)
    {
        Packs = packs.ListPacks().ToList();
        PackId = packPref.Resolve(packId);
        await LoadMapsAsync(ct);
        MapId = mapId?.Trim() ?? "";
        if (string.IsNullOrEmpty(MapId) && Maps.Count > 0)
            MapId = Maps[0].Id;
        MapName = Maps.FirstOrDefault(m => m.Id == MapId)?.Name ?? MapId;

        var (mok, _, mlist) = await store.ListAsync(PackId, EntityCatalog.Monsters, ct);
        if (mok)
        {
            Monsters = mlist.Select(e => new MonsterOption(
                JsonEntityStore.GetStr(e, "monsterId"),
                JsonEntityStore.GetStr(e, "displayName"),
                JsonEntityStore.GetStr(e, "respawnMs"),
                JsonEntityStore.GetStr(e, "maxHp"),
                JsonEntityStore.GetStr(e, "expReward")
            )).Where(m => !string.IsNullOrEmpty(m.Id)).OrderBy(m => m.Name).ToList();
        }

        CountsByMap = await spawns.CountByMapAsync(PackId, ct);
        var (ok, msg, rows) = await spawns.ListAsync(PackId, string.IsNullOrEmpty(MapId) ? null : MapId, ct);
        if (!ok) LoadError = msg;
        else Rows = rows;
    }

    public async Task<IActionResult> OnPostAddAsync(
        string packId, string mapId, string monsterId, int x, int y, int count, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        packPref.Persist(packId);
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await spawns.AddAsync(packId, mapId, monsterId, x, y, Math.Max(1, count), actor, ip, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId, mapId });
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        string packId, string mapId, int index, string monsterId, int x, int y, int count, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        packPref.Persist(packId);
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await spawns.UpdateAsync(packId, index, mapId, monsterId, x, y, Math.Max(1, count), actor, ip, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId, mapId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string packId, string mapId, int index, CancellationToken ct)
    {
        if (!CanWrite) return Forbid();
        packPref.Persist(packId);
        var actor = User.Identity?.Name ?? "?";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var (ok, msg) = await spawns.DeleteAsync(packId, index, actor, ip, ct);
        if (ok) TempData["Flash"] = msg; else TempData["Error"] = msg;
        return RedirectToPage(new { packId, mapId });
    }

    private async Task LoadMapsAsync(CancellationToken ct)
    {
        var (ok, _, list) = await store.ListAsync(PackId, EntityCatalog.Maps, ct);
        if (!ok) return;
        Maps = list.Select(e => new MapOption(
            JsonEntityStore.GetStr(e, "mapId"),
            JsonEntityStore.GetStr(e, "mapName")
        )).Where(m => !string.IsNullOrEmpty(m.Id)).OrderBy(m => m.Name).ToList();
    }

    public record MapOption(string Id, string Name);
    public record MonsterOption(string Id, string Name, string RespawnMs, string MaxHp, string Exp);
}
