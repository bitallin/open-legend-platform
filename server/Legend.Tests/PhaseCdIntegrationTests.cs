using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.CliClient;
using Legend.Merge;
using Legend.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Legend.Tests;

public sealed class PhaseCdIntegrationTests
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

    private static string GetRealmsDir()
    {
        string d = Path.Combine(RepoRoot(), "realms");
        Assert.True(Directory.Exists(d), $"realms dir missing: {d}");
        return d;
    }

    private static async Task<(LegendTcpClient Client, string CharId, EnterWorldResp Enter, string AccountId)> EnterAsAsync(
        LegendServerEngine engine, string user, string charName, string cls, CancellationToken ct = default)
    {
        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort, ct);
        Assert.True((await client.HandshakeAsync("1.20.0", $"pc-{user}", ct)).Accepted);
        await client.RegisterAsync(user, "secret12345", "PC", ct);
        var login = await client.LoginAsync(user, "secret12345", "PC", ct);
        Assert.True(login.Success, login.Message);
        var create = await client.CreateCharAsync(charName, cls, ct);
        string charId;
        if (create.Success)
            charId = create.Character.CharacterId;
        else
        {
            var list = await client.CharListAsync(ct);
            charId = list.Characters.First(c => c.Name == charName).CharacterId;
        }
        await client.SelectCharAsync(charId, ct);
        var realms = await client.GetRealmListAsync(ct);
        Assert.True(realms.Success && realms.Realms.Count > 0);
        var enter = await client.EnterWorldAsync(realms.Realms[0].RealmId, charId, ct);
        Assert.True(enter.Success, enter.Message);
        return (client, charId, enter, login.AccountId);
    }

    private static HttpClient CreateAdminClient(int adminPort, string token)
    {
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{adminPort}/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static string HmacSign(string secret, string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    [Fact]
    public void Protocol_Version_Is_1_20()
    {
        Assert.Equal("1.20.0", ProtocolConstants.CurrentVersion);
        Assert.Equal("1.20.0", ProtocolConstants.MinCompatibleVersion);
        var pack = new VersionPackLoader().LoadFromManifest(GetManifestPath());
        Assert.Equal("1.20.0", pack.Manifest.ProtocolVersion);
        Assert.Equal(MsgId.PaymentCreateOrderReq, (MsgId)3801);
        Assert.Equal(MsgId.PaymentFulfilledNotify, (MsgId)3805);
    }

    [Fact]
    public void RealmCatalog_Loads_Multi_Realms()
    {
        var list = RealmCatalogLoader.Load(GetRealmsDir());
        Assert.True(list.Count >= 2, $"expected >=2 realms, got {list.Count}");
        Assert.Contains(list, r => r.RealmId == "realm-classic-01");
        Assert.Contains(list, r => r.RealmId == "realm-classic-02");
        Assert.Equal(7100, list.First(r => r.RealmId == "realm-classic-01").Port);
        Assert.Equal(7200, list.First(r => r.RealmId == "realm-classic-02").Port);
    }

    [Fact]
    public async Task RealmList_Returns_Catalog_Zones()
    {
        var catalog = RealmCatalogLoader.Load(GetRealmsDir());
        var local = catalog.First(r => r.RealmId == "realm-classic-01");
        var storage = new InMemoryStorage();
        var engine = new LegendServerEngine(
            GetManifestPath(), port: 0, IPAddress.Loopback, storage,
            realmCatalog: catalog, localRealm: local, paymentSecret: "cd-test-secret");
        await engine.StartAsync();
        try
        {
            var client = new LegendTcpClient();
            await client.ConnectAsync("127.0.0.1", engine.BoundPort);
            Assert.True((await client.HandshakeAsync("1.20.0", "pc-cd-realm", default)).Accepted);
            await client.RegisterAsync("cd_realm_user", "secret12345", "PC");
            var login = await client.LoginAsync("cd_realm_user", "secret12345", "PC");
            Assert.True(login.Success, login.Message);
            var realms = await client.GetRealmListAsync();
            Assert.True(realms.Success);
            Assert.True(realms.Realms.Count >= 2);
            Assert.Contains(realms.Realms, r => r.RealmId == "realm-classic-01");
            Assert.Contains(realms.Realms, r => r.RealmId == "realm-classic-02");
            // Local realm should report live bound port
            var localInfo = realms.Realms.First(r => r.RealmId == "realm-classic-01");
            Assert.Equal(engine.BoundPort, localInfo.Port);
            await client.DisposeAsync();
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Payment_Create_Webhook_Fulfill_Idempotent()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-cd-pay-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { /* ignore */ }
        const string token = "cd-admin-token";
        const string secret = "cd-pay-secret";

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage, paymentSecret: secret);
        await engine.StartAsync();
        await engine.StartAdminAsync(0, token, "127.0.0.1");

        using var httpAdmin = CreateAdminClient(engine.AdminBoundPort, token);
        using var httpNoAuth = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };

        try
        {
            var (client, charId, enter, _) = await EnterAsAsync(engine, "cdpay", "CdPayHero", "warrior");
            int goldBefore = enter.Gold;

            var create = await client.PaymentCreateOrderAsync("gold_100");
            Assert.True(create.Success, create.Message);
            Assert.False(string.IsNullOrWhiteSpace(create.OrderId));
            Assert.Equal("Pending", create.Status);
            Assert.False(string.IsNullOrWhiteSpace(create.MockPayToken));

            string orderId = create.OrderId;
            string txn = $"txn-{Guid.NewGuid():N}";
            string status = "paid";
            string canonical = $"{orderId}|{txn}|{status}";
            string sig = HmacSign(secret, canonical);

            // Bad signature rejected
            var bad = await httpNoAuth.PostAsync("/admin/payment/webhook",
                new StringContent(JsonSerializer.Serialize(new { orderId, providerTxnId = txn, status }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

            var req = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/webhook");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { orderId, providerTxnId = txn, status }),
                Encoding.UTF8, "application/json");
            req.Headers.Add("X-Payment-Signature", sig);
            var ok1 = await httpNoAuth.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, ok1.StatusCode);
            using (var doc = JsonDocument.Parse(await ok1.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("Fulfilled", doc.RootElement.GetProperty("status").GetString());
            }

            var notify = await client.WaitPaymentFulfilledAsync(5000);
            Assert.Equal(orderId, notify.OrderId);
            Assert.Equal(100, notify.RewardGold);

            // Idempotent second webhook
            string sig2 = HmacSign(secret, canonical);
            var req2 = new HttpRequestMessage(HttpMethod.Post, "/admin/payment/webhook");
            req2.Content = new StringContent(
                JsonSerializer.Serialize(new { orderId, providerTxnId = txn, status }),
                Encoding.UTF8, "application/json");
            req2.Headers.Add("X-Payment-Signature", sig2);
            var ok2 = await httpNoAuth.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, ok2.StatusCode);
            using (var doc = JsonDocument.Parse(await ok2.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Contains("already", doc.RootElement.GetProperty("message").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
            }

            var st = await client.PaymentOrderStatusAsync(orderId);
            Assert.True(st.Success);
            Assert.Equal("Fulfilled", st.Status);
            Assert.False(string.IsNullOrWhiteSpace(st.FulfilledMailId));

            var mails = await client.MailListAsync();
            Assert.Contains(mails.Mails, m => m.MailId == st.FulfilledMailId);
            var claim = await client.MailClaimAsync(st.FulfilledMailId);
            Assert.True(claim.Success, claim.Message);
            Assert.Equal(goldBefore + 100, claim.Gold);

            // Admin recharge loop
            var rechargeBody = new StringContent(
                JsonSerializer.Serialize(new { characterName = "CdPayHero", productId = "gold_100" }),
                Encoding.UTF8, "application/json");
            var recharge = await httpAdmin.PostAsync("/admin/recharge", rechargeBody);
            Assert.Equal(HttpStatusCode.OK, recharge.StatusCode);
            using (var doc = JsonDocument.Parse(await recharge.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("Fulfilled", doc.RootElement.GetProperty("status").GetString());
            }

            await client.DisposeAsync();
        }
        finally
        {
            await engine.DisposeAsync();
            try { File.Delete(dbPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Merge_DryRun_And_Real_Rename_SameAccount()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"legend-cd-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbA = Path.Combine(dir, "a.db");
        string dbB = Path.Combine(dir, "b.db");
        string dbOut = Path.Combine(dir, "merged.db");
        string dbDry = Path.Combine(dir, "dry.db");

        try
        {
            await SeedRealmDbAsync(dbA, "sameuser", "HeroA", "GuildAlpha");
            await SeedRealmDbAsync(dbB, "sameuser", "HeroA", "GuildAlpha"); // same account + char name + guild -> merge/rename

            var dry = await RealmMerger.MergeAsync(dbA, dbB, dbDry, dryRun: true);
            Assert.True(dry.Ok, string.Join(";", dry.Notes));
            Assert.True(dry.DryRun);
            Assert.Equal(1, dry.AccountsFromA);
            Assert.Equal(1, dry.AccountsFromB);
            Assert.Equal(1, dry.AccountsMerged);
            Assert.Equal(2, dry.CharactersWritten);
            Assert.True(dry.CharactersRenamed >= 1);
            Assert.Equal(2, dry.GuildsWritten);
            Assert.True(dry.GuildsRenamed >= 1);
            Assert.False(File.Exists(dbDry)); // dry-run should not create target

            var real = await RealmMerger.MergeAsync(dbA, dbB, dbOut, dryRun: false);
            Assert.True(real.Ok, string.Join(";", real.Notes));
            Assert.False(real.DryRun);
            Assert.True(File.Exists(dbOut));
            Assert.Equal(1, real.AccountsMerged);
            Assert.Equal(2, real.CharactersWritten);
            Assert.Equal(2, real.GuildsWritten);

            await using var conn = new SqliteConnection($"Data Source={dbOut}");
            await conn.OpenAsync();
            async Task<int> Count(string sql)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            Assert.Equal(1, await Count("SELECT COUNT(*) FROM accounts"));
            Assert.Equal(2, await Count("SELECT COUNT(*) FROM characters"));
            Assert.Equal(2, await Count("SELECT COUNT(*) FROM guilds"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM characters WHERE name='HeroA'"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM characters WHERE name LIKE 'HeroA_m%'"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM guilds WHERE name='GuildAlpha'"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM guilds WHERE name LIKE 'GuildAlpha_m%'"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static async Task SeedRealmDbAsync(string dbPath, string username, string charName, string guildName)
    {
        try { File.Delete(dbPath); } catch { /* ignore */ }
        await using var storage = await StorageFactory.CreateAsync(new StorageOptions
        {
            Backend = "Sqlite",
            SqlitePath = dbPath
        });
        await storage.InitializeAsync();
        var (okAcc, errAcc, acc) = await storage.Accounts.RegisterAsync(username, "secret12345");
        Assert.True(okAcc, errAcc);
        var ch = new CharacterRecord
        {
            CharacterId = Guid.NewGuid().ToString("N"),
            AccountId = acc!.AccountId,
            Name = charName,
            ClassId = "warrior",
            Level = 1,
            MapId = "map_start",
            X = 10,
            Y = 10,
            Hp = 100,
            Mp = 50,
            MaxHp = 100,
            MaxMp = 50,
            Gold = 0,
            InventoryJson = "[]",
            QuestJson = "[]",
            EquipmentJson = "{}",
            SkillsJson = "{}",
            DungeonProgressJson = "{}",
            AppearanceJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var (okCh, errCh, created) = await storage.Characters.CreateAsync(ch, maxPerAccount: 10);
        Assert.True(okCh, errCh);
        var (okG, errG, _) = await storage.Guilds.CreateAsync(guildName, created!.CharacterId);
        Assert.True(okG, errG);
    }
}
