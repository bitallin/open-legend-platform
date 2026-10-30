using System.Net;
using Legend.Contracts;
using Legend.Contracts.Payment;
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
    public LoadedVersionPack VersionPack { get; private set; }
    public ILegendStorage Storage { get; }
    public LuaScriptHost ScriptHost { get; }
    public LoginModule LoginModule { get; }
    public GameWorldModule GameWorldModule { get; }
    public TcpGateServer GateServer { get; }
    public TelemetryAuditPlugin BuiltInAuditPlugin { get; }
    public AdminHttpServer? AdminServer { get; private set; }

    public string ManifestPath { get; }
    public int BoundPort => GateServer.BoundPort;
    public int AdminBoundPort => AdminServer?.BoundPort ?? 0;
    public IReadOnlyList<RealmConfig> RealmCatalog { get; private set; } = Array.Empty<RealmConfig>();
    public RealmConfig? LocalRealm { get; private set; }
    public string PaymentSecret { get; private set; } = "legend-mock-pay-secret";
    public string PaymentProviderId { get; private set; } = "mock";
    public PaymentProviderRegistry PaymentProviders { get; private set; } = null!;

    public LegendServerEngine(
        string manifestPath,
        int port = 7100,
        IPAddress? bindAddress = null,
        ILegendStorage? storage = null,
        IReadOnlyList<RealmConfig>? realmCatalog = null,
        RealmConfig? localRealm = null,
        string? paymentSecret = null,
        string? paymentProvider = null)
    {
        ManifestPath = Path.GetFullPath(manifestPath);
        EventBus = new InMemoryEventBus();
        PluginManager = new PluginManager(EventBus);

        var loader = new VersionPackLoader();
        VersionPack = loader.LoadFromManifest(ManifestPath);

        Storage = storage ?? new InMemoryStorage();
        ScriptHost = new LuaScriptHost(EventBus, ScriptPermissionLevel.GrantItem);
        ScriptHost.LoadScripts(VersionPack.LuaScripts);

        GameWorldModule = new GameWorldModule(EventBus, VersionPack, Storage);
        PaymentSecret = string.IsNullOrWhiteSpace(paymentSecret) ? "legend-mock-pay-secret" : paymentSecret!;
        PaymentProviderId = string.IsNullOrWhiteSpace(paymentProvider)
            ? (Environment.GetEnvironmentVariable("LEGEND_PAYMENT_PROVIDER") ?? "mock")
            : paymentProvider!;
        PaymentProviders = new PaymentProviderRegistry(PaymentProviderId, PaymentSecret);
        GameWorldModule.ConfigurePaymentProvider(PaymentProviders.Active);

        LocalRealm = localRealm;
        RealmCatalog = realmCatalog ?? Array.Empty<RealmConfig>();

        LoginModule = new LoginModule(
            EventBus,
            VersionPack,
            Storage,
            () => GameWorldModule.OnlineCount,
            () => GateServer?.BoundPort ?? port,
            RealmCatalog,
            localRealm?.RealmId,
            localRealm?.RealmName,
            localRealm?.Host);

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
        IReadOnlyList<RealmConfig>? realmCatalog = null,
        RealmConfig? localRealm = null,
        string? paymentSecret = null,
        string? paymentProvider = null,
        CancellationToken ct = default)
    {
        var opts = storageOptions ?? StorageFactory.FromEnvironment();
        // Reject a bad map or items file before opening Sqlite or Postgres.
        _ = new VersionPackLoader().LoadFromManifest(manifestPath);
        var storage = await StorageFactory.CreateAsync(opts, ct);
        StructuredLogger.Info("Legend.Host", $"Storage backend: {storage.BackendName}");
        var engine = new LegendServerEngine(manifestPath, port, bindAddress, storage, realmCatalog, localRealm, paymentSecret, paymentProvider);
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

    /// <summary>Start optional admin HTTP sidecar. No-op when adminPort &lt;= 0.</summary>
    public async Task StartAdminAsync(int adminPort, string adminToken, string bindIp = "127.0.0.1", CancellationToken ct = default)
    {
        if (adminPort < 0)
            return;
        if (string.IsNullOrWhiteSpace(adminToken))
            throw new InvalidOperationException("Admin token required when --admin-port is set (use --admin-token or LEGEND_ADMIN_TOKEN).");

        AdminServer = new AdminHttpServer(this, adminPort, adminToken, bindIp);
        await AdminServer.StartAsync(ct);
        GameWorldModule.ConfigurePaymentNotifyBaseUrl($"http://{bindIp}:{AdminServer.BoundPort}");
        StructuredLogger.Info("Legend.Host",
            $"Payment provider={PaymentProviderId} (wechat_sim={PaymentProviders.Wechat.IsSimulator}, alipay_sim={PaymentProviders.Alipay.IsSimulator})");
    }

    public async Task<(bool Ok, string Message, string? OldHash, string? NewHash, string? VersionId)> ReloadVersionPackAsync(
        string? manifestPath = null,
        CancellationToken ct = default)
    {
        string path = string.IsNullOrWhiteSpace(manifestPath) ? ManifestPath : Path.GetFullPath(manifestPath);
        var previous = VersionPack;
        string? oldHash = previous.PackHash;
        try
        {
            var loader = new VersionPackLoader();
            var next = loader.LoadFromManifest(path);

            // Apply config swap first (safe hot reload of configs / scripts).
            GameWorldModule.ReplaceVersionPack(next);
            LoginModule.ReplaceVersionPack(next);
            try
            {
                ScriptHost.LoadScripts(next.LuaScripts);
            }
            catch
            {
                GameWorldModule.ReplaceVersionPack(previous);
                LoginModule.ReplaceVersionPack(previous);
                try { ScriptHost.LoadScripts(previous.LuaScripts); } catch { /* best effort */ }
                throw;
            }

            VersionPack = next;
            await EventBus.PublishAsync(new ZoneLoadedEvent(
                next.Manifest.VersionId,
                next.Manifest.RealmId,
                next.PackHash), ct);

            StructuredLogger.Info("Legend.Host",
                $"VersionPack hot-reloaded: {previous.Manifest.VersionId}/{oldHash} -> {next.Manifest.VersionId}/{next.PackHash}");
            return (true, "VersionPack reloaded", oldHash, next.PackHash, next.Manifest.VersionId);
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("Legend.Host", $"VersionPack reload failed, rolled back: {ex.Message}");
            return (false, $"reload failed (rolled back): {ex.Message}", oldHash, null, previous.Manifest.VersionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (AdminServer is not null)
            await AdminServer.DisposeAsync();
        await GateServer.DisposeAsync();
        await GameWorldModule.DisposeAiAsync();
        PaymentProviders.Dispose();
        await Storage.DisposeAsync();
    }
}
