using Microsoft.AspNetCore.Http;

namespace Legend.Admin.Services;

/// <summary>
/// Resolves the active version pack. Prefer explicit query, then cookie, then sample-classic
/// (never bare FirstOrDefault — sample-176 sorts first alphabetically and often has no content).
/// </summary>
public sealed class PackPreference(VersionPackService packs, IHttpContextAccessor http)
{
    public const string CookieName = "legend.admin.pack";
    public const string PreferredId = "sample-classic";

    public IReadOnlyList<PackInfo> List() => packs.ListPacks();

    public string Resolve(string? packId, bool persistWhenExplicit = true)
    {
        var list = packs.ListPacks();
        var ids = new HashSet<string>(list.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(packId) && ids.Contains(packId))
        {
            if (persistWhenExplicit) Persist(packId);
            return packId;
        }

        var ctx = http.HttpContext;
        var cookie = ctx?.Request.Cookies[CookieName];
        if (!string.IsNullOrWhiteSpace(cookie) && ids.Contains(cookie))
            return cookie!;

        if (ids.Contains(PreferredId))
            return PreferredId;

        return list.FirstOrDefault()?.Id ?? PreferredId;
    }

    public string Current() => Resolve(null, persistWhenExplicit: false);

    public void Persist(string packId)
    {
        var ctx = http.HttpContext;
        if (ctx is null || string.IsNullOrWhiteSpace(packId)) return;
        ctx.Response.Cookies.Append(CookieName, packId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(180),
            Path = "/"
        });
    }
}
