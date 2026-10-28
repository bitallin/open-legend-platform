using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;
using MoonSharp.Interpreter;
using Xunit;

namespace Legend.Tests;

public sealed class M1IntegrationTests
{
    private static string GetManifestPath()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, "version-packs", "sample-classic", "manifest.json");
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }
        throw new FileNotFoundException("Could not locate version-packs/sample-classic/manifest.json");
    }

    private static async Task<(LegendServerEngine Engine, string DbPath)> StartSqliteEngineAsync()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m1-{Guid.NewGuid():N}.db");
        var storage = await StorageFactory.CreateAsync(new StorageOptions
        {
            Backend = "Sqlite",
            SqlitePath = dbPath
        });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        return (engine, dbPath);
    }

    private static async Task EnsureCharAsync(LegendTcpClient client, string name, string classId)
    {
        var created = await client.CreateCharAsync(name, classId);
        if (created.Success)
        {
            await client.SelectCharAsync(created.Character.CharacterId);
            return;
        }
        var list = await client.CharListAsync();
        var existing = list.Characters.First(c => c.Name == name);
        await client.SelectCharAsync(existing.CharacterId);
    }

    [Fact]
    public async Task FullChain_RegisterLogin_CreateSelect_Aoi_Blocked_SafeZone()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();

        await using var clientA = new LegendTcpClient();
        await using var clientB = new LegendTcpClient();

        await clientA.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await clientA.HandshakeAsync("1.10.0", "pc-test")).Accepted);

        Assert.True((await clientA.RegisterAsync("player_alpha", "secret1", "PC")).Success);
        var loginA = await clientA.LoginAsync("player_alpha", "secret1", "PC");
        Assert.True(loginA.Success);
        Assert.False(string.IsNullOrEmpty(loginA.SessionToken));

        var realms = await clientA.GetRealmListAsync();
        Assert.True(realms.Success);
        Assert.Single(realms.Realms);

        var create = await clientA.CreateCharAsync("AlphaWarrior", "warrior");
        Assert.True(create.Success);
        Assert.Equal("warrior", create.Character.ClassId);
        Assert.Equal(180, create.Character.Hp);
        Assert.Equal(40, create.Character.Mp);
        Assert.True(create.Character.X >= 0);

        var select = await clientA.SelectCharAsync(create.Character.CharacterId);
        Assert.True(select.Success);

        var enterA = await clientA.EnterWorldAsync("realm-classic-01", create.Character.CharacterId);
        Assert.True(enterA.Success);
        Assert.Equal(10, enterA.SelfEntity.X);
        Assert.Equal(10, enterA.SelfEntity.Y);
        Assert.Equal(180, enterA.SelfEntity.Hp);
        Assert.True(enterA.InSafeZone);

        for (int i = 0; i < 30 && clientA.ReceivedServerNotices.IsEmpty; i++)
            await Task.Delay(20);
        Assert.True(clientA.ReceivedServerNotices.TryDequeue(out var notice));
        Assert.Equal("LUA_HOOK", notice.NoticeType);

        await clientB.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await clientB.HandshakeAsync("1.10.0", "android-test")).Accepted);
        Assert.True((await clientB.RegisterAsync("player_beta", "secret2", "Android")).Success);
        Assert.True((await clientB.LoginAsync("player_beta", "secret2", "Android")).Success);
        await EnsureCharAsync(clientB, "BetaMage", "mage");
        var enterB = await clientB.EnterWorldAsync("realm-classic-01", clientB.CharacterId!);
        Assert.True(enterB.Success);
        Assert.Contains(enterB.NearbyEntities, e => e.EntityId == clientA.SelfEntity!.EntityId);
        Assert.Single(enterB.NearbyEntities.Where(e => e.Kind == EntityKind.EntityPlayer));

        for (int i = 0; i < 30 && clientA.ReceivedAoiEnters.IsEmpty; i++)
            await Task.Delay(20);
        Assert.True(clientA.ReceivedAoiEnters.TryDequeue(out var aoiEnter));
        Assert.Equal(enterB.SelfEntity.EntityId, aoiEnter.Entity.EntityId);

        var move1 = await clientA.MoveAsync(11, 11, direction: 3);
        Assert.True(move1.Accepted);
        Assert.True(move1.InSafeZone);

        for (int i = 0; i < 30 && clientB.ReceivedMoveSyncs.IsEmpty; i++)
            await Task.Delay(20);
        Assert.True(clientB.ReceivedMoveSyncs.TryDequeue(out var bSawMove));
        Assert.Equal(11, bSawMove.ToX);

        var speedHackMove = await clientA.MoveAsync(20, 20, direction: 4);
        Assert.False(speedHackMove.Accepted);

        Assert.True((await clientA.MoveAsync(13, 13, 3)).Accepted);
        Assert.True((await clientA.MoveAsync(14, 14, 3)).Accepted);
        var blockedMove = await clientA.MoveAsync(15, 15, 3);
        Assert.False(blockedMove.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrMoveBlocked, blockedMove.ErrorCode);
    }

    [Fact]
    public async Task InvalidClass_IsRejected_And_DeleteChar_Works()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.10.0");
        Assert.True((await client.RegisterAsync("cls_user", "secret1")).Success);
        Assert.True((await client.LoginAsync("cls_user", "secret1")).Success);

        var bad = await client.CreateCharAsync("BadHero", "assassin");
        Assert.False(bad.Success);
        Assert.Equal(ProtocolErrorCode.ErrInvalidClass, bad.ErrorCode);

        var good = await client.CreateCharAsync("GoodHero", "taoist");
        Assert.True(good.Success);

        var del = await client.DeleteCharAsync(good.Character.CharacterId);
        Assert.True(del.Success);

        var list = await client.CharListAsync();
        Assert.Empty(list.Characters);
    }

    [Fact]
    public async Task Portal_Transfers_To_DarkForest()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.10.0");
        await client.RegisterAndLoginAsync("portal_user", "secret1");
        var created = await client.CreateCharAsync("PortalWalker", "warrior");
        Assert.True(created.Success);
        await client.SelectCharAsync(created.Character.CharacterId);
        var enter = await client.EnterWorldAsync("realm-classic-01", created.Character.CharacterId);
        Assert.True(enter.Success);
        Assert.Equal("novice_plain", enter.MapId);

        // Walk from (10,10) to portal at (30,10) with step distance <= 2
        int x = 10, y = 10;
        while (x < 30)
        {
            int next = Math.Min(x + 2, 30);
            var m = await client.MoveAsync(next, y, 2);
            Assert.True(m.Accepted, m.Reason);
            x = next;
        }

        for (int i = 0; i < 40 && client.ReceivedMapChanges.IsEmpty; i++)
            await Task.Delay(20);

        Assert.True(client.ReceivedMapChanges.TryDequeue(out var change));
        Assert.Equal("novice_plain", change.FromMapId);
        Assert.Equal("dark_forest", change.ToMapId);
        Assert.Equal("to_forest", change.PortalId);
        Assert.Equal("dark_forest", client.SelfEntity!.MapId);
        Assert.Equal(5, client.SelfEntity.X);
        Assert.Equal(5, client.SelfEntity.Y);
    }

    [Fact]
    public async Task SqlitePersistence_Survives_HostRestart()
    {
        var (engine1, dbPath) = await StartSqliteEngineAsync();
        int port1 = engine1.BoundPort;
        string charId;
        int savedX, savedY;

        try
        {
            await using var client = new LegendTcpClient();
            await client.ConnectAsync("127.0.0.1", port1);
            await client.HandshakeAsync("1.10.0");
            await client.RegisterAndLoginAsync("persist_user", "secret1");
            var created = await client.CreateCharAsync("PersistHero", "mage");
            Assert.True(created.Success);
            charId = created.Character.CharacterId;
            await client.SelectCharAsync(charId);
            var enter = await client.EnterWorldAsync("realm-classic-01", charId);
            Assert.True(enter.Success);

            Assert.True((await client.MoveAsync(12, 10, 2)).Accepted);
            Assert.True((await client.MoveAsync(14, 10, 2)).Accepted);
            savedX = 14;
            savedY = 10;
        }
        finally
        {
            await engine1.DisposeAsync();
        }

        // Restart with same sqlite file
        var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
        await engine2.StartAsync();
        try
        {
            await using var client2 = new LegendTcpClient();
            await client2.ConnectAsync("127.0.0.1", engine2.BoundPort);
            await client2.HandshakeAsync("1.10.0");
            var login = await client2.LoginAsync("persist_user", "secret1");
            Assert.True(login.Success);

            var list = await client2.CharListAsync();
            Assert.Single(list.Characters);
            Assert.Equal(charId, list.Characters[0].CharacterId);
            Assert.Equal(savedX, list.Characters[0].X);
            Assert.Equal(savedY, list.Characters[0].Y);
            Assert.Equal("novice_plain", list.Characters[0].MapId);

            await client2.SelectCharAsync(charId);
            var enter2 = await client2.EnterWorldAsync("realm-classic-01", charId);
            Assert.True(enter2.Success);
            Assert.Equal(savedX, enter2.SelfEntity.X);
            Assert.Equal(savedY, enter2.SelfEntity.Y);
        }
        finally
        {
            await engine2.DisposeAsync();
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void PasswordHasher_NeverStoresPlaintext()
    {
        string hash = PasswordHasher.Hash("my-password");
        Assert.StartsWith("pbkdf2$", hash);
        Assert.DoesNotContain("my-password", hash);
        Assert.True(PasswordHasher.Verify("my-password", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Fact]
    public void MapConfig_BlockedBitset_And_SafeZone_Portal()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal(2, pack.Maps.Count);
        var plain = pack.GetMap("novice_plain");
        Assert.True(plain.IsBlocked(15, 15));
        Assert.False(plain.IsBlocked(10, 10));
        Assert.True(plain.IsInSafeZone(10, 10));
        Assert.False(plain.IsInSafeZone(40, 40));
        var portal = plain.FindPortalAt(30, 10);
        Assert.NotNull(portal);
        Assert.Equal("dark_forest", portal!.TargetMapId);

        string b64 = plain.BuildBlockedBitsetBase64();
        Assert.False(string.IsNullOrEmpty(b64));
        var roundtrip = new MapConfig
        {
            Width = plain.Width,
            Height = plain.Height,
            BlockedBitsetBase64 = b64
        };
        Assert.True(roundtrip.IsBlocked(15, 15));
        Assert.False(roundtrip.IsBlocked(10, 10));
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("0.9.0")]
    [InlineData("invalid")]
    public async Task ProtocolVersionMismatch_IsRejectedAtHandshake(string badVersion)
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);

        var resp = await client.HandshakeAsync(badVersion, "bad-client");
        Assert.False(resp.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrVersionMismatch, resp.ErrorCode);

        var loginResp = await client.LoginAsync("hacker", "123456");
        Assert.False(loginResp.Success);
        Assert.Equal(ProtocolErrorCode.ErrVersionMismatch, loginResp.ErrorCode);
    }

    [Fact]
    public async Task PluginHotSwap_And_LuaSandbox_StillWork()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();

        var audit = engine.BuiltInAuditPlugin;
        Assert.True(audit.IsEnabled);

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.10.0");
        await client.RegisterAndLoginAsync("user1", "pass12");
        Assert.Equal(1, audit.LoginCount);

        await engine.PluginManager.DisablePluginAsync(audit.PluginId);
        await client.RegisterAsync("user2", "pass12");
        await client.LoginAsync("user2", "pass12");
        Assert.Equal(1, audit.LoginCount);

        await engine.PluginManager.EnablePluginAsync(audit.PluginId);
        await client.RegisterAsync("user3", "pass12");
        await client.LoginAsync("user3", "pass12");
        Assert.Equal(2, audit.LoginCount);

        DynValue osVal = engine.ScriptHost.ExecuteRawInSandbox("return type(os)");
        Assert.Equal("nil", osVal.String);

        engine.ScriptHost.ReloadScriptContent("scripts/welcome.lua", @"
            function on_player_enter(player)
                legend_api.send_welcome('热更活动开启', '热更欢迎：' .. player.name)
                legend_api.grant_starter_item('item_hotreload_gem', 88)
            end
        ");

        await using var client2 = new LegendTcpClient();
        await client2.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client2.HandshakeAsync("1.10.0");
        await client2.RegisterAndLoginAsync("hot_user", "pass12");
        var c = await client2.CreateCharAsync("HotReloadHero", "taoist");
        Assert.True(c.Success);
        await client2.SelectCharAsync(c.Character.CharacterId);
        await client2.EnterWorldAsync("realm-classic-01", c.Character.CharacterId);

        for (int i = 0; i < 30 && client2.ReceivedServerNotices.IsEmpty; i++)
            await Task.Delay(20);
        Assert.True(client2.ReceivedServerNotices.TryDequeue(out var notice));
        Assert.Equal("热更活动开启", notice.Title);
        Assert.Equal(88, notice.GrantedItemCount);
    }

    [Fact]
    public async Task PostgresPath_Runs_When_LEGEND_PG_Available()
    {
        string? pg = Environment.GetEnvironmentVariable("LEGEND_PG")
                     ?? Environment.GetEnvironmentVariable("LEGEND_DB_CONN");
        if (string.IsNullOrWhiteSpace(pg))
        {
            // Skip gracefully when Docker/Postgres unavailable
            return;
        }

        var opts = new StorageOptions
        {
            Backend = "Postgres",
            PostgresConnectionString = pg,
            RedisConnectionString = Environment.GetEnvironmentVariable("LEGEND_REDIS")
                                    ?? Environment.GetEnvironmentVariable("LEGEND_REDIS_CONN")
        };

        ILegendStorage storage;
        try
        {
            storage = await StorageFactory.CreateAsync(opts);
        }
        catch (Exception)
        {
            return; // connection failed — treat as skip
        }

        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        Assert.Equal("PostgreSQL", engine.Storage.BackendName);

        string user = $"pg_user_{Guid.NewGuid():N}"[..20];
        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.10.0");
        Assert.True((await client.RegisterAsync(user, "secret1")).Success);
        Assert.True((await client.LoginAsync(user, "secret1")).Success);
        var created = await client.CreateCharAsync("PgHero", "warrior");
        Assert.True(created.Success);
        await client.SelectCharAsync(created.Character.CharacterId);
        var enter = await client.EnterWorldAsync("realm-classic-01", created.Character.CharacterId);
        Assert.True(enter.Success);
        Assert.True((await client.MoveAsync(11, 10)).Accepted);

        var loaded = await storage.Characters.GetByIdAsync(created.Character.CharacterId);
        Assert.NotNull(loaded);
        Assert.Equal(11, loaded!.X);
    }
}
