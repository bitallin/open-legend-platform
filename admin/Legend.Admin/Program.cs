using System.Security.Claims;
using Legend.Admin.Data;
using Legend.Admin.Models;
using Legend.Admin.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.AddSingleton<RepoPaths>();
builder.Services.AddDbContext<AdminDbContext>((sp, opt) =>
{
    var paths = sp.GetRequiredService<RepoPaths>();
    var dbPath = Path.Combine(paths.AdminDataDir, "admin.db");
    opt.UseSqlite($"Data Source={dbPath}");
});
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<VersionPackService>();
builder.Services.AddScoped<RealmService>();
builder.Services.AddScoped<MediaLibraryService>();
builder.Services.AddScoped<AssetManifestService>();
builder.Services.AddScoped<LookBindingService>();
builder.Services.AddScoped<JsonEntityStore>();
builder.Services.AddScoped<LookThumbResolver>();
builder.Services.AddHttpClient<HostAdminClient>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 32 * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 32 * 1024 * 1024);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Login";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("SuperAdmin", p => p.RequireRole(AdminRoles.SuperAdmin));
    o.AddPolicy("OperatorPlus", p => p.RequireRole(AdminRoles.SuperAdmin, AdminRoles.Operator));
    o.AddPolicy("AnyStaff", p => p.RequireRole(AdminRoles.All));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    await SeedData.EnsureSeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// --- JSON APIs ---
app.MapGet("/api/health", () => Results.Json(new { ok = true, service = "Legend.Admin", time = DateTimeOffset.UtcNow }));

app.MapGet("/api/configs/{packId}/{*relativePath}", async (string packId, string relativePath, VersionPackService packs, CancellationToken ct) =>
{
    var (ok, msg, content) = await packs.ReadAsync(packId, relativePath, ct);
    return ok ? Results.Content(content!, "application/json") : Results.NotFound(new { ok = false, error = msg });
}).RequireAuthorization();

app.MapPost("/api/configs/{packId}/{*relativePath}", async (string packId, string relativePath, HttpRequest req, VersionPackService packs, AuditService audit, ClaimsPrincipal user, CancellationToken ct) =>
{
    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync(ct);
    var note = req.Headers["X-Revision-Note"].ToString();
    var actor = user.Identity?.Name ?? "unknown";
    var (ok, msg, hash) = await packs.WriteAsync(packId, relativePath, body, actor, note, ct);
    if (ok) await audit.WriteAsync(actor, "config.save", $"{packId}/{relativePath}", new { hash, note }, req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
    return ok ? Results.Json(new { ok = true, hash }) : Results.BadRequest(new { ok = false, error = msg });
}).RequireAuthorization("OperatorPlus");

app.MapGet("/api/ops/online", async (HostAdminClient host, CancellationToken ct) =>
{
    var (ok, status, body) = await host.GetOnlineAsync(ct);
    return Results.Content(body, "application/json");
}).RequireAuthorization();

app.MapPost("/api/ops/proxy/{**path}", async (string path, HttpRequest req, HostAdminClient host, AuditService audit, ClaimsPrincipal user, CancellationToken ct) =>
{
    using var reader = new StreamReader(req.Body);
    var raw = await reader.ReadToEndAsync(ct);
    object payload = string.IsNullOrWhiteSpace(raw) ? new { } : System.Text.Json.JsonSerializer.Deserialize<object>(raw)!;
    var (ok, status, body) = await host.PostJsonAsync("admin/" + path, payload, ct);
    await audit.WriteAsync(user.Identity?.Name ?? "?", "ops.proxy", path, new { status, ok }, req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
    return Results.Content(body, "application/json");
}).RequireAuthorization();

app.Run();
