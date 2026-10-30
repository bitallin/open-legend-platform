using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

/// <summary>
/// Pure enter-world and move decisions. AOI membership stays in <see cref="GridAoiManager"/>.
/// </summary>
public interface IMovementRules
{
    MoveDecision EvaluateMove(MoveContext context);
    (int X, int Y) ResolveEnterCell(MapConfig map, int savedX, int savedY);
    bool IsInSafeZone(MapConfig map, int x, int y);
    PortalConfig? FindPortal(MapConfig map, int x, int y);
}

public readonly record struct MoveContext(
    MapConfig Map,
    string PlayerId,
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    bool GateBlocked,
    bool CellBreached,
    IAntiCheatProbe Probe,
    bool Mounted,
    int MoveIntervalMs,
    long LastMoveUnixMs,
    long NowUnixMs);

public readonly record struct MoveDecision(
    bool Accepted,
    ProtocolErrorCode ErrorCode,
    string Reason,
    long LastMoveUnixMs)
{
    public static MoveDecision Allow(long lastMoveUnixMs) =>
        new(true, ProtocolErrorCode.ErrNone, "OK", lastMoveUnixMs);

    public static MoveDecision Deny(ProtocolErrorCode code, string reason, long lastMoveUnixMs) =>
        new(false, code, reason, lastMoveUnixMs);
}

public sealed class DefaultMovementRules : IMovementRules
{
    public static DefaultMovementRules Instance { get; } = new();

    public const int MountIntervalSlackMs = 15;
    public const int MinMountedIntervalMs = 50;

    public MoveDecision EvaluateMove(MoveContext context)
    {
        if (context.GateBlocked)
            return MoveDecision.Deny(ProtocolErrorCode.ErrMoveBlocked, "城门未破", context.LastMoveUnixMs);

        if (context.Map.IsBlocked(context.ToX, context.ToY) && !context.CellBreached)
        {
            return MoveDecision.Deny(
                ProtocolErrorCode.ErrMoveBlocked,
                $"Target cell ({context.ToX},{context.ToY}) is blocked or out of map bounds.",
                context.LastMoveUnixMs);
        }

        if (!context.Probe.ValidateMove(context.PlayerId, context.FromX, context.FromY, context.ToX, context.ToY, out string stepReason))
            return MoveDecision.Deny(ProtocolErrorCode.ErrMoveBlocked, stepReason, context.LastMoveUnixMs);

        if (!TryAcceptInterval(context, out string speedReason, out long nextMoveUnixMs))
            return MoveDecision.Deny(ProtocolErrorCode.ErrMoveTooFast, speedReason, context.LastMoveUnixMs);

        return MoveDecision.Allow(nextMoveUnixMs);
    }

    public (int X, int Y) ResolveEnterCell(MapConfig map, int savedX, int savedY)
    {
        if (map.IsBlocked(savedX, savedY))
            return (map.SpawnX, map.SpawnY);
        return (savedX, savedY);
    }

    public bool IsInSafeZone(MapConfig map, int x, int y) => map.IsInSafeZone(x, y);

    public PortalConfig? FindPortal(MapConfig map, int x, int y) => map.FindPortalAt(x, y);

    private static bool TryAcceptInterval(MoveContext context, out string reason, out long nextMoveUnixMs)
    {
        reason = "";
        // Foot movement stays unconstrained. Mounted clients must respect MoveIntervalMs.
        if (!context.Mounted)
        {
            nextMoveUnixMs = context.NowUnixMs;
            return true;
        }

        int interval = Math.Max(MinMountedIntervalMs, context.MoveIntervalMs);
        if (context.LastMoveUnixMs > 0 && context.NowUnixMs - context.LastMoveUnixMs < interval - MountIntervalSlackMs)
        {
            reason = $"Move too fast (interval={interval}ms).";
            nextMoveUnixMs = context.LastMoveUnixMs;
            return false;
        }

        nextMoveUnixMs = context.NowUnixMs;
        return true;
    }
}
