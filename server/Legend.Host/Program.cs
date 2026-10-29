using System.Net;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

string manifestPath = "../version-packs/sample-classic/manifest.json";
int port = 7100;
string bindIp = "0.0.0.0";
StorageOptions? storageOverride = null;
int? adminPortOpt = null;
string adminToken = Environment.GetEnvironmentVariable("LEGEND_ADMIN_TOKEN") ?? string.Empty;
string adminBind = Environment.GetEnvironmentVariable("LEGEND_ADMIN_BIND") ?? "127.0.0.1";
string? realmConfigPath = Environment.GetEnvironmentVariable("LEGEND_REALM_CONFIG");
string? realmCatalogPath = Environment.GetEnvironmentVariable("LEGEND_REALM_CATALOG");
string? realmId = Environment.GetEnvironmentVariable("LEGEND_REALM_ID");
string paymentSecret = Environment.GetEnvironmentVariable("LEGEND_PAYMENT_SECRET") ?? "legend-mock-pay-secret";
string paymentProvider = Environment.GetEnvironmentVariable("LEGEND_PAYMENT_PROVIDER") ?? "mock";

if (int.TryParse(Environment.GetEnvironmentVariable("LEGEND_ADMIN_PORT"), out int envAdminPort))
    adminPortOpt = envAdminPort;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--manifest" && i + 1 < args.Length)
        manifestPath = args[++i];
    else if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out int p))
        port = p;
    else if (args[i] == "--bind" && i + 1 < args.Length)
        bindIp = args[++i];
    else if (args[i] == "--storage" && i + 1 < args.Length)
    {
        storageOverride ??= StorageFactory.FromEnvironment();
        storageOverride.Backend = args[++i];
    }
    else if (args[i] == "--sqlite" && i + 1 < args.Length)
    {
        storageOverride ??= StorageFactory.FromEnvironment();
        storageOverride.Backend = "Sqlite";
        storageOverride.SqlitePath = args[++i];
    }
    else if (args[i] == "--admin-port" && i + 1 < args.Length && int.TryParse(args[++i], out int ap))
        adminPortOpt = ap;
    else if (args[i] == "--admin-token" && i + 1 < args.Length)
        adminToken = args[++i];
    else if (args[i] == "--admin-bind" && i + 1 < args.Length)
        adminBind = args[++i];
    else if (args[i] == "--realm-config" && i + 1 < args.Length)
        realmConfigPath = args[++i];
    else if (args[i] == "--realm-catalog" && i + 1 < args.Length)
        realmCatalogPath = args[++i];
    else if (args[i] == "--realm-id" && i + 1 < args.Length)
        realmId = args[++i];
    else if (args[i] == "--payment-secret" && i + 1 < args.Length)
        paymentSecret = args[++i];
    else if (args[i] == "--payment-provider" && i + 1 < args.Length)
        paymentProvider = args[++i];
}

RealmConfig? localRealm = null;
IReadOnlyList<RealmConfig> catalog = Array.Empty<RealmConfig>();

if (!string.IsNullOrWhiteSpace(realmCatalogPath))
    catalog = RealmCatalogLoader.Load(realmCatalogPath);

if (!string.IsNullOrWhiteSpace(realmConfigPath))
{
    localRealm = RealmCatalogLoader.LoadRealmFile(realmConfigPath);
}
else if (!string.IsNullOrWhiteSpace(realmId))
{
    // Prefer catalog match, else realms/{id}.json
    localRealm = catalog.FirstOrDefault(r => string.Equals(r.RealmId, realmId, StringComparison.OrdinalIgnoreCase));
    if (localRealm is null)
    {
        string guess = Path.Combine("realms", $"{realmId}.json");
        if (File.Exists(guess))
            localRealm = RealmCatalogLoader.LoadRealmFile(guess);
        else
            throw new FileNotFoundException($"Realm id '{realmId}' not found in catalog or {guess}");
    }
}

if (localRealm is not null)
{
    if (!string.IsNullOrWhiteSpace(localRealm.ManifestPath))
        manifestPath = localRealm.ManifestPath;
    if (localRealm.Port > 0)
        port = localRealm.Port;
    if (!string.IsNullOrWhiteSpace(localRealm.SqlitePath))
    {
        storageOverride ??= StorageFactory.FromEnvironment();
        storageOverride.Backend = "Sqlite";
        storageOverride.SqlitePath = localRealm.SqlitePath;
    }
    if (localRealm.AdminPort > 0 && adminPortOpt is null)
        adminPortOpt = localRealm.AdminPort;
    if (!string.IsNullOrWhiteSpace(localRealm.AdminToken) && string.IsNullOrWhiteSpace(adminToken))
        adminToken = localRealm.AdminToken;
}

if (!File.Exists(manifestPath))
{
    string alt = Path.Combine("version-packs", "sample-classic", "manifest.json");
    if (File.Exists(alt))
        manifestPath = alt;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

await using var engine = await LegendServerEngine.CreateAsync(
    manifestPath, port, IPAddress.Parse(bindIp), storageOverride, catalog, localRealm, paymentSecret, paymentProvider, cts.Token);
await engine.StartAsync(cts.Token);

if (adminPortOpt is int adminPort)
{
    await engine.StartAdminAsync(adminPort, adminToken, adminBind, cts.Token);
}

StructuredLogger.Info("Legend.Host",
    $"Legend.Host ready on {bindIp}:{engine.BoundPort} (Realm={localRealm?.RealmId ?? engine.VersionPack.Manifest.RealmId}, VersionPack={engine.VersionPack.Manifest.VersionId}, PackHash={engine.VersionPack.PackHash}, Storage={engine.Storage.BackendName}" +
    (engine.AdminBoundPort > 0 ? $", Admin={adminBind}:{engine.AdminBoundPort}" : "") +
    $", Payment={engine.PaymentProviderId}, CatalogRealms={engine.RealmCatalog.Count})");

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    StructuredLogger.Info("Legend.Host", "Shutting down Legend.Host gracefully...");
}
