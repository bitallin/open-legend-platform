using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Host;
using Legend.Storage;
using MoonSharp.Interpreter;
using Xunit;

namespace Legend.Tests;

public sealed class ScriptSandboxTests
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

    [Fact]
    public async Task Sandbox_HasNoIoOsOrLoaders()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();

        Assert.Equal("nil", engine.ScriptHost.ExecuteRawInSandbox("return type(os)").String);
        Assert.Equal("nil", engine.ScriptHost.ExecuteRawInSandbox("return type(io)").String);
        Assert.Equal("nil", engine.ScriptHost.ExecuteRawInSandbox("return type(dofile)").String);
        Assert.Equal("nil", engine.ScriptHost.ExecuteRawInSandbox("return type(loadfile)").String);
        Assert.Equal("nil", engine.ScriptHost.ExecuteRawInSandbox("return type(require)").String);
        Assert.Equal("nil", engine.ScriptHost.ExecuteRawInSandbox("return type(debug)").String);
        // MoonSharp HardSandbox may keep an empty package table; loaders that touch disk stay nil.
        var open = Assert.ThrowsAny<Exception>(() =>
            engine.ScriptHost.ExecuteRawInSandbox("return io.open('/tmp/legend-e7-should-not-exist','w')"));
        Assert.Contains("nil", open.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ThrowingEnterHook_DoesNotBreakSubsequentMove()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();

        // Temp script only — do not touch sample pack on disk.
        engine.ScriptHost.ReloadScriptContent("scripts/welcome.lua", @"
            function on_player_enter(player)
                error('e7-sandbox-boom')
            end
        ");

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await client.HandshakeAsync("1.21.0", "pc-e7", default)).Accepted);
        await client.RegisterAsync("e7sandbox", "secret12345", "PC");
        var login = await client.LoginAsync("e7sandbox", "secret12345", "PC");
        Assert.True(login.Success, login.Message);
        var create = await client.CreateCharAsync("E7Hero", "warrior");
        Assert.True(create.Success, create.Message);
        await client.SelectCharAsync(create.Character.CharacterId);
        var realms = await client.GetRealmListAsync();
        var enter = await client.EnterWorldAsync(realms.Realms[0].RealmId, create.Character.CharacterId);
        Assert.True(enter.Success, enter.Message);

        int x = enter.SelfEntity.X;
        int y = enter.SelfEntity.Y;
        // Prefer a cardinal step that sample maps usually allow near spawn.
        var sync = await client.MoveAsync(x + 1, y, direction: 2);
        Assert.True(sync.Accepted || sync.ErrorCode == ProtocolErrorCode.ErrMoveBlocked,
            $"unexpected move failure: {sync.ErrorCode} {sync.Reason}");
        // Host still alive: a second move attempt must not hang or throw.
        var sync2 = await client.MoveAsync(sync.Accepted ? sync.ToX : x, sync.Accepted ? sync.ToY + 1 : y + 1, direction: 4);
        Assert.NotNull(sync2);
    }

    [Fact]
    public async Task BadReload_RollsBack_AndHostStillServesMove()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();
        string hash = engine.VersionPack.PackHash;

        var bad = await engine.ReloadVersionPackAsync(
            Path.Combine(Path.GetTempPath(), $"legend-e7-missing-{Guid.NewGuid():N}.json"));
        Assert.False(bad.Ok);
        Assert.Contains("rolled back", bad.Message);
        Assert.Equal(hash, engine.VersionPack.PackHash);

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.21.0", "pc-e7b");
        await client.RegisterAndLoginAsync("e7reload", "secret12345");
        var c = await client.CreateCharAsync("E7Reload", "taoist");
        Assert.True(c.Success);
        await client.SelectCharAsync(c.Character.CharacterId);
        var enter = await client.EnterWorldAsync("realm-classic-01", c.Character.CharacterId);
        Assert.True(enter.Success, enter.Message);
        var move = await client.MoveAsync(enter.SelfEntity.X + 1, enter.SelfEntity.Y, direction: 2);
        Assert.NotNull(move);
    }
}
