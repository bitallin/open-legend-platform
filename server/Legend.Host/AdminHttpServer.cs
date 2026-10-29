using System.Net;
using System.Text;
using System.Text.Json;
using Legend.Core;
using Legend.Storage;

namespace Legend.Host;

/// <summary>
/// Lightweight HttpListener admin API (bypass port next to the game gate).
/// Auth: Authorization: Bearer &lt;token&gt; or X-Admin-Token: &lt;token&gt;.
/// </summary>
public sealed class AdminHttpServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly LegendServerEngine _engine;
    private readonly string _token;
    private readonly string _prefix;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int BoundPort { get; private set; }

    public AdminHttpServer(LegendServerEngine engine, int port, string token, string bindIp = "127.0.0.1")
    {
        _engine = engine;
        _token = token ?? string.Empty;
        if (port < 0)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(_token))
            throw new ArgumentException("Admin token must not be empty when admin HTTP is enabled.", nameof(token));

        int listenPort = port;
        if (listenPort == 0)
        {
            var tmp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tmp.Start();
            listenPort = ((IPEndPoint)tmp.LocalEndpoint).Port;
            tmp.Stop();
        }

        BoundPort = listenPort;
        _prefix = $"http://{bindIp}:{listenPort}/";
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        StructuredLogger.Info("AdminHttp", $"Admin API listening on {_prefix}");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(() => HandleAsync(ctx, ct), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            string path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            string method = ctx.Request.HttpMethod.ToUpperInvariant();

            // Merchant webhook / notify: signature auth (not admin token)
            if (method == "POST" && path == "/admin/payment/webhook")
            {
                await HandlePaymentWebhookAsync(ctx, ct);
                return;
            }
            if (method == "POST" && path == "/admin/payment/wechat/notify")
            {
                await HandleWechatNotifyAsync(ctx, ct);
                return;
            }
            if (method == "POST" && path == "/admin/payment/alipay/notify")
            {
                await HandleAlipayNotifyAsync(ctx, ct);
                return;
            }

            if (!IsAuthorized(ctx.Request))
            {
                await WriteJsonAsync(ctx.Response, 401, new { ok = false, error = "unauthorized" }, ct);
                return;
            }

            if (method == "GET" && path == "/admin/online")
            {
                var players = _engine.GameWorldModule.GetOnlineSummaries();
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    ok = true,
                    online = players.Count,
                    players = players.Select(p => new
                    {
                        characterId = p.CharacterId,
                        name = p.Name,
                        accountId = p.AccountId,
                        mapId = p.MapId,
                        level = p.Level,
                        guildId = p.GuildId,
                        classId = p.ClassId
                    })
                }, ct);
                return;
            }

            if (method == "POST" && path == "/admin/ban")
            {
                await HandleBanAsync(ctx, ct);
                return;
            }

            if (method == "POST" && path == "/admin/mute")
            {
                await HandleMuteAsync(ctx, ct);
                return;
            }

            if (method == "POST" && path == "/admin/siege")
            {
                await HandleSiegeAsync(ctx, ct);
                return;
            }

            if (method == "POST" && path == "/admin/reload-pack")
            {
                await HandleReloadPackAsync(ctx, ct);
                return;
            }

            if (method == "POST" && path == "/admin/recharge")
            {
                await HandleRechargeAsync(ctx, ct);
                return;
            }
            if (method == "POST" && path == "/admin/payment/sandbox-sim/build-notify")
            {
                await HandleSandboxSimBuildNotifyAsync(ctx, ct);
                return;
            }

            await WriteJsonAsync(ctx.Response, 404, new { ok = false, error = "not_found" }, ct);
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("AdminHttp", $"Handler error: {ex.Message}");
            try { await WriteJsonAsync(ctx.Response, 500, new { ok = false, error = "internal", message = ex.Message }, ct); }
            catch { /* ignore */ }
        }
    }

    private bool IsAuthorized(HttpListenerRequest req)
    {
        string? header = req.Headers["Authorization"];
        if (!string.IsNullOrEmpty(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            string tok = header["Bearer ".Length..].Trim();
            return string.Equals(tok, _token, StringComparison.Ordinal);
        }

        string? x = req.Headers["X-Admin-Token"];
        return !string.IsNullOrEmpty(x) && string.Equals(x.Trim(), _token, StringComparison.Ordinal);
    }

    private async Task HandleBanAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var body = await ReadBodyAsync<BanMuteBody>(ctx.Request, ct) ?? new BanMuteBody();
        var account = await ResolveAccountAsync(body, ct);
        if (account is null)
        {
            await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "account_not_found" }, ct);
            return;
        }

        bool banned = body.Banned ?? true;
        long expires = 0;
        if (banned && body.DurationSeconds is > 0)
            expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + body.DurationSeconds.Value * 1000L;

        string reason = body.Reason ?? (banned ? "admin ban" : "");
        await _engine.Storage.Moderation.UpsertBanAsync(account.AccountId, banned, reason, expires, ct);

        if (banned)
            await _engine.GameWorldModule.KickAccountAsync(account.AccountId, reason, ct);

        var rec = await _engine.Storage.Moderation.GetByAccountIdAsync(account.AccountId, ct);
        await WriteJsonAsync(ctx.Response, 200, new
        {
            ok = true,
            accountId = account.AccountId,
            username = account.Username,
            banned = rec?.IsBanActive() ?? false,
            reason = rec?.BanReason ?? "",
            expiresUnixMs = rec?.BanExpiresUnixMs ?? 0
        }, ct);
    }

    private async Task HandleMuteAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var body = await ReadBodyAsync<BanMuteBody>(ctx.Request, ct) ?? new BanMuteBody();
        var account = await ResolveAccountAsync(body, ct);
        if (account is null)
        {
            await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "account_not_found" }, ct);
            return;
        }

        bool muted = body.Muted ?? true;
        long expires = 0;
        if (muted && body.DurationSeconds is > 0)
            expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + body.DurationSeconds.Value * 1000L;

        string reason = body.Reason ?? (muted ? "admin mute" : "");
        await _engine.Storage.Moderation.UpsertMuteAsync(account.AccountId, muted, reason, expires, ct);
        await _engine.GameWorldModule.NotifyMuteAsync(account.AccountId, muted, reason, ct);

        var rec = await _engine.Storage.Moderation.GetByAccountIdAsync(account.AccountId, ct);
        await WriteJsonAsync(ctx.Response, 200, new
        {
            ok = true,
            accountId = account.AccountId,
            username = account.Username,
            muted = rec?.IsMuteActive() ?? false,
            reason = rec?.MuteReason ?? "",
            expiresUnixMs = rec?.MuteExpiresUnixMs ?? 0
        }, ct);
    }

    private async Task HandleSiegeAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var body = await ReadBodyAsync<SiegeBody>(ctx.Request, ct) ?? new SiegeBody();
        string action = body.Action ?? "start";
        var (ok, message, isActive, ownerId, ownerName) =
            await _engine.GameWorldModule.AdminSetSiegeAsync(action, ct);
        await WriteJsonAsync(ctx.Response, ok ? 200 : 400, new
        {
            ok,
            message,
            isActive,
            ownerGuildId = ownerId,
            ownerGuildName = ownerName,
            action
        }, ct);
    }

    private async Task HandleReloadPackAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var body = await ReadBodyAsync<ReloadBody>(ctx.Request, ct) ?? new ReloadBody();
        var result = await _engine.ReloadVersionPackAsync(body.ManifestPath, ct);
        await WriteJsonAsync(ctx.Response, result.Ok ? 200 : 500, new
        {
            ok = result.Ok,
            message = result.Message,
            oldHash = result.OldHash,
            newHash = result.NewHash,
            versionId = result.VersionId
        }, ct);
    }

    private async Task<AccountRecord?> ResolveAccountAsync(BanMuteBody body, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body.AccountId))
            return await _engine.Storage.Accounts.GetByIdAsync(body.AccountId.Trim(), ct);

        if (!string.IsNullOrWhiteSpace(body.Username))
            return await _engine.Storage.Accounts.GetByUsernameAsync(body.Username.Trim(), ct);

        if (!string.IsNullOrWhiteSpace(body.CharacterName))
        {
            var ch = await _engine.Storage.Characters.GetByNameAsync(body.CharacterName.Trim(), ct);
            if (ch is null) return null;
            return await _engine.Storage.Accounts.GetByIdAsync(ch.AccountId, ct);
        }

        if (!string.IsNullOrWhiteSpace(body.CharacterId))
        {
            var ch = await _engine.Storage.Characters.GetByIdAsync(body.CharacterId.Trim(), ct);
            if (ch is null) return null;
            return await _engine.Storage.Accounts.GetByIdAsync(ch.AccountId, ct);
        }

        return null;
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpListenerRequest req, CancellationToken ct)
    {
        if (req.ContentLength64 == 0 && !req.HasEntityBody)
            return default;
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        string json = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
            return default;
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse resp, int status, object payload, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
        resp.StatusCode = status;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, ct);
        resp.OutputStream.Close();
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }
        try { _listener?.Close(); } catch { /* ignore */ }
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        }
        _cts?.Dispose();
    }


    private async Task HandlePaymentWebhookAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        string raw;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
            raw = await reader.ReadToEndAsync(ct);

        PaymentWebhookBody? body = null;
        try { body = JsonSerializer.Deserialize<PaymentWebhookBody>(raw, JsonOpts); }
        catch { /* ignore */ }
        body ??= new PaymentWebhookBody();

        string orderId = body.OrderId ?? "";
        string txn = body.ProviderTxnId ?? body.TxnId ?? "";
        string status = (body.Status ?? "paid").Trim().ToLowerInvariant();
        string sig = ctx.Request.Headers["X-Payment-Signature"] ?? body.Signature ?? "";

        string canonical = $"{orderId}|{txn}|{status}";
        var provider = _engine.GameWorldModule.PaymentProvider;
        if (!provider.VerifyWebhookSignature(canonical, sig))
        {
            await WriteJsonAsync(ctx.Response, 401, new { ok = false, error = "bad_signature" }, ct);
            return;
        }

        if (status is not ("paid" or "success"))
        {
            await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "unsupported_status", status }, ct);
            return;
        }

        var (ok, msg, order) = await _engine.GameWorldModule.FulfillPaymentOrderAsync(orderId, txn, requirePaidTransition: true, ct);
        await WriteJsonAsync(ctx.Response, ok ? 200 : 400, new
        {
            ok,
            message = msg,
            orderId = order?.OrderId,
            status = order?.Status,
            mailId = order?.FulfilledMailId
        }, ct);
    }

    private async Task HandleWechatNotifyAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        string raw;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
            raw = await reader.ReadToEndAsync(ct);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? key in ctx.Request.Headers.AllKeys ?? Array.Empty<string>())
        {
            if (key is null) continue;
            headers[key] = ctx.Request.Headers[key] ?? "";
        }

        var provider = _engine.PaymentProviders.Wechat;
        var parsed = await provider.ParseAndVerifyNotifyAsync(raw, headers, ct);
        if (!parsed.SignatureValid)
        {
            await WriteRawAsync(ctx.Response, 401, parsed.ContentType, parsed.FailureResponseBody, ct);
            return;
        }
        if (!parsed.Paid)
        {
            await WriteRawAsync(ctx.Response, 200, parsed.ContentType, parsed.SuccessResponseBody, ct);
            return;
        }

        var (ok, msg, order) = await _engine.GameWorldModule.FulfillPaymentOrderAsync(
            parsed.OrderId, parsed.ProviderTxnId, requirePaidTransition: true, ct, paidAmountCents: parsed.AmountCents);
        if (!ok)
        {
            StructuredLogger.Error("AdminHttp", $"WeChat fulfill failed: {msg} order={parsed.OrderId}");
            await WriteRawAsync(ctx.Response, 500, parsed.ContentType, parsed.FailureResponseBody, ct);
            return;
        }
        await WriteRawAsync(ctx.Response, 200, parsed.ContentType, parsed.SuccessResponseBody, ct);
    }

    private async Task HandleAlipayNotifyAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        string raw;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
            raw = await reader.ReadToEndAsync(ct);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? key in ctx.Request.Headers.AllKeys ?? Array.Empty<string>())
        {
            if (key is null) continue;
            headers[key] = ctx.Request.Headers[key] ?? "";
        }

        var provider = _engine.PaymentProviders.Alipay;
        var parsed = await provider.ParseAndVerifyNotifyAsync(raw, headers, ct);
        if (!parsed.SignatureValid)
        {
            await WriteRawAsync(ctx.Response, 401, parsed.ContentType, parsed.FailureResponseBody, ct);
            return;
        }
        if (!parsed.Paid)
        {
            await WriteRawAsync(ctx.Response, 200, parsed.ContentType, parsed.SuccessResponseBody, ct);
            return;
        }

        var (ok, msg, order) = await _engine.GameWorldModule.FulfillPaymentOrderAsync(
            parsed.OrderId, parsed.ProviderTxnId, requirePaidTransition: true, ct, paidAmountCents: parsed.AmountCents);
        if (!ok)
        {
            StructuredLogger.Error("AdminHttp", $"Alipay fulfill failed: {msg} order={parsed.OrderId}");
            await WriteRawAsync(ctx.Response, 500, parsed.ContentType, parsed.FailureResponseBody, ct);
            return;
        }
        await WriteRawAsync(ctx.Response, 200, parsed.ContentType, parsed.SuccessResponseBody, ct);
    }

    private static async Task WriteRawAsync(HttpListenerResponse resp, int status, string contentType, string body, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
        resp.StatusCode = status;
        resp.ContentType = string.IsNullOrWhiteSpace(contentType) ? "text/plain; charset=utf-8" : contentType;
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, ct);
        resp.OutputStream.Close();
    }

    private async Task HandleRechargeAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var body = await ReadBodyAsync<RechargeBody>(ctx.Request, ct) ?? new RechargeBody();
        if (string.IsNullOrWhiteSpace(body.ProductId))
        {
            await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "product_id_required" }, ct);
            return;
        }

        var (ok, msg, order) = await _engine.GameWorldModule.AdminRechargeAsync(
            body.CharacterId, body.CharacterName, body.ProductId!, ct);
        await WriteJsonAsync(ctx.Response, ok ? 200 : 400, new
        {
            ok,
            message = msg,
            orderId = order?.OrderId,
            status = order?.Status,
            mailId = order?.FulfilledMailId,
            characterId = order?.CharacterId
        }, ct);
    }


    private async Task HandleSandboxSimBuildNotifyAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var body = await ReadBodyAsync<SandboxSimBody>(ctx.Request, ct) ?? new SandboxSimBody();
        string provider = (body.Provider ?? "").Trim().ToLowerInvariant();
        string orderId = body.OrderId ?? "";
        string txn = string.IsNullOrWhiteSpace(body.ProviderTxnId) ? ("sim-" + Guid.NewGuid().ToString("N")) : body.ProviderTxnId!;
        int amount = body.AmountCents ?? 0;
        if (string.IsNullOrWhiteSpace(orderId) || amount <= 0)
        {
            await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "orderId_and_amountCents_required" }, ct);
            return;
        }

        if (provider == "wechat")
        {
            var sim = _engine.PaymentProviders.Wechat.Simulator;
            if (sim is null)
            {
                await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "wechat_not_in_sandbox_sim" }, ct);
                return;
            }
            var (raw, headers) = sim.BuildPaidNotify(orderId, txn, amount);
            await WriteJsonAsync(ctx.Response, 200, new { ok = true, mode = "sandbox_sim", provider, body = raw, headers, notifyPath = "/admin/payment/wechat/notify" }, ct);
            return;
        }
        if (provider == "alipay")
        {
            var sim = _engine.PaymentProviders.Alipay.Simulator;
            if (sim is null)
            {
                await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "alipay_not_in_sandbox_sim" }, ct);
                return;
            }
            var (raw, headers) = sim.BuildPaidNotify(orderId, txn, amount);
            await WriteJsonAsync(ctx.Response, 200, new { ok = true, mode = "sandbox_sim", provider, body = raw, headers, notifyPath = "/admin/payment/alipay/notify" }, ct);
            return;
        }
        await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = "provider_must_be_wechat_or_alipay" }, ct);
    }

    private sealed class BanMuteBody
    {
        public string? AccountId { get; set; }
        public string? Username { get; set; }
        public string? CharacterName { get; set; }
        public string? CharacterId { get; set; }
        public bool? Banned { get; set; }
        public bool? Muted { get; set; }
        public string? Reason { get; set; }
        public long? DurationSeconds { get; set; }
    }

    private sealed class SiegeBody
    {
        public string? Action { get; set; }
    }

    private sealed class ReloadBody
    {
        public string? ManifestPath { get; set; }
    }

    private sealed class PaymentWebhookBody
    {
        public string? OrderId { get; set; }
        public string? ProviderTxnId { get; set; }
        public string? TxnId { get; set; }
        public string? Status { get; set; }
        public string? Signature { get; set; }
    }

    private sealed class RechargeBody
    {
        public string? CharacterId { get; set; }
        public string? CharacterName { get; set; }
        public string? ProductId { get; set; }
    }

    private sealed class SandboxSimBody
    {
        public string? Provider { get; set; }
        public string? OrderId { get; set; }
        public string? ProviderTxnId { get; set; }
        public int? AmountCents { get; set; }
    }
}
