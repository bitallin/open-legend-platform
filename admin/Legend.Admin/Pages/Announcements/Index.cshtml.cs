using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Announcements;

[Authorize(Policy = "OperatorPlus")]
public class IndexModel(AdminDbContext db, AuditService audit) : PageModel
{
    public List<Announcement> Items { get; set; } = new();
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; } = true;

    public async Task OnGetAsync(CancellationToken ct)
        => Items = await db.Announcements.OrderByDescending(a => a.Id).ToListAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var a = new Announcement { Title = Title, Body = Body, IsActive = IsActive, Actor = User.Identity?.Name ?? "?", CreatedAt = DateTimeOffset.UtcNow };
        db.Announcements.Add(a);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(a.Actor, "announce.create", a.Title, a, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "公告已创建";
        return RedirectToPage();
    }
}
