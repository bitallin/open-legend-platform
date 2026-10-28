using System.Text.Json;

namespace Legend.Core;

public static class StructuredLogger
{
    private static readonly object SyncLock = new();

    public static void Info(string module, string message, string? connId = null, string? playerId = null, object? extra = null)
        => Write("INFO", module, message, connId, playerId, extra);

    public static void Warn(string module, string message, string? connId = null, string? playerId = null, object? extra = null)
        => Write("WARN", module, message, connId, playerId, extra);

    public static void Error(string module, string message, string? connId = null, string? playerId = null, object? extra = null)
        => Write("ERROR", module, message, connId, playerId, extra);

    private static void Write(string level, string module, string message, string? connId, string? playerId, object? extra)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
            ["level"] = level,
            ["module"] = module,
            ["msg"] = message
        };

        if (!string.IsNullOrEmpty(connId))
            payload["connId"] = connId;
        if (!string.IsNullOrEmpty(playerId))
            payload["playerId"] = playerId;
        if (extra != null)
            payload["data"] = extra;

        string line = JsonSerializer.Serialize(payload);
        lock (SyncLock)
        {
            Console.WriteLine(line);
        }
    }
}
