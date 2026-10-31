using Legend.Admin.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Account;

public class LogoutModel(AuditService audit) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var name = User.Identity?.Name ?? "?";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await audit.WriteAsync(name, "auth.logout", name, ip: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        return RedirectToPage("/Account/Login");
    }
}
