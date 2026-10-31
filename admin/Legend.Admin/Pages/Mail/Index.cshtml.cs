using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.Mail;

[Authorize(Policy = "OperatorPlus")]
public class IndexModel(AdminDbContext db, AuditService audit) : PageModel
{
    public List<MailJob> Jobs { get; set; } = new();
    [BindProperty] public string Scope { get; set; } = "All";
    [BindProperty] public string Target { get; set; } = "";
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    [BindProperty] public string AttachmentsJson { get; set; } = "[]";

    public async Task OnGetAsync(CancellationToken ct)
        => Jobs = await db.MailJobs.OrderByDescending(j => j.Id).Take(100).ToListAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var job = new MailJob
        {
            Scope = Scope, Target = Target, Title = Title, Body = Body,
            AttachmentsJson = string.IsNullOrWhiteSpace(AttachmentsJson) ? "[]" : AttachmentsJson,
            Status = "Queued", Actor = User.Identity?.Name ?? "?", CreatedAt = DateTimeOffset.UtcNow
        };
        db.MailJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(job.Actor, "mail.enqueue", job.Title, job, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "邮件已入队 #" + job.Id;
        return RedirectToPage();
    }
}
