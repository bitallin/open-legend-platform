using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Game;

namespace Legend.Tests;

public class CombatResolverTests
{
    private readonly DefaultCombatResolver _combat = DefaultCombatResolver.Instance;

    [Fact]
    public void SafeZoneAttackReturnsErrSafeZone()
    {
        var decision = _combat.EvaluateAttackGate(Gate(inSafeZone: true));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrSafeZone, decision.ErrorCode);
        Assert.Equal(1015, (int)decision.ErrorCode);
    }

    [Fact]
    public void DeadAttackerReturnsErrDead()
    {
        var decision = _combat.EvaluateAttackGate(Gate(isDead: true));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrDead, decision.ErrorCode);
        Assert.Equal(1018, (int)decision.ErrorCode);
    }

    [Fact]
    public void UnknownSkillReturnsErrInvalidSkill()
    {
        var decision = _combat.EvaluateAttackGate(Gate(skillKnown: false));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrInvalidSkill, decision.ErrorCode);
        Assert.Equal(1012, (int)decision.ErrorCode);
    }

    [Fact]
    public void CooldownProbeRejectionReturnsErrSkillCooldown()
    {
        var probe = new CountingAttackProbe { AllowAttack = false, AttackReason = "技能冷却中" };
        var decision = _combat.EvaluateAttackGate(Gate(probe: probe, readyAt: 5_000, now: 4_000));
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrSkillCooldown, decision.ErrorCode);
        Assert.Equal(1013, (int)decision.ErrorCode);
        Assert.Equal(5_000, decision.ReadyAtUnixMs);
        Assert.Equal(1, probe.AttackCalls);
    }

    [Fact]
    public void DefaultProbeMatchesLegacyCooldownCheck()
    {
        var probe = new DefaultAntiCheatProbe();
        Assert.False(probe.ValidateAttackInterval("p", "slash", 1_000, 2_000, out var reason));
        Assert.Equal("技能冷却中", reason);
        Assert.True(probe.ValidateAttackInterval("p", "slash", 2_000, 2_000, out _));
        Assert.True(probe.ValidateAttackInterval("p", "slash", 3_000, 0, out _));
    }

    [Fact]
    public void ProbeCanBeReplacedByTests()
    {
        var probe = new CountingAttackProbe { AllowAttack = true };
        var decision = _combat.EvaluateAttackGate(Gate(probe: probe, readyAt: 9_999, now: 1));
        Assert.True(decision.Accepted);
        Assert.Equal(1, probe.AttackCalls);
    }

    [Fact]
    public void OutOfRangeReturnsErrOutOfRange()
    {
        var decision = _combat.EvaluateRange(0, 0, 5, 0, skillRange: 1);
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrOutOfRange, decision.ErrorCode);
        Assert.Equal(1014, (int)decision.ErrorCode);
    }

    [Fact]
    public void CalcDamageLocksExistingNumbers()
    {
        Assert.Equal(24, _combat.CalcDamage(25, 1, 1.0));
        Assert.Equal(1, _combat.CalcDamage(5, 100, 1.0));
        Assert.Equal(42, _combat.CalcDamage(25, 1, 1.75));
        Assert.Equal(CombatMath.CalcDamage(25, 1, 1.0), _combat.CalcDamage(25, 1, 1.0));
    }

    [Fact]
    public void BuffUnknownReturnsErrBuffDenied()
    {
        var decision = _combat.EvaluateBuffApply(buffKnown: false, stackRule: "refresh", alreadyActive: false);
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrBuffDenied, decision.ErrorCode);
        Assert.Equal(1051, (int)decision.ErrorCode);
    }

    [Fact]
    public void BuffDenyStackRuleWhileActiveIsDenied()
    {
        var decision = _combat.EvaluateBuffApply(buffKnown: true, stackRule: "deny", alreadyActive: true);
        Assert.False(decision.Accepted);
        Assert.Equal(ProtocolErrorCode.ErrBuffDenied, decision.ErrorCode);
    }

    private static CombatGateContext Gate(
        bool isDead = false,
        bool inSafeZone = false,
        bool skillKnown = true,
        bool classAllowed = true,
        bool skillLearned = true,
        long now = 10_000,
        long readyAt = 0,
        int mp = 100,
        int mpCost = 0,
        IAntiCheatProbe? probe = null)
        => new(
            "player-1",
            isDead,
            inSafeZone,
            skillKnown,
            classAllowed,
            skillLearned,
            "basic_slash",
            now,
            readyAt,
            mp,
            mpCost,
            probe ?? new CountingAttackProbe());

    private sealed class CountingAttackProbe : IAntiCheatProbe
    {
        public bool AllowAttack { get; init; } = true;
        public string AttackReason { get; init; } = "技能冷却中";
        public int AttackCalls { get; private set; }
        public string ProbeName => "counting-attack";

        public bool ValidateMove(string playerId, int fromX, int fromY, int toX, int toY, out string reason)
        {
            reason = "";
            return true;
        }

        public bool ValidateAttackInterval(string playerId, string skillId, long nowUnixMs, long readyAtUnixMs, out string reason)
        {
            AttackCalls++;
            reason = AllowAttack ? "" : AttackReason;
            return AllowAttack;
        }
    }
}
