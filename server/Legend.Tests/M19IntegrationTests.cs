using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.CliClient;
using Legend.Storage;
using Xunit;

namespace Legend.Tests;

public sealed class M19IntegrationTests
{
    private static string GetManifestPath()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string p = Path.Combine(root, "version-packs", "sample-classic", "manifest.json");
        if (!File.Exists(p))
        {
            root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            p = Path.Combine(root, "version-packs", "sample-classic", "manifest.json");
        }
        if (!File.Exists(p))
            p = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "version-packs", "sample-classic", "manifest.json"));
        Assert.True(File.Exists(p), $"manifest not found: {p}");
        return p;
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

    [Fact]
    public void Protocol_Version_Is_1_19()
    {
        Assert.Equal("1.20.0", ProtocolConstants.CurrentVersion);
        Assert.Equal("1.20.0", ProtocolConstants.MinCompatibleVersion);
        var pack = new VersionPackLoader().LoadFromManifest(GetManifestPath());
        Assert.Equal("1.20.0", pack.Manifest.ProtocolVersion);
    }

    [Fact]
    public async Task Admin_Requires_Token_And_Online_Ban_Mute_Siege_Reload()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m19-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { /* ignore */ }

        const string token = "m19-test-token";
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        await engine.StartAdminAsync(0, token, "127.0.0.1");
        Assert.True(engine.AdminBoundPort > 0);

        using var http = CreateAdminClient(engine.AdminBoundPort, token);
        using var httpNoAuth = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };

        try
        {
            // Unauthorized
            var unauth = await httpNoAuth.GetAsync("/admin/online");
            Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

            // Empty online
            var online0 = await http.GetAsync("/admin/online");
            Assert.Equal(HttpStatusCode.OK, online0.StatusCode);
            using (var doc = JsonDocument.Parse(await online0.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(0, doc.RootElement.GetProperty("online").GetInt32());
            }

            var (client, charId, enter, accountId) = await EnterAsAsync(engine, "m19admin", "M19Hero", "warrior");
            Assert.False(enter.IsMuted);

            var online1 = await http.GetAsync("/admin/online");
            using (var doc = JsonDocument.Parse(await online1.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1, doc.RootElement.GetProperty("online").GetInt32());
                var players = doc.RootElement.GetProperty("players");
                Assert.Equal(1, players.GetArrayLength());
                Assert.Equal("M19Hero", players[0].GetProperty("name").GetString());
                Assert.Equal(accountId, players[0].GetProperty("accountId").GetString());
            }

            // Mute
            var muteBody = new StringContent(
                JsonSerializer.Serialize(new { username = "m19admin", muted = true, reason = "spam" }),
                Encoding.UTF8, "application/json");
            var muteResp = await http.PostAsync("/admin/mute", muteBody);
            Assert.Equal(HttpStatusCode.OK, muteResp.StatusCode);
            using (var doc = JsonDocument.Parse(await muteResp.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("muted").GetBoolean());
            }

            client.ReceivedServerNotices.Clear();
            // Drain any prior chat notifies
            while (client.ReceivedChatNotifies.TryDequeue(out _)) { }

            await client.ChatAsync(ChatChannel.ChatWorld, "should-be-blocked");
            await Task.Delay(200);
            Assert.DoesNotContain(client.ReceivedChatNotifies.ToArray(), c => c.Content == "should-be-blocked");
            // Expect mute notice eventually
            bool sawMute = false;
            for (int i = 0; i < 20; i++)
            {
                if (client.ReceivedServerNotices.ToArray().Any(n => n.NoticeType == "chat_denied" || n.NoticeType == "muted"))
                {
                    sawMute = true;
                    break;
                }
                await Task.Delay(50);
            }
            Assert.True(sawMute, "expected mute/chat_denied notice");

            // Unmute
            var unmuteBody = new StringContent(
                JsonSerializer.Serialize(new { username = "m19admin", muted = false, reason = "ok" }),
                Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/admin/mute", unmuteBody)).StatusCode);

            while (client.ReceivedChatNotifies.TryDequeue(out _)) { }
            await client.ChatAsync(ChatChannel.ChatWorld, "after-unmute");
            await client.WaitChatAsync(n => n.Content == "after-unmute", 3000);

            // Siege start/stop
            var siegeStart = await http.PostAsync("/admin/siege",
                new StringContent("""{"action":"start"}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, siegeStart.StatusCode);
            using (var doc = JsonDocument.Parse(await siegeStart.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("isActive").GetBoolean());
            }
            var (active, _, _) = engine.GameWorldModule.GetSiegeSnapshot();
            Assert.True(active);

            var siegeStop = await http.PostAsync("/admin/siege",
                new StringContent("""{"action":"stop"}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, siegeStop.StatusCode);
            Assert.False(engine.GameWorldModule.GetSiegeSnapshot().Active);

            // Reload pack success
            string oldHash = engine.VersionPack.PackHash;
            var reloadOk = await http.PostAsync("/admin/reload-pack",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, reloadOk.StatusCode);
            using (var doc = JsonDocument.Parse(await reloadOk.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(oldHash, doc.RootElement.GetProperty("oldHash").GetString());
                Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("newHash").GetString()));
            }

            // Reload pack failure → rollback
            var reloadBad = await http.PostAsync("/admin/reload-pack",
                new StringContent("""{"manifestPath":"/tmp/legend-m19-does-not-exist.json"}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.InternalServerError, reloadBad.StatusCode);
            using (var doc = JsonDocument.Parse(await reloadBad.Content.ReadAsStringAsync()))
            {
                Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            }
            Assert.Equal(oldHash, engine.VersionPack.PackHash); // same pack still loaded (reload of same file kept hash; failed path rolled back)

            // Ban blocks login
            var banBody = new StringContent(
                JsonSerializer.Serialize(new { username = "m19admin", banned = true, reason = "cheat" }),
                Encoding.UTF8, "application/json");
            var banResp = await http.PostAsync("/admin/ban", banBody);
            Assert.Equal(HttpStatusCode.OK, banResp.StatusCode);

            await Task.Delay(150); // allow kick

            var bannedClient = new LegendTcpClient();
            await bannedClient.ConnectAsync("127.0.0.1", engine.BoundPort);
            Assert.True((await bannedClient.HandshakeAsync("1.20.0", "pc-ban", default)).Accepted);
            var bannedLogin = await bannedClient.LoginAsync("m19admin", "secret12345", "PC");
            Assert.False(bannedLogin.Success);
            Assert.Equal(ProtocolErrorCode.ErrBanned, bannedLogin.ErrorCode);

            await client.DisposeAsync();
            await bannedClient.DisposeAsync();

            // Persist across engine restart (sqlite)
            await engine.DisposeAsync();
            engine = null!;

            var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            await using var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
            await engine2.StartAsync();
            await using (var c2 = new LegendTcpClient())
            {
                await c2.ConnectAsync("127.0.0.1", engine2.BoundPort);
                Assert.True((await c2.HandshakeAsync("1.20.0", "pc-ban2", default)).Accepted);
                var login2 = await c2.LoginAsync("m19admin", "secret12345", "PC");
                Assert.False(login2.Success);
                Assert.Equal(ProtocolErrorCode.ErrBanned, login2.ErrorCode);
            }
        }
        finally
        {
            if (engine is not null)
            {
                try { await engine.DisposeAsync(); } catch { /* ignore */ }
            }
            try { File.Delete(dbPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Admin_X_Admin_Token_Header_Works()
    {
        const string token = "hdr-token";
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();
        await engine.StartAdminAsync(0, token);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{engine.AdminBoundPort}/") };
            http.DefaultRequestHeaders.Add("X-Admin-Token", token);
            var resp = await http.GetAsync("/admin/online");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }
}
