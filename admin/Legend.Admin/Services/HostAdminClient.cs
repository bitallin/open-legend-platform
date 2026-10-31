using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Legend.Admin.Services;

public sealed class HostAdminOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:7101";
    public string Token { get; set; } = "";
}

public sealed class HostAdminClient
{
    private readonly HttpClient _http;
    private readonly HostAdminOptions _opts;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public HostAdminClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _opts = new HostAdminOptions
        {
            BaseUrl = config["Legend:HostAdmin:BaseUrl"] ?? "http://127.0.0.1:7101",
            Token = config["Legend:HostAdmin:Token"] ?? Environment.GetEnvironmentVariable("LEGEND_ADMIN_TOKEN") ?? ""
        };
        if (_http.BaseAddress is null && Uri.TryCreate(_opts.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var u))
            _http.BaseAddress = u;
    }

    public string BaseUrl => _opts.BaseUrl;
    public bool HasToken => !string.IsNullOrWhiteSpace(_opts.Token);

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_opts.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.Token);
    }

    public async Task<(bool ok, int status, string body)> GetOnlineAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "admin/online");
        ApplyAuth(req);
        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (Exception ex) { return (false, 0, ex.Message); }
    }

    public async Task<(bool ok, int status, string body)> PostJsonAsync(string relativePath, object payload, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativePath.TrimStart('/'));
        ApplyAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (Exception ex) { return (false, 0, ex.Message); }
    }
}
