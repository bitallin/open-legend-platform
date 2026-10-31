using Legend.Admin.Data;
using Legend.Admin.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Audit;

public class IndexModel(AdminDbContext db) : PageModel
{
    public List<AuditLog> Logs { get; set; } = new();
    public string Q { get; set; } = "";

    public async Task OnGetAsync(string? q, CancellationToken ct)
    {
        Q = q ?? "";
        IQueryable<AuditLog> query = db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
            query = query.Where(a => a.Action.Contains(Q) || a.Target.Contains(Q) || a.Actor.Contains(Q));
        Logs = await query.OrderByDescending(a => a.Id).Take(200).ToListAsync(ct);
    }
}
