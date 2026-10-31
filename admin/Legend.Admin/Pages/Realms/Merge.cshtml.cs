using System.Text.Json;
using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Realms;

[Authorize(Policy = "OperatorPlus")]
public class MergeModel(AdminDbContext db, AuditService audit) : PageModel
{
    public List<MergePlan> Plans { get; set; } = new();
    [BindProperty] public string SourceRealmIds { get; set; } = "";
    [BindProperty] public string TargetRealmId { get; set; } = "";
    [BindProperty] public string Notes { get; set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
        => Plans = await db.MergePlans.OrderByDescending(p => p.Id).ToListAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var sources = SourceRealmIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var plan = new MergePlan
        {
            SourceRealmIdsJson = JsonSerializer.Serialize(sources),
            TargetRealmId = TargetRealmId.Trim(),
            Notes = Notes,
            Status = "Draft",
            Actor = User.Identity?.Name ?? "?",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.MergePlans.Add(plan);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(plan.Actor, "merge.create", plan.TargetRealmId, plan, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "合区计划已创建 #" + plan.Id;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, string status, CancellationToken ct)
    {
        var plan = await db.MergePlans.FindAsync([id], ct);
        if (plan is null) { TempData["Error"] = "not found"; return RedirectToPage(); }
        plan.Status = status;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(User.Identity?.Name ?? "?", "merge.status", id.ToString(), new { status }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = $"计划 #{id} => {status}";
        return RedirectToPage();
    }
}
