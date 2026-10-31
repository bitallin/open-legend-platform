using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Pages.SystemPages;

[Authorize(Policy = "SuperAdmin")]
public class UsersModel(AdminDbContext db, AuditService audit) : PageModel
{
    public List<AdminUser> Users { get; set; } = new();
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Role { get; set; } = AdminRoles.Support;

    public async Task OnGetAsync(CancellationToken ct)
        => Users = await db.Users.OrderBy(u => u.Id).ToListAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Username == Username, ct))
        {
            TempData["Error"] = "用户名已存在";
            return RedirectToPage();
        }
        db.Users.Add(new AdminUser
        {
            Username = Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            Role = Role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(User.Identity?.Name ?? "?", "user.create", Username, new { Role }, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        TempData["Flash"] = "已创建 " + Username;
        return RedirectToPage();
    }
}
