using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Cdk;

[Authorize(Policy = "OperatorPlus")]
public class IndexModel(AdminDbContext db, AuditService audit) : PageModel
{
    public List<CdkBatch> Batches { get; set; } = new();
    public List<CdkCode> Codes { get; set; } = new();
    [BindProperty] public string BatchName { get; set; } = "";
    [BindProperty] public int Count { get; set; } = 10;
    [BindProperty] public string RewardsJson { get; set; } = "[]";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Batches = await db.CdkBatches.OrderByDescending(b => b.Id).Take(50).ToListAsync(ct);
        Codes = await db.CdkCodes.OrderByDescending(c => c.Id).Take(100).ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Count = Math.Clamp(Count, 1, 500);
        var batch = new CdkBatch
        {
            Name = BatchName,
            RewardsJson = string.IsNullOrWhiteSpace(RewardsJson) ? "[]" : RewardsJson,
            Actor = User.Identity?.Name ?? "?",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.CdkBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        for (int i = 0; i < Count; i++)
        {
            db.CdkCodes.Add(new CdkCode
            {
                BatchId = batch.Id,
                Code = $"OL-{batch.Id:D4}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
            });
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(batch.Actor, "cdk.generate", batch.Name, new { batch.Id, Count }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = $"已生成批次 #{batch.Id} × {Count}";
        return RedirectToPage();
    }
}
