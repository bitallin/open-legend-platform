using System.Text.Json;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly SemaphoreSlim _siegeLock = new(1, 1);
    private bool _siegeIsActive;
    private string _siegeOwnerGuildId = string.Empty;
    private string _siegeOwnerGuildName = string.Empty;
    private readonly List<SiegeDeclaredGuildEntry> _siegeDeclaredGuilds = new();

    private static readonly JsonSerializerOptions SiegeJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task LoadSiegeStateAsync(CancellationToken ct)
    {
        string castleId = _versionPack.Siege.CastleId;
        var rec = await _storage.Siege.GetAsync(castleId, ct);
        await _siegeLock.WaitAsync(ct);
        try
        {
            _siegeDeclaredGuilds.Clear();
            if (rec != null)
            {
                _siegeOwnerGuildId = rec.OwnerGuildId ?? string.Empty;
                _siegeOwnerGuildName = rec.OwnerGuildName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(rec.DeclaredGuildsJson))
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<SiegeDeclaredGuildEntry>>(rec.DeclaredGuildsJson, SiegeJsonOpts);
                        if (list != null)
                            _siegeDeclaredGuilds.AddRange(list);
                    }
                    catch
                    {
                        // ignore malformed json
                    }
                }
            }
            else
            {
                _siegeOwnerGuildId = string.Empty;
                _siegeOwnerGuildName = string.Empty;
            }
        }
        finally
        {
            _siegeLock.Release();
        }
    }

    private async Task PersistSiegeStateLockedAsync(CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(_siegeDeclaredGuilds, SiegeJsonOpts);
        await _storage.Siege.UpsertAsync(new SiegeStateRecord
        {
            CastleId = _versionPack.Siege.CastleId,
            OwnerGuildId = _siegeOwnerGuildId,
            OwnerGuildName = _siegeOwnerGuildName,
            DeclaredGuildsJson = json,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);
    }

    private bool IsCastleLordGuild(string? guildId) =>
        !string.IsNullOrEmpty(guildId) &&
        !string.IsNullOrEmpty(_siegeOwnerGuildId) &&
        string.Equals(guildId, _siegeOwnerGuildId, StringComparison.Ordinal);

    private void ApplyCastleLordBuffOnEnterIfEligible(WorldPlayerEntity entity)
    {
        if (!IsCastleLordGuild(entity.GuildId))
            return;
        string buffId = _versionPack.Siege.OwnerBuffId;
        if (string.IsNullOrWhiteSpace(buffId) || !_versionPack.Buffs.TryGetValue(buffId, out var cfg))
            return;
        if (entity.ActiveBuffs.Any(b => string.Equals(b.BuffId, buffId, StringComparison.OrdinalIgnoreCase)))
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        entity.ActiveBuffs.Add(new ActiveBuffState
        {
            BuffId = buffId,
            Stacks = 1,
            ExpiresAtUnixMs = now + Math.Max(60000, cfg.DurationMs),
            NextTickUnixMs = cfg.TickMs > 0 ? now + cfg.TickMs : 0
        });
    }

    private async Task RefreshOnlineCastleLordBuffsAsync(string newOwnerGuildId, CancellationToken ct)
    {
        string buffId = _versionPack.Siege.OwnerBuffId;
        if (string.IsNullOrWhiteSpace(buffId) || !_versionPack.Buffs.ContainsKey(buffId))
            return;

        foreach (var aoi in _maps.Values)
        {
            foreach (var p in aoi.AllPlayers().ToList())
            {
                bool isOwnerMember = !string.IsNullOrEmpty(newOwnerGuildId) &&
                                     string.Equals(p.GuildId, newOwnerGuildId, StringComparison.Ordinal);
                bool hasBuff = p.ActiveBuffs.Any(b => string.Equals(b.BuffId, buffId, StringComparison.OrdinalIgnoreCase));
                if (isOwnerMember)
                {
                    await ApplyBuffAsync(p, buffId, ct);
                }
                else if (hasBuff)
                {
                    await RemoveBuffAsync(p, buffId, "siege_lost", ct);
                }
            }
        }
    }

    private SiegeStateNotify BuildSiegeStateNotify(string reason)
    {
        var notify = new SiegeStateNotify
        {
            CastleId = _versionPack.Siege.CastleId,
            CastleName = _versionPack.Siege.DisplayName,
            IsActive = _siegeIsActive,
            OwnerGuildId = _siegeOwnerGuildId,
            OwnerGuildName = _siegeOwnerGuildName,
            Reason = reason
        };
        foreach (var g in _siegeDeclaredGuilds)
        {
            notify.DeclaredGuildIds.Add(g.GuildId);
            notify.DeclaredGuildNames.Add(g.GuildName);
        }
        return notify;
    }

    private async Task BroadcastSiegeStateNotifyAsync(string reason, CancellationToken ct)
    {
        var notify = BuildSiegeStateNotify(reason);
        foreach (var aoi in _maps.Values)
        {
            foreach (var p in aoi.AllPlayers().ToList())
            {
                await p.Session.SendAsync(MsgId.SiegeStateNotify, 0, notify, ct);
            }
        }
    }

    private async Task HandleSiegeDeclareAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        _ = SiegeDeclareReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.SiegeDeclareResp, frame.Seq, new SiegeDeclareResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(player.GuildId))
        {
            await session.SendAsync(MsgId.SiegeDeclareResp, frame.Seq, new SiegeDeclareResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSiegeDenied,
                Message = "未加入行会，无法报名攻城。"
            }, ct);
            return;
        }

        if (_versionPack.Siege.LeaderOnlyDeclare &&
            !string.Equals(player.GuildRank, "leader", StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.SiegeDeclareResp, frame.Seq, new SiegeDeclareResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSiegeDenied,
                Message = "仅行会会长可报名攻城。"
            }, ct);
            return;
        }

        int cost = Math.Max(0, _versionPack.Siege.DeclareCostGold);
        SiegeDeclareResp resp;

        await _siegeLock.WaitAsync(ct);
        try
        {
            if (_siegeDeclaredGuilds.Any(g => string.Equals(g.GuildId, player.GuildId, StringComparison.Ordinal)))
            {
                await session.SendAsync(MsgId.SiegeDeclareResp, frame.Seq, new SiegeDeclareResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrSiegeAlreadyDeclared,
                    Message = "本行会已报名本次攻城战。",
                    NewGold = player.Gold
                }, ct);
                return;
            }

            if (player.Gold < cost)
            {
                await session.SendAsync(MsgId.SiegeDeclareResp, frame.Seq, new SiegeDeclareResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                    Message = $"金币不足（需 {cost} 金币）",
                    NewGold = player.Gold
                }, ct);
                return;
            }

            player.Gold -= cost;
            await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);

            _siegeDeclaredGuilds.Add(new SiegeDeclaredGuildEntry
            {
                GuildId = player.GuildId,
                GuildName = player.GuildName
            });
            await PersistSiegeStateLockedAsync(ct);

            resp = new SiegeDeclareResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = $"行会 [{player.GuildName}] 已成功报名沙巴克攻城战！",
                NewGold = player.Gold
            };
            foreach (var g in _siegeDeclaredGuilds)
            {
                resp.DeclaredGuildIds.Add(g.GuildId);
                resp.DeclaredGuildNames.Add(g.GuildName);
            }
        }
        finally
        {
            _siegeLock.Release();
        }

        await session.SendAsync(MsgId.SiegeDeclareResp, frame.Seq, resp, ct);
        await BroadcastSiegeStateNotifyAsync("declared", ct);
        await _eventBus.PublishAsync(new SiegeDeclaredEvent(
            session, _versionPack.Siege.CastleId, player.GuildId, player.GuildName, player.Name, cost), ct);
    }

    private async Task HandleSiegeStatusAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        _ = SiegeStatusReq.Parser.ParseFrom(frame.Payload);
        SiegeStatusResp resp;
        await _siegeLock.WaitAsync(ct);
        try
        {
            resp = new SiegeStatusResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "OK",
                ScheduleNote = _versionPack.Siege.ScheduleNote ?? "",
                CastleId = _versionPack.Siege.CastleId,
                CastleName = _versionPack.Siege.DisplayName,
                IsActive = _siegeIsActive,
                OwnerGuildId = _siegeOwnerGuildId,
                OwnerGuildName = _siegeOwnerGuildName
            };
            foreach (var g in _siegeDeclaredGuilds)
            {
                resp.DeclaredGuildIds.Add(g.GuildId);
                resp.DeclaredGuildNames.Add(g.GuildName);
            }
        }
        finally
        {
            _siegeLock.Release();
        }

        await session.SendAsync(MsgId.SiegeStatusResp, frame.Seq, resp, ct);
    }

    private async Task HandleSiegeControlAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = SiegeControlReq.Parser.ParseFrom(frame.Payload);
        string action = (req.Action ?? "start").Trim().ToLowerInvariant();

        if (action is "start" or "open")
        {
            await _siegeLock.WaitAsync(ct);
            try
            {
                _siegeIsActive = true;
            }
            finally
            {
                _siegeLock.Release();
            }

            await session.SendAsync(MsgId.SiegeControlResp, frame.Seq, new SiegeControlResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "沙巴克攻城战已开启！",
                IsActive = true,
                OwnerGuildId = _siegeOwnerGuildId,
                OwnerGuildName = _siegeOwnerGuildName
            }, ct);
            await BroadcastSiegeStateNotifyAsync("started", ct);
            return;
        }

        if (action is "stop" or "end" or "close")
        {
            string winnerId;
            string winnerName;
            await _siegeLock.WaitAsync(ct);
            try
            {
                _siegeIsActive = false;
                winnerId = _siegeOwnerGuildId;
                winnerName = _siegeOwnerGuildName;
                await PersistSiegeStateLockedAsync(ct);
            }
            finally
            {
                _siegeLock.Release();
            }

            if (!string.IsNullOrEmpty(winnerId))
            {
                await RefreshOnlineCastleLordBuffsAsync(winnerId, ct);
                int reward = Math.Max(0, _versionPack.Siege.WinnerRewardGold);
                if (reward > 0)
                {
                    var rewardedOnline = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var aoi in _maps.Values)
                    {
                        foreach (var p in aoi.AllPlayers().ToList())
                        {
                            if (string.Equals(p.GuildId, winnerId, StringComparison.Ordinal))
                            {
                                p.Gold += reward;
                                await _storage.Characters.SaveGoldAsync(p.CharacterId, p.Gold, ct);
                                var invNotify = new InventoryNotify { Gold = p.Gold, Reason = "siege_reward" };
                                foreach (var it in InventoryHelper.ToProto(p.Inventory))
                                    invNotify.Items.Add(it);
                                await p.Session.SendAsync(MsgId.InventoryNotify, 0, invNotify, ct);
                                rewardedOnline.Add(p.CharacterId);
                            }
                        }
                    }

                    // Offline guild members: reward via mail attachment only (mutually exclusive with online instant gold).
                    var guild = await _storage.Guilds.GetByIdAsync(winnerId, ct);
                    if (guild != null)
                    {
                        foreach (var memberId in guild.MemberCharacterIds.ToList())
                        {
                            if (rewardedOnline.Contains(memberId))
                                continue;
                            var ch = await _storage.Characters.GetByIdAsync(memberId, ct);
                            string name = ch?.Name ?? memberId;
                            await SendMailAsync(
                                memberId,
                                name,
                                MailCfg.SystemSenderName,
                                "攻城战胜利奖励",
                                $"行会 [{winnerName}] 占领沙巴克，离线成员奖励 {reward} 金（请领取附件）。",
                                reward,
                                null,
                                ct);
                        }
                    }
                }
            }

            await session.SendAsync(MsgId.SiegeControlResp, frame.Seq, new SiegeControlResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = string.IsNullOrEmpty(winnerId)
                    ? "沙巴克攻城战已结束（无占领行会）。"
                    : $"沙巴克攻城战已结束，占领行会：[{winnerName}]！",
                IsActive = false,
                OwnerGuildId = winnerId,
                OwnerGuildName = winnerName
            }, ct);
            await BroadcastSiegeStateNotifyAsync("ended", ct);
            await _eventBus.PublishAsync(new SiegeEndedEvent(_versionPack.Siege.CastleId, winnerId, winnerName), ct);
            return;
        }

        await session.SendAsync(MsgId.SiegeControlResp, frame.Seq, new SiegeControlResp
        {
            Success = false,
            ErrorCode = ProtocolErrorCode.ErrSiegeDenied,
            Message = $"未知攻城控制指令: {req.Action}",
            IsActive = _siegeIsActive,
            OwnerGuildId = _siegeOwnerGuildId,
            OwnerGuildName = _siegeOwnerGuildName
        }, ct);
    }

    private async Task HandleSiegeCaptureAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        _ = SiegeCaptureReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out var aoi) || player is null || aoi is null)
        {
            await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (player.IsDead || player.Hp <= 0)
        {
            await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "死亡状态无法占领皇宫。"
            }, ct);
            return;
        }

        if (!_siegeIsActive)
        {
            await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSiegeNotActive,
                Message = "当前不在攻城战开启期间。",
                OwnerGuildId = _siegeOwnerGuildId,
                OwnerGuildName = _siegeOwnerGuildName
            }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(player.GuildId))
        {
            await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSiegeDenied,
                Message = "未加入行会，无法夺取皇宫。"
            }, ct);
            return;
        }

        var zone = _versionPack.Siege.CaptureZone;
        if (!zone.Contains(player.MapId, player.X, player.Y))
        {
            await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSiegeDenied,
                Message = $"不在皇宫占领区域内（需在 {zone.MapId} [{zone.X1},{zone.Y1}]-[{zone.X2},{zone.Y2}]）。",
                OwnerGuildId = _siegeOwnerGuildId,
                OwnerGuildName = _siegeOwnerGuildName
            }, ct);
            return;
        }

        await _siegeLock.WaitAsync(ct);
        try
        {
            if (!_siegeDeclaredGuilds.Any(g => string.Equals(g.GuildId, player.GuildId, StringComparison.Ordinal)))
            {
                await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrSiegeDenied,
                    Message = "本行会尚未报名攻城战，无法夺旗占领。",
                    OwnerGuildId = _siegeOwnerGuildId,
                    OwnerGuildName = _siegeOwnerGuildName
                }, ct);
                return;
            }

            // Check if any alive player from another guild (or guildless) is inside the capture zone
            var zoneAoi = GetAoi(zone.MapId);
            bool contested = zoneAoi.AllPlayers().Any(other =>
                other.EntityId != player.EntityId &&
                !other.IsDead && other.Hp > 0 &&
                zone.Contains(other.MapId, other.X, other.Y) &&
                !string.Equals(other.GuildId, player.GuildId, StringComparison.Ordinal));

            if (contested)
            {
                await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrSiegeContested,
                    Message = "皇宫区域内仍有其他行会存活玩家，须先击败清场后才可夺取皇宫！",
                    OwnerGuildId = _siegeOwnerGuildId,
                    OwnerGuildName = _siegeOwnerGuildName
                }, ct);
                return;
            }

            _siegeOwnerGuildId = player.GuildId;
            _siegeOwnerGuildName = player.GuildName;
            await PersistSiegeStateLockedAsync(ct);
        }
        finally
        {
            _siegeLock.Release();
        }

        await RefreshOnlineCastleLordBuffsAsync(player.GuildId, ct);

        await session.SendAsync(MsgId.SiegeCaptureResp, frame.Seq, new SiegeCaptureResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = $"行会 [{player.GuildName}] 成功占领 [{_versionPack.Siege.DisplayName}] 皇宫！",
            OwnerGuildId = _siegeOwnerGuildId,
            OwnerGuildName = _siegeOwnerGuildName
        }, ct);

        await BroadcastSiegeStateNotifyAsync("captured", ct);
        await _eventBus.PublishAsync(new SiegeCapturedEvent(
            session, _versionPack.Siege.CastleId, player.GuildId, player.GuildName, player.Name), ct);
    }

    private async Task<bool> TryHandleSiegePvpAttackAsync(
        IClientSession session,
        PacketFrame frame,
        WorldPlayerEntity attacker,
        GridAoiManager aoi,
        AttackReq req,
        SkillConfigItem skill,
        double dmgMult,
        int cooldownMs,
        long now,
        CancellationToken ct)
    {
        if (!_siegeIsActive)
            return false;

        if (!aoi.TryGetEntity(req.TargetEntityId, out var targetPlayer) ||
            targetPlayer is null ||
            string.Equals(targetPlayer.EntityId, attacker.EntityId, StringComparison.Ordinal))
        {
            return false;
        }

        var zone = _versionPack.Siege.CaptureZone;
        if (!zone.Contains(attacker.MapId, attacker.X, attacker.Y) ||
            !zone.Contains(targetPlayer.MapId, targetPlayer.X, targetPlayer.Y))
        {
            return false;
        }

        if (targetPlayer.InSafeZone || aoi.Map.IsInSafeZone(targetPlayer.X, targetPlayer.Y))
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSafeZone,
                Message = "目标处于安全区内，禁止攻击。"
            }, ct);
            return true;
        }

        if (targetPlayer.IsDead || targetPlayer.Hp <= 0)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidTarget,
                Message = "目标已死亡。"
            }, ct);
            return true;
        }

        if (!string.IsNullOrEmpty(attacker.GuildId) &&
            string.Equals(attacker.GuildId, targetPlayer.GuildId, StringComparison.Ordinal))
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSiegeDenied,
                Message = "同行会成员免疫攻城伤害。"
            }, ct);
            return true;
        }

        int dist = CombatMath.Chebyshev(attacker.X, attacker.Y, targetPlayer.X, targetPlayer.Y);
        if (dist > skill.Range)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrOutOfRange,
                Message = $"目标超出技能距离 ({dist}>{skill.Range})"
            }, ct);
            return true;
        }

        attacker.Mp -= skill.MpCost;
        long nextReady = now + cooldownMs;
        attacker.SkillReadyAtUnixMs[skill.SkillId] = nextReady;

        int rawSiege = CombatMath.CalcDamage(attacker.Atk, targetPlayer.Def, dmgMult);
        int damage = await ApplyIncomingDamageAsync(targetPlayer, rawSiege, ct);
        bool targetDied = targetPlayer.Hp <= 0;
        if (targetDied)
            targetPlayer.IsDead = true;

        await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            RemainingMp = attacker.Mp,
            NextReadyUnixMs = nextReady
        }, ct);

        var combat = new CombatSync
        {
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = targetPlayer.EntityId,
            SkillId = skill.SkillId,
            Damage = damage,
            TargetHp = targetPlayer.Hp,
            TargetMaxHp = targetPlayer.MaxHp,
            Result = targetDied ? CombatResultKind.CombatKill : CombatResultKind.CombatHit,
            TargetDied = targetDied,
            MapId = attacker.MapId
        };

        await session.SendAsync(MsgId.CombatSync, 0, combat, ct);
        await targetPlayer.Session.SendAsync(MsgId.CombatSync, 0, combat, ct);
        foreach (var v in aoi.QueryPlayersNear(targetPlayer.X, targetPlayer.Y, attacker.EntityId))
        {
            if (v.EntityId != targetPlayer.EntityId)
                await v.Session.SendAsync(MsgId.CombatSync, 0, combat, ct);
        }

        await GrantSkillExpAsync(attacker, skill, skill.SkillExpPerCast + (targetDied ? skill.SkillExpPerKill : 0), ct);

        if (targetDied)
        {
            await ClearBuffsOnDeathAsync(targetPlayer, ct);
            await RevivePlayerAsync(targetPlayer, aoi, ct);
        }
        else
        {
            await _storage.Characters.SavePositionAsync(
                targetPlayer.CharacterId, targetPlayer.MapId, targetPlayer.X, targetPlayer.Y, targetPlayer.Hp, targetPlayer.Mp, ct);
        }

        return true;
    }
}
