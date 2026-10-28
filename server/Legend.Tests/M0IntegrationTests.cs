using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Host;
using MoonSharp.Interpreter;
using Xunit;

namespace Legend.Tests;

public sealed class M0IntegrationTests
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

    [Fact]
    public async Task FullChain_MultiClient_AoiBroadcast_ObstacleCheck_And_LuaHook_Succeeds()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback);
        await engine.StartAsync();

        await using var clientA = new LegendTcpClient();
        await using var clientB = new LegendTcpClient();

        await clientA.ConnectAsync("127.0.0.1", engine.BoundPort);
        var hsA = await clientA.HandshakeAsync("1.0.0", "pc-test");
        Assert.True(hsA.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrNone, hsA.ErrorCode);

        var loginA = await clientA.LoginAsync("player_alpha", "secret", "PC");
        Assert.True(loginA.Success);
        Assert.StartsWith("tk_acc_player_alpha_", loginA.SessionToken);

        var realms = await clientA.GetRealmListAsync();
        Assert.True(realms.Success);
        Assert.Single(realms.Realms);
        Assert.Equal("realm-classic-01", realms.Realms[0].RealmId);

        var enterA = await clientA.EnterWorldAsync("realm-classic-01", "AlphaWarrior", "warrior");
        Assert.True(enterA.Success);
        Assert.Equal(10, enterA.SelfEntity.X);
        Assert.Equal(10, enterA.SelfEntity.Y);
        Assert.Equal(180, enterA.SelfEntity.Hp);

        // Wait for Lua welcome notice
        for (int i = 0; i < 30 && clientA.ReceivedServerNotices.IsEmpty; i++)
            await Task.Delay(20);

        Assert.True(clientA.ReceivedServerNotices.TryDequeue(out var notice));
        Assert.Equal("LUA_HOOK", notice.NoticeType);
        Assert.Equal("item_wooden_blade", notice.GrantedItemId);
        Assert.Equal(1, notice.GrantedItemCount);

        // Connect Client B (Android)
        await clientB.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await clientB.HandshakeAsync("1.0.0", "android-test")).Accepted);
        Assert.True((await clientB.LoginAsync("player_beta", "secret", "Android")).Success);

        var enterB = await clientB.EnterWorldAsync("realm-classic-01", "BetaMage", "mage");
        Assert.True(enterB.Success);
        Assert.Single(enterB.NearbyEntities);
        Assert.Equal(enterA.SelfEntity.EntityId, enterB.NearbyEntities[0].EntityId);

        // Client A should receive EntityEnterAoi for Client B
        for (int i = 0; i < 30 && clientA.ReceivedAoiEnters.IsEmpty; i++)
            await Task.Delay(20);

        Assert.True(clientA.ReceivedAoiEnters.TryDequeue(out var aoiEnter));
        Assert.Equal(enterB.SelfEntity.EntityId, aoiEnter.Entity.EntityId);

        // Client A moves from (10,10) -> (11,11) -> (13,13) -> (14,15)
        var move1 = await clientA.MoveAsync(11, 11, direction: 3);
        Assert.True(move1.Accepted);

        for (int i = 0; i < 30 && clientB.ReceivedMoveSyncs.IsEmpty; i++)
            await Task.Delay(20);

        Assert.True(clientB.ReceivedMoveSyncs.TryDequeue(out var bSawMove));
        Assert.Equal(enterA.SelfEntity.EntityId, bSawMove.EntityId);
        Assert.Equal(11, bSawMove.ToX);
        Assert.Equal(11, bSawMove.ToY);

        // Test Anti-Cheat speed probe: jumping from (11,11) to (20,20) in one step is rejected
        var speedHackMove = await clientA.MoveAsync(20, 20, direction: 4);
        Assert.False(speedHackMove.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrMoveBlocked, speedHackMove.ErrorCode);

        // Move step by step near blocked obstacle cell (15,15)
        Assert.True((await clientA.MoveAsync(13, 13, 3)).Accepted);
        Assert.True((await clientA.MoveAsync(14, 14, 3)).Accepted);

        // Cell (15,15) is configured as blocked in maps/novice_plain.json
        var blockedMove = await clientA.MoveAsync(15, 15, 3);
        Assert.False(blockedMove.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrMoveBlocked, blockedMove.ErrorCode);
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("0.9.0")]
    [InlineData("invalid")]
    public async Task ProtocolVersionMismatch_IsRejectedAtHandshake(string badVersion)
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback);
        await engine.StartAsync();

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);

        var resp = await client.HandshakeAsync(badVersion, "bad-client");
        Assert.False(resp.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrVersionMismatch, resp.ErrorCode);

        // Attempting to login without valid handshake must also be rejected
        var loginResp = await client.LoginAsync("hacker", "123456");
        Assert.False(loginResp.Success);
        Assert.Equal(ProtocolErrorCode.ErrVersionMismatch, loginResp.ErrorCode);
    }

    [Fact]
    public async Task PluginHotSwap_LoadEnableDisableUnload_WorksDynamically()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback);
        await engine.StartAsync();

        var audit = engine.BuiltInAuditPlugin;
        Assert.True(audit.IsEnabled);
        Assert.Equal(0, audit.LoginCount);

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.0.0");
        await client.LoginAsync("user1", "pass");
        Assert.Equal(1, audit.LoginCount);

        // Hot-disable plugin at runtime
        await engine.PluginManager.DisablePluginAsync(audit.PluginId);
        Assert.False(audit.IsEnabled);

        await client.LoginAsync("user2", "pass");
        Assert.Equal(1, audit.LoginCount); // Did not increment while disabled

        // Hot-enable plugin again
        await engine.PluginManager.EnablePluginAsync(audit.PluginId);
        Assert.True(audit.IsEnabled);

        await client.LoginAsync("user3", "pass");
        Assert.Equal(2, audit.LoginCount);

        // Unload plugin
        await engine.PluginManager.UnloadPluginAsync(audit.PluginId);
        Assert.DoesNotContain(engine.PluginManager.Plugins, p => p.PluginId == audit.PluginId);
    }

    [Fact]
    public async Task LuaHardSandbox_BlocksForbiddenOsAndIoAccess_AndSupportsHotReload()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback);
        await engine.StartAsync();

        // 1. Verify os and io tables are completely absent in MoonSharp HardSandbox
        DynValue osVal = engine.ScriptHost.ExecuteRawInSandbox("return type(os)");
        DynValue ioVal = engine.ScriptHost.ExecuteRawInSandbox("return type(io)");
        Assert.Equal("nil", osVal.String);
        Assert.Equal("nil", ioVal.String);

        // 2. Hot-reload welcome script without restarting server
        engine.ScriptHost.ReloadScriptContent("scripts/welcome.lua", @"
            function on_player_enter(player)
                legend_api.send_welcome('热更活动开启', '热更欢迎：' .. player.name)
                legend_api.grant_starter_item('item_hotreload_gem', 88)
            end
        ");

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.0.0");
        await client.LoginAsync("hot_user", "pass");
        await client.EnterWorldAsync("realm-classic-01", "HotReloadHero", "taoist");

        for (int i = 0; i < 30 && client.ReceivedServerNotices.IsEmpty; i++)
            await Task.Delay(20);

        Assert.True(client.ReceivedServerNotices.TryDequeue(out var notice));
        Assert.Equal("热更活动开启", notice.Title);
        Assert.Equal("item_hotreload_gem", notice.GrantedItemId);
        Assert.Equal(88, notice.GrantedItemCount);
    }
}
