using System.Net;
using Legend.Core;
using Legend.Host;

string manifestPath = "../version-packs/sample-classic/manifest.json";
int port = 7100;
string bindIp = "0.0.0.0";

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--manifest" && i + 1 < args.Length)
        manifestPath = args[++i];
    else if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out int p))
        port = p;
    else if (args[i] == "--bind" && i + 1 < args.Length)
        bindIp = args[++i];
}

if (!File.Exists(manifestPath))
{
    // Fallback relative to repo root if launched from /workspace/legend-platform
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

await using var engine = new LegendServerEngine(manifestPath, port, IPAddress.Parse(bindIp));
await engine.StartAsync(cts.Token);

StructuredLogger.Info("Legend.Host",
    $"Legend.Host ready on {bindIp}:{engine.BoundPort} (VersionPack={engine.VersionPack.Manifest.VersionId}, PackHash={engine.VersionPack.PackHash})");

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    StructuredLogger.Info("Legend.Host", "Shutting down Legend.Host gracefully...");
}
