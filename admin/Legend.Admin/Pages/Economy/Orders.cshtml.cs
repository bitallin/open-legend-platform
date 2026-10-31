using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Economy;

[Authorize(Policy = "OperatorPlus")]
public class OrdersModel(AdminDbContext db, HostAdminClient host, AuditService audit) : PageModel
{
    public List<RechargeOrderNote> Orders { get; set; } = new();
    public string HostResponse { get; set; } = "";
    [BindProperty] public string OrderId { get; set; } = "";
    [BindProperty] public string AccountId { get; set; } = "";
    [BindProperty] public string CharacterName { get; set; } = "";
    [BindProperty] public long AmountCents { get; set; }
    [BindProperty] public string CurrencyId { get; set; } = "yuanbao";
    [BindProperty] public long GrantAmount { get; set; }
    [BindProperty] public string Note { get; set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
        => Orders = await db.RechargeOrders.OrderByDescending(o => o.Id).Take(100).ToListAsync(ct);

    public async Task<IActionResult> OnPostManualAsync(CancellationToken ct)
    {
        var o = new RechargeOrderNote
        {
            OrderId = string.IsNullOrWhiteSpace(OrderId) ? ("manual-" + Guid.NewGuid().ToString("N")[..8]) : OrderId,
            AccountId = AccountId,
            CharacterName = CharacterName,
            AmountCents = AmountCents,
            CurrencyId = CurrencyId,
            GrantAmount = GrantAmount,
            Status = "Manual",
            Provider = "manual",
            Actor = User.Identity?.Name ?? "?",
            Note = Note,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.RechargeOrders.Add(o);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(o.Actor, "economy.manual_order", o.OrderId, o, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "补单已记录 " + o.OrderId;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostHostRechargeAsync(string characterName, string productId, int quantity, CancellationToken ct)
    {
        var (ok, status, body) = await host.PostJsonAsync("admin/recharge", new { characterName, productId, quantity }, ct);
        HostResponse = body;
        await audit.WriteAsync(User.Identity?.Name ?? "?", "economy.host_recharge", characterName, new { productId, quantity, status, body }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        Orders = await db.RechargeOrders.OrderByDescending(o => o.Id).Take(100).ToListAsync(ct);
        TempData[ok ? "Flash" : "Error"] = body;
        return Page();
    }
}
