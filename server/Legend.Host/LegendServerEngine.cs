using System.Net;
using Legend.Contracts;
using Legend.Core;
using Legend.Game;
using Legend.Gate;
using Legend.Login;
using Legend.Script;

namespace Legend.Host;

public sealed class LegendServerEngine : IAsyncDisposable
{
    public IEventBus EventBus { get; }
    public PluginManager PluginManager { get; }
    public LoadedVersionPack VersionPack { get; }
    public SessionTokenRegistry TokenRegistry { get; }
    public LuaScriptHost ScriptHost { get; }
    public LoginModule LoginModule { get; }
    public GameWorldModule GameWorldModule { get; }
    public TcpGateServer GateServer { get; }
    public TelemetryAuditPlugin BuiltInAuditPlugin { get; }

    public int BoundPort => GateServer.BoundPort;

    public LegendServerEngine(string manifestPath, int port = 7100, IPAddress? bindAddress = null)
    {
        EventBus = new InMemoryEventBus();
        PluginManager = new PluginManager(EventBus);

        var loader = new VersionPackLoader();
        VersionPack = loader.LoadFromManifest(manifestPath);

        TokenRegistry = new SessionTokenRegistry();
        ScriptHost = new LuaScriptHost(EventBus, ScriptPermissionLevel.GrantItem);
        ScriptHost.LoadScripts(VersionPack.LuaScripts);

        GameWorldModule = new GameWorldModule(
            EventBus,
            VersionPack,
            token =>
            {
                bool ok = TokenRegistry.TryValidateToken(token, out string acc);
                return (ok, acc);
            });

        LoginModule = new LoginModule(
            EventBus,
            VersionPack,
            TokenRegistry,
            () => GameWorldModule.OnlineCount,
            () => GateServer?.BoundPort ?? port);

        GateServer = new TcpGateServer(
            bindAddress ?? IPAddress.Loopback,
            port,
            new IGameModule[] { LoginModule, GameWorldModule });

        BuiltInAuditPlugin = new TelemetryAuditPlugin();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await PluginManager.RegisterAndEnableAsync(BuiltInAuditPlugin, ct);
        await GateServer.StartAsync(ct);
        await EventBus.PublishAsync(new ZoneLoadedEvent(
            VersionPack.Manifest.VersionId,
            VersionPack.Manifest.RealmId,
            VersionPack.PackHash), ct);
    }

    public async ValueTask DisposeAsync()
    {
        await GateServer.DisposeAsync();
    }
}
