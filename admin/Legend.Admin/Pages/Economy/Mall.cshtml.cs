using Legend.Admin.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legend.Admin.Pages.Economy;

public class MallModel(VersionPackService packs) : PageModel
{
    public string PackId { get; set; } = "sample-classic";
    public string ShopsJson { get; set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var (ok, _, content) = await packs.ReadAsync(PackId, "configs/shops.json", ct);
        ShopsJson = ok ? content! : "(无法读取 shops.json)";
    }
}
