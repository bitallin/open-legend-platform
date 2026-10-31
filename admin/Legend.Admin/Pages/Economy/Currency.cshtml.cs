using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Economy;

[Authorize(Policy = "OperatorPlus")]
public class CurrencyModel(VersionPackService packs, AdminDbContext db, AuditService audit) : PageModel
{
    public string PackId { get; set; } = "sample-classic";
    public string CurrenciesJson { get; set; } = "";
    public List<CurrencyAdjustment> Adjustments { get; set; } = new();
    [BindProperty] public string AccountOrCharacter { get; set; } = "";
    [BindProperty] public string CurrencyId { get; set; } = "yuanbao";
    [BindProperty] public long Delta { get; set; }
    [BindProperty] public string Reason { get; set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var (ok, _, content) = await packs.ReadAsync(PackId, "configs/currencies.json", ct);
        CurrenciesJson = ok ? content! : "(无法读取)";
        Adjustments = await db.CurrencyAdjustments.OrderByDescending(a => a.Id).Take(50).ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var row = new CurrencyAdjustment
        {
            AccountOrCharacter = AccountOrCharacter,
            CurrencyId = CurrencyId,
            Delta = Delta,
            Reason = Reason,
            Actor = User.Identity?.Name ?? "?",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.CurrencyAdjustments.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(row.Actor, "economy.adjust", row.AccountOrCharacter, row, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "调账已记录（本地审计流水）";
        return RedirectToPage();
    }
}
