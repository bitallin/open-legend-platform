using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Entities;

public class GameplayModel(VersionPackService packs) : PageModel
{
    public List<PackInfo> Packs { get; set; } = new();
    public string PackId { get; set; } = "sample-classic";
    public List<ModuleLink> Modules { get; set; } = new();

    public void OnGet(string? packId)
    {
        Packs = packs.ListPacks().ToList();
        PackId = string.IsNullOrWhiteSpace(packId) ? (Packs.FirstOrDefault()?.Id ?? "sample-classic") : packId;
        var files = packs.ListConfigFiles(PackId).Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defs = new (string Title, string Path, string Hint)[]
        {
            ("职业", "configs/classes.json", "三职业基础"),
            ("等级经验", "configs/levels.json", "升级曲线"),
            ("货币定义", "configs/currencies.json", "多货币"),
            ("Buff", "configs/buffs.json", "状态效果"),
            ("行会", "configs/guild.json", "行会规则"),
            ("攻城/沙巴克", "configs/siege.json", "攻城参数"),
            ("组队", "configs/party.json", "组队规则"),
            ("交易/寄售", "configs/trade.json", "交易税等"),
            ("邮件规则", "configs/mail.json", "邮件上限等"),
            ("副本", "configs/dungeons.json", "副本入口"),
            ("世界 Boss", "configs/world_boss.json", "刷新与奖励"),
            ("坐骑", "configs/mounts.json", "坐骑表"),
            ("时装", "configs/fashions.json", "外观"),
            ("宠物", "configs/pets.json", "宠物"),
            ("天赋", "configs/talents.json", "天赋树"),
            ("神器/生肖", "configs/artifacts.json", "神器"),
            ("英雄", "configs/heroes.json", "英雄"),
            ("合击", "configs/combos.json", "合击技能"),
            ("装备槽", "configs/equipment_slots.json", "槽位"),
            ("回收", "configs/recycle.json", "回收价"),
            ("内挂默认", "configs/internal.json", "内挂策略"),
            ("客户端辅助", "configs/client_assist.json", "辅助开关"),
            ("拾取过滤", "configs/pickup_filters.json", "拾取过滤"),
        };
        Modules = defs.Select(d => new ModuleLink(d.Title, d.Path, d.Hint, files.Contains(d.Path))).ToList();
    }

    public record ModuleLink(string Title, string Path, string Hint, bool Exists);
}
