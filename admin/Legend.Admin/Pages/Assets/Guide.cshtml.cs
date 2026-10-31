using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Assets;

[Authorize(Policy = "AnyStaff")]
public class GuideModel : PageModel
{
    public void OnGet() { }
}
