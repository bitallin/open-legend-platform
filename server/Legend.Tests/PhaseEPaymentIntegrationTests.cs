using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legend.Contracts;
using Legend.Contracts.Payment;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.CliClient;
using Legend.Storage;
using Xunit;

namespace Legend.Tests;

public sealed class PhaseEPaymentIntegrationTests
{
    private static string RepoRoot()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        if (!File.Exists(Path.Combine(root, "protocol", "VERSION")))
            root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (!File.Exists(Path.Combine(root, "protocol", "VERSION")))
            root = Path.GetFullPath(Directory.GetCurrentDirectory());
        return root;
    }

    private static string GetManifestPath()
    {
        string p = Path.Combine(RepoRoot(), "version-packs", "sample-classic", "manifest.json");
        Assert.True(File.Exists(p), $"manifest not found: {p}");
        return p;
    }

    private static async Task<(LegendTcpClient Client, string CharId, EnterWorldResp Enter)> EnterAsAsync(
        LegendServerEngine engine, string user, string charName, CancellationToken ct = default)
    {
        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort, ct);
        Assert.True((await client.HandshakeAsync("1.21.0", $"pc-{user}", ct)).Accepted);
        await client.RegisterAsync(user, "secret12345", "PC", ct);
        var login = await client.LoginAsync(user, "secret12345", "PC", ct);
        Assert.True(login.Success, login.Message);
        var create = await client.CreateCharAsync(charName, "warrior", ct);
        string charId = create.Success
            ? create.Character.CharacterId
            : (await client.CharListAsync(ct)).Characters.First(c => c.Name == charName).CharacterId;
        await client.SelectCharAsync(charId, ct);
        var realms = await client.GetRealmListAsync(ct);
        var enter = await client.EnterWorldAsync(realms.Realms[0].RealmId, charId, ct);
        Assert.True(enter.Success, enter.Message);
        return (client, charId, enter);
    }

    private static string HmacSign(string secret, string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static async Task PostNotifyAsync(HttpClient http, string path, string body, Dictionary<string, string> headers)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Content = new StringContent(body, Encoding.UTF8, headers.GetValueOrDefault("Content-Type", "application/json"));
        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public void Protocol_Version_Is_1_21()
    {
        Assert.Equal("1.21.0", ProtocolConstants.CurrentVersion);
        Assert.Equal("1.21.0", File.ReadAllText(Path.Combine(RepoRoot(), "protocol", "VERSION")).Trim());
        var pack = new VersionPackLoader().LoadFromManifest(GetManifestPath());
        Assert.Equal("1.21.0", pack.Manifest.ProtocolVersion);
    }

    [Fact]
    public async Task Wechat_Notify_BadSignature_Rejected()
    {
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            paymentSecret: "e-secret", paymentProvider: "wechat");
        await engine.StartAsync();
        await engine.StartAdminAsync(0, "e-admin", "127.0.0.1");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
        try
        {
            var (client, _, _) = await EnterAsAsync(engine, "ewxbad", "EWxBad");
            var create = await client.PaymentCreateOrderAsync("gold_100");
            Assert.True(create.Success, create.Message);
            Assert.Equal("wechat", create.ProviderId);
            Assert.False(string.IsNullOrWhiteSpace(create.CheckoutUrl));

            var bad = await http.PostAsync("/admin/payment/wechat/notify",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
            await client.DisposeAsync();
        }
        finally { await engine.DisposeAsync(); }
    }

    [Fact]
    public async Task Wechat_Notify_Success_Fulfill_And_Idempotent()
    {
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            paymentSecret: "e-secret", paymentProvider: "wechat");
        await engine.StartAsync();
        await engine.StartAdminAsync(0, "e-admin", "127.0.0.1");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
        try
        {
            var (client, _, enter) = await EnterAsAsync(engine, "ewxok", "EWxOk");
            int goldBefore = enter.Gold;
            var create = await client.PaymentCreateOrderAsync("gold_100");
            Assert.True(create.Success, create.Message);
            Assert.True(create.PayPayload.Contains("sandbox_sim") || create.CheckoutUrl.Contains("weixin"));

            var sim = engine.PaymentProviders.Wechat.Simulator!;
            Assert.NotNull(sim);
            string txn = "wxtxn-" + Guid.NewGuid().ToString("N");
            var (body, headers) = sim.BuildPaidNotify(create.OrderId, txn, amountCents: 100);

            var req = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/wechat/notify");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            foreach (var kv in headers)
                if (!string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            var ok = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Contains("SUCCESS", await ok.Content.ReadAsStringAsync());

            var notify = await client.WaitPaymentFulfilledAsync(5000);
            Assert.Equal(create.OrderId, notify.OrderId);

            // idempotent
            var (body2, headers2) = sim.BuildPaidNotify(create.OrderId, txn, amountCents: 100);
            var req2 = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/wechat/notify");
            req2.Content = new StringContent(body2, Encoding.UTF8, "application/json");
            foreach (var kv in headers2)
                if (!string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    req2.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            var ok2 = await http.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, ok2.StatusCode);

            var st = await client.PaymentOrderStatusAsync(create.OrderId);
            Assert.Equal("Fulfilled", st.Status);
            var claim = await client.MailClaimAsync(st.FulfilledMailId);
            Assert.True(claim.Success, claim.Message);
            Assert.Equal(goldBefore + 100, claim.Gold);
            await client.DisposeAsync();
        }
        finally { await engine.DisposeAsync(); }
    }

    [Fact]
    public async Task Wechat_AmountMismatch_Rejected()
    {
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            paymentProvider: "wechat");
        await engine.StartAsync();
        await engine.StartAdminAsync(0, "e-admin", "127.0.0.1");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
        try
        {
            var (client, _, _) = await EnterAsAsync(engine, "ewxamt", "EWxAmt");
            var create = await client.PaymentCreateOrderAsync("gold_100");
            var sim = engine.PaymentProviders.Wechat.Simulator!;
            var (body, headers) = sim.BuildPaidNotify(create.OrderId, "txn-amt", amountCents: 9999);
            var req = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/wechat/notify");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            foreach (var kv in headers)
                if (!string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
            var st = await client.PaymentOrderStatusAsync(create.OrderId);
            Assert.Equal("Pending", st.Status);
            await client.DisposeAsync();
        }
        finally { await engine.DisposeAsync(); }
    }

    [Fact]
    public async Task Alipay_Notify_BadSignature_Rejected()
    {
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            paymentProvider: "alipay");
        await engine.StartAsync();
        await engine.StartAdminAsync(0, "e-admin", "127.0.0.1");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
        try
        {
            var (client, _, _) = await EnterAsAsync(engine, "ealibad", "EAliBad");
            var create = await client.PaymentCreateOrderAsync("gold_100");
            Assert.Equal("alipay", create.ProviderId);
            var bad = await http.PostAsync("/admin/payment/alipay/notify",
                new StringContent("out_trade_no=x&trade_status=TRADE_SUCCESS&sign=bad", Encoding.UTF8,
                    "application/x-www-form-urlencoded"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
            await client.DisposeAsync();
        }
        finally { await engine.DisposeAsync(); }
    }

    [Fact]
    public async Task Alipay_Notify_Success_Fulfill_And_Idempotent()
    {
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            paymentProvider: "alipay");
        await engine.StartAsync();
        await engine.StartAdminAsync(0, "e-admin", "127.0.0.1");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
        try
        {
            var (client, _, enter) = await EnterAsAsync(engine, "ealiok", "EAliOk");
            int goldBefore = enter.Gold;
            var create = await client.PaymentCreateOrderAsync("gold_100");
            Assert.True(create.Success, create.Message);
            Assert.Contains("sandbox_sim", create.PayPayload);

            var sim = engine.PaymentProviders.Alipay.Simulator!;
            string txn = "alitxn-" + Guid.NewGuid().ToString("N");
            var (body, headers) = sim.BuildPaidNotify(create.OrderId, txn, amountCents: 100);

            var req = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/alipay/notify");
            req.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            var ok = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("success", (await ok.Content.ReadAsStringAsync()).Trim());

            var notify = await client.WaitPaymentFulfilledAsync(5000);
            Assert.Equal(create.OrderId, notify.OrderId);

            var (body2, _) = sim.BuildPaidNotify(create.OrderId, txn, amountCents: 100);
            var req2 = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/alipay/notify");
            req2.Content = new StringContent(body2, Encoding.UTF8, "application/x-www-form-urlencoded");
            Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(req2)).StatusCode);

            var st = await client.PaymentOrderStatusAsync(create.OrderId);
            Assert.Equal("Fulfilled", st.Status);
            var claim = await client.MailClaimAsync(st.FulfilledMailId);
            Assert.True(claim.Success);
            Assert.Equal(goldBefore + 100, claim.Gold);
            await client.DisposeAsync();
        }
        finally { await engine.DisposeAsync(); }
    }

    [Fact]
    public async Task Alipay_AmountMismatch_Rejected()
    {
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            paymentProvider: "alipay");
        await engine.StartAsync();
        await engine.StartAdminAsync(0, "e-admin", "127.0.0.1");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
        try
        {
            var (client, _, _) = await EnterAsAsync(engine, "ealiamt", "EAliAmt");
            var create = await client.PaymentCreateOrderAsync("gold_100");
            var sim = engine.PaymentProviders.Alipay.Simulator!;
            var (body, _) = sim.BuildPaidNotify(create.OrderId, "txn-bad-amt", amountCents: 1);
            var req = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/alipay/notify");
            req.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
            Assert.Equal("Pending", (await client.PaymentOrderStatusAsync(create.OrderId)).Status);
            await client.DisposeAsync();
        }
        finally { await engine.DisposeAsync(); }
    }

    [Fact]
    public async Task Mock_Webhook_Regression_Still_Works()
    {
        const string secret = "e-mock-secret";
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            paymentSecret: secret, paymentProvider: "mock");
        await engine.StartAsync();
        await engine.StartAdminAsync(0, "e-admin", "127.0.0.1");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
        try
        {
            var (client, _, _) = await EnterAsAsync(engine, "emock", "EMock");
            var create = await client.PaymentCreateOrderAsync("gold_100");
            Assert.Equal("mock", create.ProviderId);
            Assert.False(string.IsNullOrWhiteSpace(create.MockPayToken));
            string txn = "mocktxn";
            string canonical = $"{create.OrderId}|{txn}|paid";
            string sig = HmacSign(secret, canonical);
            var req = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/webhook");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { orderId = create.OrderId, providerTxnId = txn, status = "paid" }),
                Encoding.UTF8, "application/json");
            req.Headers.Add("X-Payment-Signature", sig);
            var ok = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("Fulfilled", (await client.PaymentOrderStatusAsync(create.OrderId)).Status);
            await client.DisposeAsync();
        }
        finally { await engine.DisposeAsync(); }
    }

    [Fact]
    public void LocalSimulators_Crypto_Roundtrip()
    {
        using var wx = new LocalWechatSandboxSimulator();
        var (body, headers) = wx.BuildPaidNotify("ord1", "txn1", 100);
        var parsed = wx.ParseAndVerifyNotify(body, headers);
        Assert.True(parsed.SignatureValid);
        Assert.True(parsed.Paid);
        Assert.Equal("ord1", parsed.OrderId);
        Assert.Equal(100, parsed.AmountCents);

        using var ali = new LocalAlipaySandboxSimulator();
        var (abody, _) = ali.BuildPaidNotify("ord2", "txn2", 50);
        var ap = ali.ParseAndVerifyNotify(abody);
        Assert.True(ap.SignatureValid);
        Assert.True(ap.Paid);
        Assert.Equal("ord2", ap.OrderId);
        Assert.Equal(50, ap.AmountCents);
    }
}
