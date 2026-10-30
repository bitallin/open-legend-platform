using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly ConcurrentDictionary<string, FaceTrade> _faceTrades = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _charToTrade = new(StringComparer.Ordinal);

    private sealed class FaceTrade
    {
        public required string Id { get; init; }
        public required string AId { get; init; }
        public required string BId { get; init; }
        public bool Accepted { get; set; }
        public List<TradeStack> AItems { get; } = new();
        public int AGold { get; set; }
        public bool ALock { get; set; }
        public List<TradeStack> BItems { get; } = new();
        public int BGold { get; set; }
        public bool BLock { get; set; }
    }

    private async Task<int> ApplyIncomingDamageAsync(WorldPlayerEntity player, int raw, CancellationToken ct)
    {
        int left = Math.Max(0, raw);
        var spent = new List<string>();
        foreach (var buff in player.ActiveBuffs)
        {
            if (left <= 0 || buff.AbsorbRemaining <= 0)
                continue;
            int use = PhaseMRules.AbsorbDamage(left, buff.AbsorbRemaining);
            buff.AbsorbRemaining -= use;
            left -= use;
            if (buff.AbsorbRemaining <= 0)
                spent.Add(buff.BuffId);
        }
        player.Hp = Math.Max(0, player.Hp - left);
        foreach (string id in spent)
            await RemoveBuffAsync(player, id, "absorb", ct);
        return left;
    }

    private async Task WearWeaponAsync(WorldPlayerEntity attacker, CancellationToken ct)
    {
        var weapon = attacker.Equipment.FirstOrDefault(e =>
            string.Equals(e.Slot, "weapon", StringComparison.OrdinalIgnoreCase));
        if (weapon == null || weapon.MaxDurability <= 0 || weapon.Durability <= 0)
            return;
        weapon.Durability -= 1;
        if (weapon.Durability <= 0)
        {
            weapon.Durability = 0;
            EquipmentHelper.RecalculateStats(attacker, _versionPack.Items, _versionPack.Buffs);
            await PersistEquipmentAsync(attacker, ct);
        }
    }

    private async Task StepChargeAsync(WorldPlayerEntity attacker, int tx, int ty, int steps, CancellationToken ct)
    {
        int budget = steps <= 0 ? 3 : steps;
        if (!_maps.TryGetValue(attacker.MapId, out var aoi))
            return;
        for (int i = 0; i < budget; i++)
        {
            if (CombatMath.Chebyshev(attacker.X, attacker.Y, tx, ty) <= 1)
                break;
            int nx = attacker.X + Math.Sign(tx - attacker.X);
            int ny = attacker.Y + Math.Sign(ty - attacker.Y);
            if (!aoi.IsWalkable(nx, ny) || aoi.Map.IsInSafeZone(nx, ny))
                break;
            int ox = attacker.X;
            int oy = attacker.Y;
            int dir = GridDirection(nx - ox, ny - oy);
            aoi.MoveEntity(attacker.EntityId, nx, ny, dir);
            var sync = new MoveSync
            {
                EntityId = attacker.EntityId,
                FromX = ox,
                FromY = oy,
                ToX = attacker.X,
                ToY = attacker.Y,
                Direction = dir,
                Accepted = true,
                Reason = "Charge",
                InSafeZone = attacker.InSafeZone
            };
            await attacker.Session.SendAsync(MsgId.MoveSync, 0, sync, ct);
            await BroadcastNearAsync(attacker.MapId, attacker.X, attacker.Y, MsgId.MoveSync, sync, ct);
        }
    }

    private static int GridDirection(int dx, int dy)
    {
        if (dx == 0 && dy > 0) return 0;
        if (dx > 0 && dy > 0) return 1;
        if (dx > 0 && dy == 0) return 2;
        if (dx > 0 && dy < 0) return 3;
        if (dx == 0 && dy < 0) return 4;
        if (dx < 0 && dy < 0) return 5;
        if (dx < 0 && dy == 0) return 6;
        if (dx < 0 && dy > 0) return 7;
        return 4;
    }

    private async Task SplashAoeAsync(
        IClientSession session,
        WorldPlayerEntity attacker,
        MonsterEntity primary,
        SkillConfigItem skill,
        double dmgMult,
        CancellationToken ct)
    {
        int radius = skill.AoeRadius <= 0 ? 1 : skill.AoeRadius;
        bool aroundTarget = string.Equals(skill.AoeCenter, "target", StringComparison.OrdinalIgnoreCase);
        int cx = aroundTarget ? primary.X : attacker.X;
        int cy = aroundTarget ? primary.Y : attacker.Y;
        foreach (var other in _monsters.Values.ToList())
        {
            if (!other.Alive || other.EntityId == primary.EntityId)
                continue;
            if (!string.IsNullOrEmpty(other.WorldBossId))
                continue;
            if (!string.Equals(other.MapId, attacker.MapId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (CombatMath.Chebyshev(cx, cy, other.X, other.Y) > radius)
                continue;
            if (string.Equals(skill.AoeShape, "arc", StringComparison.OrdinalIgnoreCase) &&
                !PhaseNRules.InFacingArc(attacker.X, attacker.Y, attacker.Direction, other.X, other.Y))
                continue;
            await HitSplashedMonsterAsync(session, attacker, other, skill, dmgMult, ct);
        }

        if (!_maps.TryGetValue(attacker.MapId, out var aoi))
            return;
        foreach (var other in aoi.AllPlayers().ToList())
        {
            if (other.EntityId == attacker.EntityId || other.IsDead || other.Hp <= 0)
                continue;
            if (other.InSafeZone || aoi.Map.IsInSafeZone(other.X, other.Y))
                continue;
            if (CombatMath.Chebyshev(cx, cy, other.X, other.Y) > radius)
                continue;
            if (string.Equals(skill.AoeShape, "arc", StringComparison.OrdinalIgnoreCase) &&
                !PhaseNRules.InFacingArc(attacker.X, attacker.Y, attacker.Direction, other.X, other.Y))
                continue;
            bool sameParty = !string.IsNullOrEmpty(attacker.PartyId) && attacker.PartyId == other.PartyId;
            bool sameGuild = !string.IsNullOrEmpty(attacker.GuildId) && attacker.GuildId == other.GuildId;
            if (!PhaseMRules.AllowsPlayerTarget(attacker.AttackMode, sameParty, sameGuild, other.PkKills, force: false))
                continue;
            int beforePk = other.PkKills;
            int dealt = await ApplyIncomingDamageAsync(other, CombatMath.CalcDamage(attacker.Atk, other.Def, dmgMult), ct);
            bool died = other.Hp <= 0;
            await SendCombatAsync(attacker, other.EntityId, other.Hp, other.MaxHp, skill.SkillId, dealt, died, ct);
            if (died)
            {
                if (beforePk < PhaseMRules.RedNameKills)
                    await AddPkKillAsync(attacker, ct);
                other.IsDead = true;
                await ClearBuffsOnDeathAsync(other, ct);
                if (!string.IsNullOrEmpty(other.ActivePetId))
                    await DespawnPetEntityAsync(other, "owner_death", persist: true, ct);
                await RevivePlayerAsync(other, aoi, ct);
            }
        }
    }

    private async Task HitSplashedMonsterAsync(
        IClientSession session,
        WorldPlayerEntity attacker,
        MonsterEntity monster,
        SkillConfigItem skill,
        double dmgMult,
        CancellationToken ct)
    {
        int damage = CombatMath.CalcDamage(attacker.Atk, monster.Template.Def, dmgMult);
        monster.Hp = Math.Max(0, monster.Hp - damage);
        monster.TargetEntityId = attacker.EntityId;
        bool killed = monster.Hp <= 0;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await SendCombatAsync(attacker, monster.EntityId, monster.Hp, monster.MaxHp, skill.SkillId, damage, killed, ct);
        if (!killed)
            return;
        monster.Alive = false;
        monster.RespawnAtUnixMs = now + Math.Max(1000, monster.Template.RespawnMs);
        await OnStructureDestroyedAsync(monster, ct);
        await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.EntityDespawn,
            new EntityDespawn { EntityId = monster.EntityId, Reason = "Killed", MapId = monster.MapId }, ct);
        await DropLootAsync(monster, attacker, ct);
        await GrantExpFromKillAsync(attacker, monster, ct);
        await UpdateQuestKillProgressAsync(attacker, monster.MonsterId, ct);
        if (monster.NoRespawn || !string.IsNullOrEmpty(monster.InstanceId))
            monster.RespawnAtUnixMs = 0;
    }

    private async Task SendCombatAsync(
        WorldPlayerEntity attacker, string targetId, int hp, int maxHp, string skillId, int damage, bool died, CancellationToken ct)
    {
        var combat = new CombatSync
        {
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = targetId,
            SkillId = skillId,
            Result = died ? CombatResultKind.CombatKill : CombatResultKind.CombatHit,
            Damage = damage,
            TargetHp = hp,
            TargetMaxHp = maxHp,
            TargetDied = died,
            MapId = attacker.MapId
        };
        await BroadcastNearAsync(attacker.MapId, attacker.X, attacker.Y, MsgId.CombatSync, combat, ct);
        await attacker.Session.SendAsync(MsgId.CombatSync, 0, combat, ct);
    }

    private async Task<bool> TryHandleOpenPvpAsync(
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
        if (!aoi.TryGetEntity(req.TargetEntityId, out var target) || target is null ||
            target.EntityId == attacker.EntityId)
            return false;
        if (target.InSafeZone || aoi.Map.IsInSafeZone(target.X, target.Y) ||
            attacker.InSafeZone || aoi.Map.IsInSafeZone(attacker.X, attacker.Y))
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSafeZone,
                Message = "安全区内禁止攻击。"
            }, ct);
            return true;
        }
        bool sameParty = !string.IsNullOrEmpty(attacker.PartyId) && attacker.PartyId == target.PartyId;
        bool sameGuild = !string.IsNullOrEmpty(attacker.GuildId) && attacker.GuildId == target.GuildId;
        bool atWar = GuildsAtWar(attacker.GuildId, target.GuildId);
        if (!atWar && !PhaseMRules.AllowsPlayerTarget(attacker.AttackMode, sameParty, sameGuild, target.PkKills, req.Force))
            return false;
        if (target.IsDead || target.Hp <= 0)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidTarget,
                Message = "目标已死亡。"
            }, ct);
            return true;
        }
        if (string.Equals(skill.Kind, "charge", StringComparison.OrdinalIgnoreCase))
            await StepChargeAsync(attacker, target.X, target.Y, skill.ChargeSteps, ct);
        int dist = CombatMath.Chebyshev(attacker.X, attacker.Y, target.X, target.Y);
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
        attacker.SkillReadyAtUnixMs[skill.SkillId] = now + cooldownMs;
        int beforePk = target.PkKills;
        int damage = await ApplyIncomingDamageAsync(target, CombatMath.CalcDamage(attacker.Atk, target.Def, dmgMult), ct);
        bool died = target.Hp <= 0;
        if (died)
            target.IsDead = true;
        await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            RemainingMp = attacker.Mp,
            NextReadyUnixMs = now + cooldownMs
        }, ct);
        await SendCombatAsync(attacker, target.EntityId, target.Hp, target.MaxHp, skill.SkillId, damage, died, ct);
        await WearWeaponAsync(attacker, ct);
        await GrantSkillExpAsync(attacker, skill, skill.SkillExpPerCast, ct);
        if (died)
        {
            if (beforePk < PhaseMRules.RedNameKills)
                await AddPkKillAsync(attacker, ct);
            await ClearBuffsOnDeathAsync(target, ct);
            if (!string.IsNullOrEmpty(target.ActivePetId))
                await DespawnPetEntityAsync(target, "owner_death", persist: true, ct);
            await RevivePlayerAsync(target, aoi, ct);
        }
        else
        {
            await _storage.Characters.SavePositionAsync(
                target.CharacterId, target.MapId, target.X, target.Y, target.Hp, target.Mp, ct);
        }
        return true;
    }

    private async Task AddPkKillAsync(WorldPlayerEntity attacker, CancellationToken ct)
    {
        attacker.PkKills += 1;
        await PersistAppearanceAsync(attacker, ct);
        await BroadcastPkAsync(attacker, ct);
    }

    private async Task BroadcastPkAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        var notify = new PkSyncNotify
        {
            EntityId = player.EntityId,
            PkKills = player.PkKills,
            NameColor = PhaseMRules.NameColor(player.PkKills),
            AttackMode = player.AttackMode
        };
        await player.Session.SendAsync(MsgId.PkSyncNotify, 0, notify, ct);
        await BroadcastNearAsync(player.MapId, player.X, player.Y, MsgId.PkSyncNotify, notify, ct);
    }

    private async Task HandleSetAttackModeAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = SetAttackModeReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.SetAttackModeResp, frame.Seq, new SetAttackModeResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        string mode = PhaseMRules.NormalizeMode(req.Mode);
        if (mode.Length == 0)
        {
            await session.SendAsync(MsgId.SetAttackModeResp, frame.Seq, new SetAttackModeResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidSkill,
                Message = "未知攻击模式"
            }, ct);
            return;
        }
        player.AttackMode = mode;
        await PersistAppearanceAsync(player, ct);
        await session.SendAsync(MsgId.SetAttackModeResp, frame.Seq, new SetAttackModeResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            Mode = mode,
            NameColor = PhaseMRules.NameColor(player.PkKills),
            PkKills = player.PkKills
        }, ct);
        await BroadcastPkAsync(player, ct);
    }

    private async Task HandleIdentifyAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = IdentifyReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.IdentifyResp, frame.Seq, new IdentifyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        if (!TryGetNpc(req.NpcEntityId, out var npc) || !NearNpc(player, npc))
        {
            await session.SendAsync(MsgId.IdentifyResp, frame.Seq, new IdentifyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNpcTooFar,
                Message = "离鉴定师太远"
            }, ct);
            return;
        }
        var piece = player.Equipment.FirstOrDefault(e =>
            string.Equals(e.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase) && !e.Identified);
        if (piece == null)
        {
            await session.SendAsync(MsgId.IdentifyResp, frame.Seq, new IdentifyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "没有需要鉴定的装备",
                Gold = player.Gold
            }, ct);
            return;
        }
        const int cost = 15;
        if (player.Gold < cost)
        {
            await session.SendAsync(MsgId.IdentifyResp, frame.Seq, new IdentifyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                Message = "金币不足",
                Gold = player.Gold
            }, ct);
            return;
        }
        player.Gold -= cost;
        piece.Identified = true;
        EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
        await PersistEconomyAsync(player, ct);
        await PersistEquipmentAsync(player, ct);
        var resp = new IdentifyResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "鉴定完成",
            Gold = player.Gold
        };
        foreach (var eq in EquipmentHelper.ToProto(player.Equipment, _versionPack.Items))
            resp.Equipment.Add(eq);
        await session.SendAsync(MsgId.IdentifyResp, frame.Seq, resp, ct);
    }

    private async Task RecallToTownAsync(
        IClientSession session, WorldPlayerEntity player, GridAoiManager fromAoi, string? mapId, CancellationToken ct)
    {
        string homeId = string.IsNullOrWhiteSpace(mapId) ? _versionPack.Map.MapId : mapId;
        var home = _versionPack.GetMap(homeId);
        var portal = new PortalConfig
        {
            PortalId = "recall",
            TargetMapId = home.MapId,
            TargetX = home.SpawnX,
            TargetY = home.SpawnY
        };
        await TransferViaPortalAsync(session, player, fromAoi, portal, ct);
    }

    private async Task HandleTradeStartAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = TradeStartReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.TradeStartResp, frame.Seq, FailTradeStart("Not in world"), ct);
            return;
        }
        var target = FindOnlineByName(req.TargetName ?? "");
        if (target is null || target.CharacterId == player.CharacterId)
        {
            await session.SendAsync(MsgId.TradeStartResp, frame.Seq, FailTradeStart("对方不在线"), ct);
            return;
        }
        if (!string.Equals(player.MapId, target.MapId, StringComparison.OrdinalIgnoreCase) ||
            CombatMath.Chebyshev(player.X, player.Y, target.X, target.Y) > 8)
        {
            await session.SendAsync(MsgId.TradeStartResp, frame.Seq, FailTradeStart("离对方太远"), ct);
            return;
        }
        if (_charToTrade.ContainsKey(player.CharacterId) || _charToTrade.ContainsKey(target.CharacterId))
        {
            await session.SendAsync(MsgId.TradeStartResp, frame.Seq, FailTradeStart("已在交易中"), ct);
            return;
        }
        var trade = new FaceTrade
        {
            Id = "trade_" + Guid.NewGuid().ToString("N")[..8],
            AId = player.CharacterId,
            BId = target.CharacterId
        };
        _faceTrades[trade.Id] = trade;
        _charToTrade[player.CharacterId] = trade.Id;
        _charToTrade[target.CharacterId] = trade.Id;
        await session.SendAsync(MsgId.TradeStartResp, frame.Seq, new TradeStartResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            TradeId = trade.Id
        }, ct);
        await target.Session.SendAsync(MsgId.TradeInviteNotify, 0, new TradeInviteNotify
        {
            TradeId = trade.Id,
            FromName = player.Name
        }, ct);
    }

    private async Task HandleTradeRespondAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = TradeRespondReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null ||
            !_faceTrades.TryGetValue(req.TradeId ?? "", out var trade) || trade.BId != player.CharacterId)
        {
            await session.SendAsync(MsgId.TradeRespondResp, frame.Seq, new TradeRespondResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "没有这笔交易"
            }, ct);
            return;
        }
        if (!req.Accept)
        {
            await CloseTradeAsync(trade, "对方拒绝了交易", ct);
            await session.SendAsync(MsgId.TradeRespondResp, frame.Seq, new TradeRespondResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "已拒绝",
                TradeId = trade.Id
            }, ct);
            return;
        }
        trade.Accepted = true;
        await session.SendAsync(MsgId.TradeRespondResp, frame.Seq, new TradeRespondResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            TradeId = trade.Id
        }, ct);
        await PushTradeStateAsync(trade, ct);
    }

    private async Task HandleTradePutAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = TradePutReq.Parser.ParseFrom(frame.Payload);
        if (!TryTradeSide(session, req.TradeId, out var player, out var trade, out bool sideA) || player is null || trade is null || !trade.Accepted)
        {
            await session.SendAsync(MsgId.TradePutResp, frame.Seq, new TradePutResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "交易还没开始"
            }, ct);
            return;
        }
        int count = Math.Max(0, req.Count);
        int gold = Math.Max(0, req.Gold);
        var offer = sideA ? trade.AItems : trade.BItems;
        if (!string.IsNullOrWhiteSpace(req.ItemId))
        {
            if (count <= 0)
                offer.RemoveAll(s => string.Equals(s.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase));
            else
            {
                var stack = player.Inventory.FirstOrDefault(s => string.Equals(s.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase));
                if (stack == null || stack.Count < count)
                {
                    await session.SendAsync(MsgId.TradePutResp, frame.Seq, new TradePutResp
                    {
                        Success = false,
                        ErrorCode = ProtocolErrorCode.ErrShopDenied,
                        Message = "物品不足"
                    }, ct);
                    return;
                }
                if (offer.Count >= 6 && offer.All(s => !string.Equals(s.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase)))
                {
                    await session.SendAsync(MsgId.TradePutResp, frame.Seq, new TradePutResp
                    {
                        Success = false,
                        ErrorCode = ProtocolErrorCode.ErrShopDenied,
                        Message = "交易格已满"
                    }, ct);
                    return;
                }
                offer.RemoveAll(s => string.Equals(s.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase));
                offer.Add(new TradeStack { ItemId = req.ItemId, Count = count });
            }
        }
        if (gold > player.Gold)
        {
            await session.SendAsync(MsgId.TradePutResp, frame.Seq, new TradePutResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                Message = "金币不足"
            }, ct);
            return;
        }
        if (sideA)
        {
            trade.AGold = gold;
            trade.ALock = false;
            trade.BLock = false;
        }
        else
        {
            trade.BGold = gold;
            trade.ALock = false;
            trade.BLock = false;
        }
        await session.SendAsync(MsgId.TradePutResp, frame.Seq, new TradePutResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        }, ct);
        await PushTradeStateAsync(trade, ct);
    }

    private async Task HandleTradeLockAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = TradeLockReq.Parser.ParseFrom(frame.Payload);
        if (!TryTradeSide(session, req.TradeId, out var player, out var trade, out bool sideA) || player is null || trade is null || !trade.Accepted)
        {
            await session.SendAsync(MsgId.TradeLockResp, frame.Seq, new TradeLockResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "交易还没开始"
            }, ct);
            return;
        }
        if (sideA) trade.ALock = req.Locked;
        else trade.BLock = req.Locked;
        await session.SendAsync(MsgId.TradeLockResp, frame.Seq, new TradeLockResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        }, ct);
        if (trade.ALock && trade.BLock)
            await CompleteTradeAsync(trade, ct);
        else
            await PushTradeStateAsync(trade, ct);
    }

    private async Task HandleTradeCancelAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = TradeCancelReq.Parser.ParseFrom(frame.Payload);
        if (_faceTrades.TryGetValue(req.TradeId ?? "", out var trade))
            await CloseTradeAsync(trade, "交易取消", ct);
        await session.SendAsync(MsgId.TradeCancelResp, frame.Seq, new TradeCancelResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        }, ct);
    }

    private bool TryTradeSide(IClientSession session, string? tradeId, out WorldPlayerEntity? player, out FaceTrade? trade, out bool sideA)
    {
        player = null;
        trade = null;
        sideA = false;
        if (!TryGetPlayer(session, out player, out _) || player is null)
            return false;
        if (!_faceTrades.TryGetValue(tradeId ?? "", out trade))
            return false;
        if (trade.AId == player.CharacterId)
        {
            sideA = true;
            return true;
        }
        if (trade.BId == player.CharacterId)
            return true;
        trade = null;
        return false;
    }

    private async Task PushTradeStateAsync(FaceTrade trade, CancellationToken ct)
    {
        var a = FindOnlineByCharacterId(trade.AId);
        var b = FindOnlineByCharacterId(trade.BId);
        if (a != null && b != null)
        {
            await a.Session.SendAsync(MsgId.TradeStateNotify, 0, View(trade, a, b, sideA: true), ct);
            await b.Session.SendAsync(MsgId.TradeStateNotify, 0, View(trade, b, a, sideA: false), ct);
        }
    }

    private static TradeStateNotify View(FaceTrade trade, WorldPlayerEntity self, WorldPlayerEntity peer, bool sideA)
    {
        var mine = sideA ? trade.AItems : trade.BItems;
        var theirs = sideA ? trade.BItems : trade.AItems;
        var view = new TradeStateNotify
        {
            TradeId = trade.Id,
            SelfItemId = mine.FirstOrDefault()?.ItemId ?? "",
            SelfCount = mine.FirstOrDefault()?.Count ?? 0,
            SelfGold = sideA ? trade.AGold : trade.BGold,
            SelfLocked = sideA ? trade.ALock : trade.BLock,
            PeerName = peer.Name,
            PeerItemId = theirs.FirstOrDefault()?.ItemId ?? "",
            PeerCount = theirs.FirstOrDefault()?.Count ?? 0,
            PeerGold = sideA ? trade.BGold : trade.AGold,
            PeerLocked = sideA ? trade.BLock : trade.ALock
        };
        view.SelfItems.AddRange(mine);
        view.PeerItems.AddRange(theirs);
        return view;
    }

    private async Task CompleteTradeAsync(FaceTrade trade, CancellationToken ct)
    {
        var a = FindOnlineByCharacterId(trade.AId);
        var b = FindOnlineByCharacterId(trade.BId);
        if (a is null || b is null)
        {
            await CloseTradeAsync(trade, "对方已离线", ct);
            return;
        }
        if (!TakeOffers(a, trade.AItems, trade.AGold) || !TakeOffers(b, trade.BItems, trade.BGold))
        {
            await CloseTradeAsync(trade, "物品或金币不足，交易失败", ct);
            return;
        }
        foreach (var stack in trade.BItems)
            InventoryHelper.AddItem(a.Inventory, stack.ItemId, stack.Count);
        foreach (var stack in trade.AItems)
            InventoryHelper.AddItem(b.Inventory, stack.ItemId, stack.Count);
        a.Gold += trade.BGold;
        b.Gold += trade.AGold;
        await PersistEconomyAsync(a, ct);
        await PersistEconomyAsync(b, ct);
        await SendTradeResultAsync(a, "交易完成", ct);
        await SendTradeResultAsync(b, "交易完成", ct);
        ForgetTrade(trade);
    }

    private static bool TakeOffers(WorldPlayerEntity player, IReadOnlyList<TradeStack> items, int gold)
    {
        if (gold > player.Gold)
            return false;
        foreach (var stack in items)
        {
            var have = player.Inventory.FirstOrDefault(s => string.Equals(s.ItemId, stack.ItemId, StringComparison.OrdinalIgnoreCase));
            if (have == null || have.Count < stack.Count)
                return false;
        }
        foreach (var stack in items)
        {
            if (!InventoryHelper.TryRemoveItem(player.Inventory, stack.ItemId, stack.Count))
                return false;
        }
        player.Gold -= gold;
        return true;
    }

    private async Task SendTradeResultAsync(WorldPlayerEntity player, string message, CancellationToken ct)
    {
        var notify = new TradeResultNotify
        {
            Success = true,
            Message = message,
            Gold = player.Gold
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            notify.Inventory.Add(slot);
        await player.Session.SendAsync(MsgId.TradeResultNotify, 0, notify, ct);
    }

    private async Task CloseTradeAsync(FaceTrade trade, string message, CancellationToken ct)
    {
        var a = FindOnlineByCharacterId(trade.AId);
        var b = FindOnlineByCharacterId(trade.BId);
        if (a != null)
            await a.Session.SendAsync(MsgId.TradeResultNotify, 0, new TradeResultNotify { Success = false, Message = message, Gold = a.Gold }, ct);
        if (b != null)
            await b.Session.SendAsync(MsgId.TradeResultNotify, 0, new TradeResultNotify { Success = false, Message = message, Gold = b.Gold }, ct);
        ForgetTrade(trade);
    }

    private void ForgetTrade(FaceTrade trade)
    {
        _faceTrades.TryRemove(trade.Id, out _);
        _charToTrade.TryRemove(trade.AId, out _);
        _charToTrade.TryRemove(trade.BId, out _);
    }

    private static TradeStartResp FailTradeStart(string message) => new()
    {
        Success = false,
        ErrorCode = ProtocolErrorCode.ErrShopDenied,
        Message = message
    };
}
