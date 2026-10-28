using System.Net;
using Legend.Contracts;
using Legend.Core;
using Legend.Game;
using Legend.Gate;
using Legend.Login;
using Legend.Script;
using Legend.Storage;

namespace Legend.Host;

public sealed class LegendServerEngine : IAsyncDisposable
{
    public IEventBus EventBus { get; }
    public PluginManager PluginManager { get; }
    public LoadedVersionPack VersionPack { get; }
    public ILegendStorage Storage { get; }
    public LuaScriptHost ScriptHost { get; }
    public LoginModule LoginModule { get; }
    public GameWorldModule GameWorldModule { get; }
    public TcpGateServer GateServer { get; }
    public TelemetryAuditPlugin BuiltInAuditPlugin { get; }

    public int BoundPort => GateServer.BoundPort;

    public LegendServerEngine(
        string manifestPath,
        int port = 7100,
        IPAddress? bindAddress = null,
        ILegendStorage? storage = null)
    {
        EventBus = new InMemoryEventBus();
        PluginManager = new PluginManager(EventBus);

        var loader = new VersionPackLoader();
        VersionPack = loader.LoadFromManifest(manifestPath);

        Storage = storage ?? new InMemoryStorage();
        ScriptHost = new LuaScriptHost(EventBus, ScriptPermissionLevel.GrantItem);
        ScriptHost.LoadScripts(VersionPack.LuaScripts);

        GameWorldModule = new GameWorldModule(EventBus, VersionPack, Storage);

        LoginModule = new LoginModule(
            EventBus,
            VersionPack,
            Storage,
            () => GameWorldModule.OnlineCount,
            () => GateServer?.BoundPort ?? port);

        GateServer = new TcpGateServer(
            bindAddress ?? IPAddress.Loopback,
            port,
            new IGameModule[] { LoginModule, GameWorldModule });

        BuiltInAuditPlugin = new TelemetryAuditPlugin();
    }

    public static async Task<LegendServerEngine> CreateAsync(
        string manifestPath,
        int port = 7100,
        IPAddress? bindAddress = null,
        StorageOptions? storageOptions = null,
        CancellationToken ct = default)
    {
        var opts = storageOptions ?? StorageFactory.FromEnvironment();
        var storage = await StorageFactory.CreateAsync(opts, ct);
        StructuredLogger.Info("Legend.Host", $"Storage backend: {storage.BackendName}");
        var engine = new LegendServerEngine(manifestPath, port, bindAddress, storage);
        return engine;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await Storage.InitializeAsync(ct);
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
        await Storage.DisposeAsync();
    }
}
