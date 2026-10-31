using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private async Task HandleLearnSkillAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = LearnSkillReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.LearnSkillResp, frame.Seq, new LearnSkillResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        string itemId = (req.ItemId ?? string.Empty).Trim();
        string skillId = (req.SkillId ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(itemId) && !string.IsNullOrEmpty(skillId) &&
            _versionPack.Skills.TryGetValue(skillId, out var bySkill) &&
            !string.IsNullOrEmpty(bySkill.SkillBookItemId))
        {
            itemId = bySkill.SkillBookItemId;
        }

        if (string.IsNullOrEmpty(itemId) ||
            !_versionPack.Items.TryGetValue(itemId, out var tmpl) ||
            !string.Equals(tmpl.Kind, "skillbook", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(tmpl.TeachesSkillId))
        {
            await session.SendAsync(MsgId.LearnSkillResp, frame.Seq, new LearnSkillResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSkillLearnDenied,
                Message = "无效技能书。"
            }, ct);
            return;
        }

        skillId = tmpl.TeachesSkillId;
        var (ok, err, state) = await TryLearnSkillAsync(player, skillId, consumeItemId: itemId, ct);
        if (!ok || state is null)
        {
            await session.SendAsync(MsgId.LearnSkillResp, frame.Seq, new LearnSkillResp
            {
                Success = false,
                ErrorCode = err,
                Message = MessageFor(err)
            }, ct);
            return;
        }

        _versionPack.Skills.TryGetValue(skillId, out var cfg);
        var resp = new LearnSkillResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            Skill = SkillHelper.ToProto(state, cfg)
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        await session.SendAsync(MsgId.LearnSkillResp, frame.Seq, resp, ct);
        await SendSkillListNotifyAsync(player, "learn", ct);
    }

    private async Task HandleSkillListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.SkillListResp, frame.Seq, new SkillListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        var resp = new SkillListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        };
        foreach (var s in player.LearnedSkills)
        {
            _versionPack.Skills.TryGetValue(s.SkillId, out var cfg);
            resp.Skills.Add(SkillHelper.ToProto(s, cfg));
        }
        await session.SendAsync(MsgId.SkillListResp, frame.Seq, resp, ct);
    }

    private async Task<(bool Ok, ProtocolErrorCode Err, LearnedSkillState? State)> TryLearnSkillAsync(
        WorldPlayerEntity player, string skillId, string? consumeItemId, CancellationToken ct)
    {
        if (!_versionPack.Skills.TryGetValue(skillId, out var cfg))
            return (false, ProtocolErrorCode.ErrSkillLearnDenied, null);

        if (cfg.AllowedClasses.Count > 0 &&
            !cfg.AllowedClasses.Any(c => string.Equals(c, player.ClassId, StringComparison.OrdinalIgnoreCase)))
            return (false, ProtocolErrorCode.ErrSkillLearnDenied, null);

        if (player.Level < Math.Max(1, cfg.LearnRequiredLevel))
            return (false, ProtocolErrorCode.ErrSkillLearnDenied, null);

        if (player.LearnedSkills.Any(s => string.Equals(s.SkillId, skillId, StringComparison.OrdinalIgnoreCase)))
            return (false, ProtocolErrorCode.ErrSkillLearnDenied, null);

        if (!string.IsNullOrEmpty(consumeItemId))
        {
            if (!InventoryHelper.TryRemoveItem(player.Inventory, consumeItemId, 1))
                return (false, ProtocolErrorCode.ErrSkillLearnDenied, null);
            await _storage.Characters.SaveInventoryAsync(
                player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
        }

        var state = new LearnedSkillState { SkillId = skillId, Level = 1, Exp = 0 };
        player.LearnedSkills.Add(state);
        await PersistSkillsAsync(player, ct);

        await _eventBus.PublishAsync(new SkillLearnedEvent(
            player.Session, player.Name, player.CharacterId, skillId, 1), ct);

        return (true, ProtocolErrorCode.ErrNone, state);
    }

    private void EnsureStarterSkills(WorldPlayerEntity player)
    {
        bool changed = false;
        foreach (var cfg in _versionPack.Skills.Values)
        {
            if (cfg.RequiresLearn)
                continue;
            if (cfg.AllowedClasses.Count > 0 &&
                !cfg.AllowedClasses.Any(c => string.Equals(c, player.ClassId, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (player.LearnedSkills.Any(s => string.Equals(s.SkillId, cfg.SkillId, StringComparison.OrdinalIgnoreCase)))
                continue;
            player.LearnedSkills.Add(new LearnedSkillState { SkillId = cfg.SkillId, Level = 1, Exp = 0 });
            changed = true;
        }
        if (changed)
        {
            // fire-and-forget persist is ok on enter; await at call site
        }
    }

    private async Task PersistSkillsAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        await _storage.Characters.SaveSkillsAsync(
            player.CharacterId, SkillHelper.Serialize(player.LearnedSkills), ct);
    }

    private async Task SendSkillListNotifyAsync(WorldPlayerEntity player, string reason, CancellationToken ct)
    {
        var notify = new SkillListNotify { Reason = reason };
        foreach (var s in player.LearnedSkills)
        {
            _versionPack.Skills.TryGetValue(s.SkillId, out var cfg);
            notify.Skills.Add(SkillHelper.ToProto(s, cfg));
        }
        await player.Session.SendAsync(MsgId.SkillListNotify, 0, notify, ct);
    }

    private bool HasLearnedSkill(WorldPlayerEntity player, string skillId, out LearnedSkillState? state)
    {
        state = player.LearnedSkills.FirstOrDefault(s =>
            string.Equals(s.SkillId, skillId, StringComparison.OrdinalIgnoreCase));
        return state != null;
    }

    private async Task GrantSkillExpAsync(WorldPlayerEntity player, SkillConfigItem cfg, int amount, CancellationToken ct)
    {
        if (amount <= 0) return;
        if (!HasLearnedSkill(player, cfg.SkillId, out var state) || state is null)
            return;
        if (state.Level >= Math.Max(1, cfg.MaxLevel))
            return;

        state.Exp += amount;
        bool leveled = false;
        int oldLevel = state.Level;
        while (state.Level < cfg.MaxLevel)
        {
            int need = SkillHelper.ExpToNext(cfg, state.Level);
            if (need <= 0 || state.Exp < need)
                break;
            state.Exp -= need;
            state.Level++;
            leveled = true;
        }
        if (state.Level >= cfg.MaxLevel)
            state.Exp = 0;

        await PersistSkillsAsync(player, ct);

        if (leveled)
        {
            await player.Session.SendAsync(MsgId.SkillLevelUpNotify, 0, new SkillLevelUpNotify
            {
                SkillId = cfg.SkillId,
                OldLevel = oldLevel,
                NewLevel = state.Level,
                Exp = state.Exp,
                ExpToNext = SkillHelper.ExpToNext(cfg, state.Level)
            }, ct);
            await SendSkillListNotifyAsync(player, "level_up", ct);
            await _eventBus.PublishAsync(new SkillLevelUpEvent(
                player.Session, player.Name, player.CharacterId, cfg.SkillId, oldLevel, state.Level), ct);
        }
    }

    private async Task ApplyBuffAsync(WorldPlayerEntity player, string buffId, CancellationToken ct)
    {
        bool known = _versionPack.Buffs.TryGetValue(buffId, out var cfg);
        var existing = player.ActiveBuffs.FirstOrDefault(b =>
            string.Equals(b.BuffId, buffId, StringComparison.OrdinalIgnoreCase));
        string rule = known ? (cfg!.StackRule ?? "refresh") : "refresh";
        var decision = _combat.EvaluateBuffApply(known, rule, existing != null);
        if (!decision.Accepted)
            return; // same silent deny as before; ErrBuffDenied is exposed on the port for callers/tests
        if (!known || cfg is null)
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        rule = (cfg.StackRule ?? "refresh").ToLowerInvariant();
        if (existing != null)
        {
            if (rule == "deny")
                return;
            if (rule == "stack")
                existing.Stacks = Math.Min(5, existing.Stacks + 1);
            existing.ExpiresAtUnixMs = now + Math.Max(100, cfg.DurationMs);
            existing.NextTickUnixMs = cfg.TickMs > 0 ? now + cfg.TickMs : 0;
            if (cfg.AbsorbAmount > 0)
                existing.AbsorbRemaining = cfg.AbsorbAmount;
        }
        else
        {
            player.ActiveBuffs.Add(new ActiveBuffState
            {
                BuffId = buffId,
                Stacks = 1,
                ExpiresAtUnixMs = now + Math.Max(100, cfg.DurationMs),
                NextTickUnixMs = cfg.TickMs > 0 ? now + cfg.TickMs : 0,
                AbsorbRemaining = Math.Max(0, cfg.AbsorbAmount)
            });
        }

        EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
        var ab = player.ActiveBuffs.First(b => string.Equals(b.BuffId, buffId, StringComparison.OrdinalIgnoreCase));
        await player.Session.SendAsync(MsgId.BuffApplyNotify, 0, new BuffApplyNotify
        {
            Buff = BuffHelper.ToProto(ab, cfg),
            TargetEntityId = player.EntityId,
            EffectiveAtk = player.Atk,
            EffectiveDef = player.Def
        }, ct);

        await _eventBus.PublishAsync(new BuffAppliedEvent(
            player.Session, player.Name, player.CharacterId, buffId, cfg.DurationMs), ct);
    }

    private async Task RemoveBuffAsync(WorldPlayerEntity player, string buffId, string reason, CancellationToken ct)
    {
        int removed = player.ActiveBuffs.RemoveAll(b =>
            string.Equals(b.BuffId, buffId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;

        EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
        await player.Session.SendAsync(MsgId.BuffRemoveNotify, 0, new BuffRemoveNotify
        {
            BuffId = buffId,
            TargetEntityId = player.EntityId,
            Reason = reason,
            EffectiveAtk = player.Atk,
            EffectiveDef = player.Def
        }, ct);
        await _eventBus.PublishAsync(new BuffExpiredEvent(
            player.Session, player.Name, player.CharacterId, buffId, reason), ct);
    }

    private async Task ClearBuffsOnDeathAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        var toClear = player.ActiveBuffs
            .Where(b => _versionPack.Buffs.TryGetValue(b.BuffId, out var cfg) && cfg.ClearOnDeath)
            .Select(b => b.BuffId)
            .ToList();
        foreach (var id in toClear)
            await RemoveBuffAsync(player, id, "death", ct);
    }

    private async Task TickPlayerBuffsAsync(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var aoi in _maps.Values)
        {
            foreach (var player in aoi.AllPlayers().ToList())
            {
                if (player.ActiveBuffs.Count == 0) continue;

                foreach (var buff in player.ActiveBuffs.ToList())
                {
                    if (!_versionPack.Buffs.TryGetValue(buff.BuffId, out var cfg))
                    {
                        await RemoveBuffAsync(player, buff.BuffId, "expire", ct);
                        continue;
                    }

                    if (cfg.TickMs > 0 && buff.NextTickUnixMs > 0 && now >= buff.NextTickUnixMs)
                    {
                        if (cfg.DotDamagePerTick > 0 && !player.IsDead)
                        {
                            player.Hp = Math.Max(0, player.Hp - cfg.DotDamagePerTick);
                            await player.Session.SendAsync(MsgId.CombatSync, 0, new CombatSync
                            {
                                AttackerEntityId = "buff:" + buff.BuffId,
                                TargetEntityId = player.EntityId,
                                SkillId = buff.BuffId,
                                Damage = cfg.DotDamagePerTick,
                                TargetHp = player.Hp,
                                TargetMaxHp = player.MaxHp,
                                Result = player.Hp <= 0 ? CombatResultKind.CombatKill : CombatResultKind.CombatHit,
                                TargetDied = player.Hp <= 0,
                                MapId = player.MapId
                            }, ct);
                            if (player.Hp <= 0)
                            {
                                player.IsDead = true;
                                await ClearBuffsOnDeathAsync(player, ct);
                            }
                        }
                        buff.NextTickUnixMs = now + cfg.TickMs;
                    }

                    if (now >= buff.ExpiresAtUnixMs)
                        await RemoveBuffAsync(player, buff.BuffId, "expire", ct);
                }
            }
        }
    }

    private static string MessageFor(ProtocolErrorCode code) => code switch
    {
        ProtocolErrorCode.ErrSkillNotLearned => "尚未学会该技能。",
        ProtocolErrorCode.ErrSkillLearnDenied => "无法学习该技能。",
        ProtocolErrorCode.ErrSkillMaxLevel => "技能已达最高等级。",
        ProtocolErrorCode.ErrBuffDenied => "无法施加该状态。",
        _ => "失败"
    };
}
