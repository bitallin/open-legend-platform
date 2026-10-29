using System.Diagnostics;
using System.Net.Sockets;

namespace Legend.Launcher.Core;

/// <summary>TCP connect RTT probe to host:port.</summary>
public sealed class LatencyProbe
{
    public async Task<int?> ProbeAsync(string host, int port, int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            return null;

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, linked.Token);
            sw.Stop();
            return (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
        }
        catch
        {
            return null;
        }
    }

    public async Task ProbeAllAsync(IEnumerable<RealmEntry> realms, int timeoutMs = 2000, CancellationToken ct = default)
    {
        var tasks = realms.Select(async r =>
        {
            r.LatencyMs = await ProbeAsync(r.Host, r.Port, timeoutMs, ct);
        });
        await Task.WhenAll(tasks);
    }
}
