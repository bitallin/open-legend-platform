using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

/// <summary>
/// Pure attack / skill / buff settlement gates. World mutation stays in <see cref="GameWorldModule"/>.
/// Damage numbers stay in <see cref="CombatMath"/>; this port only forwards them.
/// </summary>
public interface ICombatResolver
{
    CombatGateDecision EvaluateAttackGate(CombatGateContext context);
    CombatGateDecision EvaluateRange(int attackerX, int attackerY, int targetX, int targetY, int skillRange);
    BuffApplyDecision EvaluateBuffApply(bool buffKnown, string stackRule, bool alreadyActive);
    int CalcDamage(int atk, int def, double multiplier);
    double EffectiveMultiplier(SkillConfigItem skill, int level);
    int EffectiveCooldownMs(SkillConfigItem skill, int level, int castTimeMs);
}

public readonly record struct CombatGateContext(
    string PlayerId,
    bool IsDead,
    bool InSafeZone,
    bool SkillKnown,
    bool ClassAllowed,
    bool SkillLearned,
    string SkillId,
    long NowUnixMs,
    long SkillReadyAtUnixMs,
    int Mp,
    int MpCost,
    IAntiCheatProbe Probe);

public readonly record struct CombatGateDecision(
    bool Accepted,
    ProtocolErrorCode ErrorCode,
    string Message,
    long ReadyAtUnixMs)
{
    public static CombatGateDecision Allow(long readyAt = 0) =>
        new(true, ProtocolErrorCode.ErrNone, "OK", readyAt);

    public static CombatGateDecision Deny(ProtocolErrorCode code, string message, long readyAt = 0) =>
        new(false, code, message, readyAt);
}

public readonly record struct BuffApplyDecision(
    bool Accepted,
    ProtocolErrorCode ErrorCode,
    string Message)
{
    public static BuffApplyDecision Allow() =>
        new(true, ProtocolErrorCode.ErrNone, "OK");

    public static BuffApplyDecision Deny(ProtocolErrorCode code, string message) =>
        new(false, code, message);
}

public sealed class DefaultCombatResolver : ICombatResolver
{
    public static DefaultCombatResolver Instance { get; } = new();

    public CombatGateDecision EvaluateAttackGate(CombatGateContext context)
    {
        if (context.IsDead)
            return CombatGateDecision.Deny(ProtocolErrorCode.ErrDead, "You are dead.");

        if (context.InSafeZone)
            return CombatGateDecision.Deny(ProtocolErrorCode.ErrSafeZone, "安全区内禁止攻击。");

        if (!context.SkillKnown)
            return CombatGateDecision.Deny(ProtocolErrorCode.ErrInvalidSkill, $"未知技能: {context.SkillId}");

        if (!context.ClassAllowed)
            return CombatGateDecision.Deny(ProtocolErrorCode.ErrInvalidSkill, $"职业无法使用技能 {context.SkillId}");

        if (!context.SkillLearned)
            return CombatGateDecision.Deny(ProtocolErrorCode.ErrSkillNotLearned, $"尚未学会技能: {context.SkillId}");

        if (!context.Probe.ValidateAttackInterval(
                context.PlayerId,
                context.SkillId,
                context.NowUnixMs,
                context.SkillReadyAtUnixMs,
                out string cooldownReason))
        {
            return CombatGateDecision.Deny(
                ProtocolErrorCode.ErrSkillCooldown,
                string.IsNullOrWhiteSpace(cooldownReason) ? "技能冷却中" : cooldownReason,
                context.SkillReadyAtUnixMs);
        }

        if (context.Mp < context.MpCost)
            return CombatGateDecision.Deny(ProtocolErrorCode.ErrInvalidSkill, "MP 不足");

        return CombatGateDecision.Allow(context.SkillReadyAtUnixMs);
    }

    public CombatGateDecision EvaluateRange(int attackerX, int attackerY, int targetX, int targetY, int skillRange)
    {
        int dist = CombatMath.Chebyshev(attackerX, attackerY, targetX, targetY);
        if (dist > skillRange)
        {
            return CombatGateDecision.Deny(
                ProtocolErrorCode.ErrOutOfRange,
                $"目标超出技能距离 ({dist}>{skillRange})");
        }

        return CombatGateDecision.Allow();
    }

    public BuffApplyDecision EvaluateBuffApply(bool buffKnown, string stackRule, bool alreadyActive)
    {
        if (!buffKnown)
            return BuffApplyDecision.Deny(ProtocolErrorCode.ErrBuffDenied, "无法施加该状态。");

        if (alreadyActive &&
            string.Equals((stackRule ?? "refresh").Trim(), "deny", StringComparison.OrdinalIgnoreCase))
        {
            return BuffApplyDecision.Deny(ProtocolErrorCode.ErrBuffDenied, "无法施加该状态。");
        }

        return BuffApplyDecision.Allow();
    }

    public int CalcDamage(int atk, int def, double multiplier) =>
        CombatMath.CalcDamage(atk, def, multiplier);

    public double EffectiveMultiplier(SkillConfigItem skill, int level) =>
        SkillHelper.EffectiveMultiplier(skill, level);

    public int EffectiveCooldownMs(SkillConfigItem skill, int level, int castTimeMs)
    {
        int cooldownMs = SkillHelper.EffectiveCooldownMs(skill, level);
        if (castTimeMs > 0)
            cooldownMs = Math.Max(cooldownMs, castTimeMs);
        return cooldownMs;
    }
}
