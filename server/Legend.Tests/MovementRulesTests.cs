using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Game;

namespace Legend.Tests;

public class MovementRulesTests
{
    private readonly DefaultMovementRules _rules = DefaultMovementRules.Instance;

    [Fact]
    public void BlockedCellReturnsMoveBlocked()
    {
        var probe = new CountingProbe();
        var decision = _rules.EvaluateMove(Context(toX: 1, toY: 1, probe: probe));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrMoveBlocked, decision.ErrorCode);
        Assert.Equal(1005, (int)decision.ErrorCode);
        Assert.Contains("blocked", decision.Reason);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void BreachedBlockedCellIsAllowed()
    {
        var decision = _rules.EvaluateMove(Context(toX: 1, toY: 1, breached: true));
        Assert.True(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrNone, decision.ErrorCode);
    }

    [Fact]
    public void ClosedGateReturnsMoveBlockedBeforeTheProbe()
    {
        var probe = new CountingProbe();
        var decision = _rules.EvaluateMove(Context(toX: 2, toY: 0, gateBlocked: true, probe: probe));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrMoveBlocked, decision.ErrorCode);
        Assert.Equal("城门未破", decision.Reason);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void ProbeRejectionReturnsMoveBlocked()
    {
        var decision = _rules.EvaluateMove(Context(toX: 2, toY: 0, probe: new CountingProbe { Allow = false, Reason = "step too far" }));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrMoveBlocked, decision.ErrorCode);
        Assert.Equal("step too far", decision.Reason);
    }

    [Fact]
    public void MountedMoveTooSoonReturnsMoveTooFast()
    {
        var decision = _rules.EvaluateMove(Context(
            toX: 2,
            toY: 0,
            mounted: true,
            moveIntervalMs: 300,
            lastMoveUnixMs: 1_000,
            nowUnixMs: 1_200));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrMoveTooFast, decision.ErrorCode);
        Assert.Equal(1073, (int)decision.ErrorCode);
        Assert.Equal(1_000, decision.LastMoveUnixMs);
    }

    [Fact]
    public void FootMovementIgnoresInterval()
    {
        var decision = _rules.EvaluateMove(Context(toX: 2, toY: 0, lastMoveUnixMs: 1_000, nowUnixMs: 1_010));
        Assert.True(decision.Accepted);
        Assert.Equal(1_010, decision.LastMoveUnixMs);
    }

    [Fact]
    public void EnterCellLeavesABlockedSaveForTheSpawn()
    {
        var map = SampleMap();
        var (x, y) = _rules.ResolveEnterCell(map, 1, 1);
        Assert.Equal((map.SpawnX, map.SpawnY), (x, y));
        var kept = _rules.ResolveEnterCell(map, 2, 0);
        Assert.Equal((2, 0), kept);
    }

    [Fact]
    public void SafeZoneAndPortalFollowTheMap()
    {
        var map = SampleMap();
        Assert.True(_rules.IsInSafeZone(map, 0, 0));
        Assert.False(_rules.IsInSafeZone(map, 3, 3));
        Assert.Equal("to_gate", _rules.FindPortal(map, 2, 0)?.PortalId);
        Assert.Null(_rules.FindPortal(map, 3, 3));
    }

    private static MoveContext Context(
        int toX,
        int toY,
        bool gateBlocked = false,
        bool breached = false,
        IAntiCheatProbe? probe = null,
        bool mounted = false,
        int moveIntervalMs = 300,
        long lastMoveUnixMs = 0,
        long nowUnixMs = 10_000)
        => new(
            SampleMap(),
            "player-1",
            0,
            0,
            toX,
            toY,
            gateBlocked,
            breached,
            probe ?? new CountingProbe(),
            mounted,
            moveIntervalMs,
            lastMoveUnixMs,
            nowUnixMs);

    private static MapConfig SampleMap() => new()
    {
        MapId = "yard",
        Width = 4,
        Height = 4,
        SpawnX = 0,
        SpawnY = 0,
        BlockedCells = new List<GridCoord> { new(1, 1) },
        SafeZones = new List<SafeZoneRect> { new() { Name = "spawn", X1 = 0, Y1 = 0, X2 = 0, Y2 = 0 } },
        Portals = new List<PortalConfig>
        {
            new() { PortalId = "to_gate", X = 2, Y = 0, TargetMapId = "gate", TargetX = 1, TargetY = 1 }
        }
    };

    private sealed class CountingProbe : IAntiCheatProbe
    {
        public bool Allow { get; init; } = true;
        public string Reason { get; init; } = "step too far";
        public int Calls { get; private set; }
        public string ProbeName => "counting";

        public bool ValidateMove(string playerId, int fromX, int fromY, int toX, int toY, out string reason)
        {
            Calls++;
            reason = Allow ? "" : Reason;
            return Allow;
        }

        public bool ValidateAttackInterval(string playerId, string skillId, long nowUnixMs, long readyAtUnixMs, out string reason)
        {
            reason = "";
            return true;
        }
    }
}
