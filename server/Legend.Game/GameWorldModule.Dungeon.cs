using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private sealed class DungeonInstanceState
    {
        public required string InstanceId { get; init; }
        public required string DungeonId { get; init; }
        public required DungeonDefinition Def { get; init; }
        public required string MapId { get; init; }
        public required HashSet<string> MonsterEntityIds { get; init; }
        public int TargetKills { get; init; }
        public int Killed { get; set; }
        public string? OwnerCharacterId { get; set; }
        public HashSet<string> MemberCharacterIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RewardedCharacterIds { get; } = new(StringComparer.Ordinal);
        public bool Completed { get; set; }
    }

    private readonly ConcurrentDictionary<string, MapConfig> _instanceMapConfigs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DungeonInstanceState> _dungeonInstances = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _characterToDungeonInstance = new(StringComparer.OrdinalIgnoreCase);

    private DungeonsConfigFile DungeonCfg => _versionPack.Dungeons;

    private bool TryGetDungeon(string dungeonId, out DungeonDefinition def)
    {
        def = DungeonCfg.Dungeons.FirstOrDefault(d =>
            string.Equals(d.DungeonId, dungeonId, StringComparison.OrdinalIgnoreCase))!;
        return def is not null;
    }

    private static bool IsInstanceMapId(string mapId)
        => mapId.StartsWith("inst_", StringComparison.OrdinalIgnoreCase);

    private MapConfig ResolveMapConfig(string mapId)
    {
        if (_instanceMapConfigs.TryGetValue(mapId, out var inst))
            return inst;
        if (_versionPack.Maps.ContainsKey(mapId))
            return _versionPack.GetMap(mapId);
        return _versionPack.Map;
    }

    private static MapConfig CloneMapForInstance(MapConfig template, string instanceMapId)
    {
        return new MapConfig
        {
            MapId = instanceMapId,
            MapName = template.MapName,
            Width = template.Width,
            Height = template.Height,
            AoiCellSize = template.AoiCellSize,
            AoiRadiusCells = template.AoiRadiusCells,
            SpawnX = template.SpawnX,
            SpawnY = template.SpawnY,
            BlockedCells = template.BlockedCells,
            BlockedBitsetBase64 = template.BlockedBitsetBase64,
            SafeZones = template.SafeZones,
            Portals = new List<PortalConfig>(),
            MapFlags = template.MapFlags
        };
    }

    private async Task TransferPlayerToMapAsync(
        IClientSession session,
        WorldPlayerEntity entity,
        string toMapId,
        int tx,
        int ty,
        string reason,
        CancellationToken ct)
    {
        if (!_maps.TryGetValue(entity.MapId, out var fromAoi))
            fromAoi = GetAoi(entity.MapId);

        string fromMapId = entity.MapId;
        var leaveViewers = fromAoi.RemoveEntity(entity.EntityId);
        var leaveMsg = new EntityLeaveAoi { EntityId = entity.EntityId, Reason = reason };
        foreach (var v in leaveViewers)
            await v.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leaveMsg, ct);

        var toMap = ResolveMapConfig(toMapId);
        if (toMap.IsBlocked(tx, ty))
        {
            tx = toMap.SpawnX;
            ty = toMap.SpawnY;
        }

        entity.MapId = toMap.MapId;
        entity.X = tx;
        entity.Y = ty;
        SyncHeroFollow(entity);

        var toAoi = GetAoi(toMap.MapId);
        var nearby = toAoi.AddOrUpdateEntity(entity);

        await _storage.Characters.SavePositionAsync(
            entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);

        var notify = new MapChangeNotify
        {
            EntityId = entity.EntityId,
            FromMapId = fromMapId,
            ToMapId = toMap.MapId,
            ToX = tx,
            ToY = ty,
            PortalId = reason,
            InSafeZone = entity.InSafeZone,
            MapNoHang = toMap.IsNoHang,
            MapNoAutoPickup = toMap.IsNoAutoPickup
        };
        foreach (var n in nearby)
            notify.NearbyEntities.Add(n.ToSnapshot());
        foreach (var snap in EnumerateNearbyNonPlayers(toMap.MapId, tx, ty))
            notify.NearbyEntities.Add(snap);

        await session.SendAsync(MsgId.MapChangeNotify, 0, notify, ct);

        var enterNotice = new EntityEnterAoi { Entity = entity.ToSnapshot() };
        foreach (var viewer in nearby)
            await viewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, enterNotice, ct);
    }

    private (ProtocolErrorCode Code, string Message, int RemainingDaily, int DailyLimit, long NextEnterUnix) EvaluateDungeonGates(
        WorldPlayerEntity player, DungeonDefinition def, DateTimeOffset now)
    {
        int dailyLimit = def.DailyLimit;
        var entry = DungeonProgressJson.GetOrCreate(player.DungeonProgress, def.DungeonId, def.ResetHourLocal, now);
        int remaining = dailyLimit <= 0 ? -1 : Math.Max(0, dailyLimit - entry.UsedToday);
        long nextUnix = 0;

        if (player.Level < def.MinLevel)
            return (ProtocolErrorCode.ErrDungeonLevel, $"等级不足，需要 Lv.{def.MinLevel}", remaining, dailyLimit, 0);

        if (def.EntryCostGold > 0 && player.Gold < def.EntryCostGold)
            return (ProtocolErrorCode.ErrDungeonCost, $"金币不足，进入需 {def.EntryCostGold} 金", remaining, dailyLimit, 0);

        if (dailyLimit > 0 && entry.UsedToday >= dailyLimit)
        {
            return (ProtocolErrorCode.ErrDungeonDailyLimit,
                $"今日次数已用尽 ({entry.UsedToday}/{dailyLimit})", 0, dailyLimit, 0);
        }

        if (def.CooldownSeconds > 0 && entry.LastEnterUnix > 0)
        {
            long readyAt = entry.LastEnterUnix + def.CooldownSeconds;
            long nowUnix = now.ToUnixTimeSeconds();
            if (nowUnix < readyAt)
            {
                nextUnix = readyAt;
                return (ProtocolErrorCode.ErrDungeonCooldown,
                    $"冷却中，还需 {readyAt - nowUnix} 秒", remaining, dailyLimit, nextUnix);
            }
        }

        return (ProtocolErrorCode.ErrNone, "OK", remaining, dailyLimit, 0);
    }

    private async Task PersistDungeonProgressAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        string json = DungeonProgressJson.Serialize(player.DungeonProgress);
        await _storage.Characters.SaveDungeonProgressAsync(player.CharacterId, json, ct);
    }

    private async Task CommitDungeonEnterCostAndQuotaAsync(
        WorldPlayerEntity player, DungeonDefinition def, DateTimeOffset now, CancellationToken ct)
    {
        if (def.EntryCostGold > 0)
        {
            AdjustGold(player, -(def.EntryCostGold));
            await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);
        }

        var entry = DungeonProgressJson.GetOrCreate(player.DungeonProgress, def.DungeonId, def.ResetHourLocal, now);
        entry.UsedToday++;
        entry.LastEnterUnix = now.ToUnixTimeSeconds();
        entry.DayKey = DungeonProgressJson.ComputeDayKey(now, def.ResetHourLocal);
        await PersistDungeonProgressAsync(player, ct);
    }

    private (string InstanceId, string MapId, HashSet<string> MonsterIds, int TargetKills)? TryCreateDungeonInstance(
        DungeonDefinition def, string ownerCharacterId)
    {
        string instanceId = Guid.NewGuid().ToString("N");
        string instanceMapId = $"inst_{instanceId}";
        var template = _versionPack.GetMap(def.TemplateMapId);
        var instMap = CloneMapForInstance(template, instanceMapId);
        _instanceMapConfigs[instanceMapId] = instMap;
        _maps[instanceMapId] = new GridAoiManager(instMap);

        var monsterIds = new HashSet<string>(StringComparer.Ordinal);
        int targetKills = 0;
        foreach (var spawn in def.Spawns)
        {
            if (!_versionPack.Monsters.TryGetValue(spawn.MonsterId, out var tmpl))
                continue;
            int count = Math.Max(1, spawn.Count);
            for (int i = 0; i < count; i++)
            {
                int x = spawn.X + (i % 3);
                int y = spawn.Y + (i / 3);
                if (instMap.IsBlocked(x, y) || instMap.IsInSafeZone(x, y))
                {
                    x = spawn.X;
                    y = spawn.Y;
                }
                var m = CreateMonster(tmpl, instanceMapId, x, y);
                m.InstanceId = instanceId;
                m.NoRespawn = true;
                m.RespawnAtUnixMs = 0;
                monsterIds.Add(m.EntityId);
                targetKills++;
            }
        }

        if (targetKills <= 0)
        {
            _maps.TryRemove(instanceMapId, out _);
            _instanceMapConfigs.TryRemove(instanceMapId, out _);
            return null;
        }

        var state = new DungeonInstanceState
        {
            InstanceId = instanceId,
            DungeonId = def.DungeonId,
            Def = def,
            MapId = instanceMapId,
            MonsterEntityIds = monsterIds,
            TargetKills = targetKills,
            OwnerCharacterId = ownerCharacterId
        };
        _dungeonInstances[instanceId] = state;
        return (instanceId, instanceMapId, monsterIds, targetKills);
    }

    private async Task PlacePlayerIntoDungeonAsync(
        IClientSession session,
        WorldPlayerEntity player,
        DungeonInstanceState state,
        CancellationToken ct)
    {
        var def = state.Def;
        int ex = def.EnterX != 0 || def.EnterY != 0 ? def.EnterX : ResolveMapConfig(state.MapId).SpawnX;
        int ey = def.EnterX != 0 || def.EnterY != 0 ? def.EnterY : ResolveMapConfig(state.MapId).SpawnY;
        await TransferPlayerToMapAsync(session, player, state.MapId, ex, ey, "DungeonEnter", ct);

        player.DungeonId = def.DungeonId;
        player.DungeonInstanceId = state.InstanceId;
        player.DungeonCompleted = false;
        state.MemberCharacterIds.Add(player.CharacterId);
        _characterToDungeonInstance[player.CharacterId] = state.InstanceId;
    }

    private async Task HandleDungeonEnterAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = DungeonEnterReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (!string.IsNullOrEmpty(player.DungeonInstanceId))
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = "已在副本中，请先离开"
            }, ct);
            return;
        }

        if (!TryGetDungeon(req.DungeonId, out var def))
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = $"未知副本: {req.DungeonId}"
            }, ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        PartyRuntime? partyForEnter = null;
        if (req.PartyEnter &&
            _charToParty.TryGetValue(player.CharacterId, out string? partyId) &&
            !string.IsNullOrEmpty(partyId) &&
            _parties.TryGetValue(partyId, out var foundParty) &&
            foundParty is not null)
        {
            partyForEnter = foundParty;
        }

        if (partyForEnter is not null)
        {
            await HandlePartyDungeonEnterAsync(session, frame, player, def, partyForEnter, now, ct);
            return;
        }

        var (code, msg, remaining, dailyLimit, _) = EvaluateDungeonGates(player, def, now);
        if (code != ProtocolErrorCode.ErrNone)
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = code,
                Message = msg,
                DungeonId = def.DungeonId,
                RemainingDaily = remaining < 0 ? 0 : remaining,
                DailyLimit = dailyLimit
            }, ct);
            return;
        }

        var created = TryCreateDungeonInstance(def, player.CharacterId);
        if (created is null)
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = "副本刷怪配置无效"
            }, ct);
            return;
        }

        var state = _dungeonInstances[created.Value.InstanceId];
        await CommitDungeonEnterCostAndQuotaAsync(player, def, now, ct);
        await PlacePlayerIntoDungeonAsync(session, player, state, ct);

        int remAfter = dailyLimit <= 0 ? -1 : Math.Max(0, remaining - 1);
        await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            DungeonId = def.DungeonId,
            InstanceId = state.InstanceId,
            MapId = state.MapId,
            X = player.X,
            Y = player.Y,
            RemainingKills = state.TargetKills,
            TargetKills = state.TargetKills,
            Gold = player.Gold,
            RemainingDaily = remAfter < 0 ? 0 : remAfter,
            DailyLimit = dailyLimit
        }, ct);

        await session.SendAsync(MsgId.DungeonProgressNotify, 0, new DungeonProgressNotify
        {
            DungeonId = def.DungeonId,
            InstanceId = state.InstanceId,
            RemainingKills = state.TargetKills,
            TargetKills = state.TargetKills,
            Killed = 0
        }, ct);

        await _eventBus.PublishAsync(new DungeonEnteredEvent(
            session, player.Name, player.CharacterId, def.DungeonId, state.InstanceId), ct);

        StructuredLogger.Info("GameWorldModule",
            $"Dungeon enter {player.Name} -> {def.DungeonId} inst={state.InstanceId} kills={state.TargetKills}",
            connId: session.ConnectionId, playerId: player.EntityId);
    }

    private async Task HandlePartyDungeonEnterAsync(
        IClientSession session,
        PacketFrame frame,
        WorldPlayerEntity leader,
        DungeonDefinition def,
        PartyRuntime party,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!string.Equals(party.LeaderCharacterId, leader.CharacterId, StringComparison.Ordinal))
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = "仅队长可发起组队进本",
                DungeonId = def.DungeonId
            }, ct);
            return;
        }

        // Candidates: leader + online members on same map, not already in a dungeon
        var candidates = new List<WorldPlayerEntity>();
        foreach (var memberId in party.MemberCharacterIds.ToList())
        {
            if (!TryFindOnlinePlayerByCharacterId(memberId, out var m) || m is null)
                continue;
            if (!string.IsNullOrEmpty(m.DungeonInstanceId))
                continue;
            if (!string.Equals(m.MapId, leader.MapId, StringComparison.OrdinalIgnoreCase))
                continue;
            candidates.Add(m);
        }
        if (!candidates.Any(c => c.CharacterId == leader.CharacterId))
            candidates.Insert(0, leader);

        var results = new List<DungeonPartyMemberResult>();
        var eligible = new List<WorldPlayerEntity>();
        foreach (var c in candidates.GroupBy(x => x.CharacterId).Select(g => g.First()))
        {
            var (code, msg, _, _, _) = EvaluateDungeonGates(c, def, now);
            bool ok = code == ProtocolErrorCode.ErrNone;
            results.Add(new DungeonPartyMemberResult
            {
                CharacterId = c.CharacterId,
                Name = c.Name,
                Entered = ok,
                ErrorCode = code,
                Message = msg
            });
            if (ok) eligible.Add(c);
        }

        // Also note offline / off-map members as denied
        foreach (var memberId in party.MemberCharacterIds)
        {
            if (results.Any(r => r.CharacterId == memberId))
                continue;
            string name = memberId;
            if (TryFindOnlinePlayerByCharacterId(memberId, out var offlineish) && offlineish is not null)
                name = offlineish.Name;
            else
            {
                var rec = await _storage.Characters.GetByIdAsync(memberId, ct);
                if (rec is not null) name = rec.Name;
            }
            results.Add(new DungeonPartyMemberResult
            {
                CharacterId = memberId,
                Name = name,
                Entered = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = "不在线或不同图，无法组队进本"
            });
        }

        if (eligible.Count == 0 || !eligible.Any(e => e.CharacterId == leader.CharacterId))
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = "队长不满足进本条件或无合格队员",
                DungeonId = def.DungeonId
            }, ct);

            var denyNotify = new DungeonPartyEnterNotify { DungeonId = def.DungeonId };
            foreach (var r in results) denyNotify.Members.Add(r);
            await session.SendAsync(MsgId.DungeonPartyEnterNotify, 0, denyNotify, ct);
            return;
        }

        var created = TryCreateDungeonInstance(def, leader.CharacterId);
        if (created is null)
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = "副本刷怪配置无效"
            }, ct);
            return;
        }

        var state = _dungeonInstances[created.Value.InstanceId];
        int entered = 0;
        int denied = results.Count(r => !r.Entered);

        foreach (var member in eligible)
        {
            await CommitDungeonEnterCostAndQuotaAsync(member, def, now, ct);
            await PlacePlayerIntoDungeonAsync(member.Session, member, state, ct);
            entered++;

            var entry = DungeonProgressJson.GetOrCreate(member.DungeonProgress, def.DungeonId, def.ResetHourLocal, now);
            int rem = def.DailyLimit <= 0 ? 0 : Math.Max(0, def.DailyLimit - entry.UsedToday);

            await member.Session.SendAsync(MsgId.DungeonEnterResp,
                member.CharacterId == leader.CharacterId ? frame.Seq : 0,
                new DungeonEnterResp
                {
                    Success = true,
                    ErrorCode = ProtocolErrorCode.ErrNone,
                    Message = "OK",
                    DungeonId = def.DungeonId,
                    InstanceId = state.InstanceId,
                    MapId = state.MapId,
                    X = member.X,
                    Y = member.Y,
                    RemainingKills = state.TargetKills,
                    TargetKills = state.TargetKills,
                    Gold = member.Gold,
                    RemainingDaily = rem,
                    DailyLimit = def.DailyLimit
                }, ct);

            await member.Session.SendAsync(MsgId.DungeonProgressNotify, 0, new DungeonProgressNotify
            {
                DungeonId = def.DungeonId,
                InstanceId = state.InstanceId,
                RemainingKills = state.TargetKills,
                TargetKills = state.TargetKills,
                Killed = 0
            }, ct);

            await _eventBus.PublishAsync(new DungeonEnteredEvent(
                member.Session, member.Name, member.CharacterId, def.DungeonId, state.InstanceId), ct);
        }

        // Mark result Entered accurately for eligible
        foreach (var r in results)
        {
            if (eligible.Any(e => e.CharacterId == r.CharacterId))
            {
                r.Entered = true;
                r.ErrorCode = ProtocolErrorCode.ErrNone;
                r.Message = "OK";
            }
        }

        var notify = new DungeonPartyEnterNotify
        {
            DungeonId = def.DungeonId,
            InstanceId = state.InstanceId
        };
        foreach (var r in results) notify.Members.Add(r);

        foreach (var memberId in party.MemberCharacterIds)
        {
            if (TryFindOnlinePlayerByCharacterId(memberId, out var p) && p is not null)
                await p.Session.SendAsync(MsgId.DungeonPartyEnterNotify, 0, notify, ct);
        }

        await _eventBus.PublishAsync(new DungeonPartyEnteredEvent(
            session, leader.Name, leader.CharacterId, def.DungeonId, state.InstanceId, entered, denied), ct);

        StructuredLogger.Info("GameWorldModule",
            $"Dungeon party enter leader={leader.Name} dungeon={def.DungeonId} inst={state.InstanceId} entered={entered} denied={denied}",
            connId: session.ConnectionId, playerId: leader.EntityId);
    }

    private async Task HandleDungeonStatusAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = DungeonStatusReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.DungeonStatusResp, frame.Seq, new DungeonStatusResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (!TryGetDungeon(req.DungeonId, out var def))
        {
            await session.SendAsync(MsgId.DungeonStatusResp, frame.Seq, new DungeonStatusResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = $"未知副本: {req.DungeonId}"
            }, ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var entry = DungeonProgressJson.GetOrCreate(player.DungeonProgress, def.DungeonId, def.ResetHourLocal, now);
        int used = entry.UsedToday;
        int remaining = def.DailyLimit <= 0 ? 999 : Math.Max(0, def.DailyLimit - used);
        long nextUnix = 0;
        bool canEnter = true;
        if (def.DailyLimit > 0 && used >= def.DailyLimit)
            canEnter = false;
        if (def.CooldownSeconds > 0 && entry.LastEnterUnix > 0)
        {
            long ready = entry.LastEnterUnix + def.CooldownSeconds;
            if (now.ToUnixTimeSeconds() < ready)
            {
                nextUnix = ready;
                canEnter = false;
            }
        }
        if (player.Level < def.MinLevel) canEnter = false;

        await session.SendAsync(MsgId.DungeonStatusResp, frame.Seq, new DungeonStatusResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            DungeonId = def.DungeonId,
            DailyLimit = def.DailyLimit,
            UsedToday = used,
            RemainingDaily = remaining,
            CooldownSeconds = def.CooldownSeconds,
            NextEnterUnix = nextUnix,
            CanEnter = canEnter,
            MinLevel = def.MinLevel,
            EntryCostGold = def.EntryCostGold
        }, ct);
    }

    private async Task HandleDungeonLeaveAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.DungeonLeaveResp, frame.Seq, new DungeonLeaveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.DungeonInstanceId) ||
            !_dungeonInstances.TryGetValue(player.DungeonInstanceId, out var state))
        {
            await session.SendAsync(MsgId.DungeonLeaveResp, frame.Seq, new DungeonLeaveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonNotIn,
                Message = "当前不在副本中"
            }, ct);
            return;
        }

        bool completed = state.Completed;
        await ExitDungeonAsync(session, player, state, reason: "DungeonLeave", ct);

        await session.SendAsync(MsgId.DungeonLeaveResp, frame.Seq, new DungeonLeaveResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = completed ? "已完成并离开" : "未完成离开，无清怪奖励",
            MapId = player.MapId,
            X = player.X,
            Y = player.Y
        }, ct);
    }

    private async Task OnDungeonMonsterKilledAsync(WorldPlayerEntity killer, MonsterEntity monster, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(monster.InstanceId))
            return;
        if (!_dungeonInstances.TryGetValue(monster.InstanceId, out var state) || state.Completed)
            return;
        if (!state.MonsterEntityIds.Contains(monster.EntityId))
            return;

        state.Killed++;
        int remaining = Math.Max(0, state.TargetKills - state.Killed);

        var progress = new DungeonProgressNotify
        {
            DungeonId = state.DungeonId,
            InstanceId = state.InstanceId,
            RemainingKills = remaining,
            TargetKills = state.TargetKills,
            Killed = state.Killed
        };

        foreach (var mid in state.MemberCharacterIds.ToList())
        {
            if (TryFindOnlinePlayerByCharacterId(mid, out var member) &&
                member is not null &&
                string.Equals(member.DungeonInstanceId, state.InstanceId, StringComparison.Ordinal))
            {
                await member.Session.SendAsync(MsgId.DungeonProgressNotify, 0, progress, ct);
            }
        }

        if (remaining > 0)
            return;

        state.Completed = true;
        await CompleteDungeonForAllMembersAsync(state, ct);
    }

    private async Task CompleteDungeonForAllMembersAsync(DungeonInstanceState state, CancellationToken ct)
    {
        var members = state.MemberCharacterIds.ToList();
        foreach (var mid in members)
        {
            if (!TryFindOnlinePlayerByCharacterId(mid, out var member) || member is null)
                continue;
            if (!string.Equals(member.DungeonInstanceId, state.InstanceId, StringComparison.Ordinal))
                continue;
            member.DungeonCompleted = true;
            await RewardDungeonMemberAsync(member.Session, member, state, ct);
            await ExitDungeonAsync(member.Session, member, state, reason: "DungeonComplete", ct);
        }
    }

    private async Task RewardDungeonMemberAsync(
        IClientSession session, WorldPlayerEntity player, DungeonInstanceState state, CancellationToken ct)
    {
        if (!state.RewardedCharacterIds.Add(player.CharacterId))
            return;

        var def = state.Def;
        int rewardGold = Math.Max(0, def.RewardGold);
        var rewardItems = def.RewardItems ?? new List<DungeonRewardItemConfig>();
        bool mailed = false;

        AdjustGold(player, rewardGold);
        await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);

        int maxSlots = Math.Max(1, _versionPack.Mail.MaxInventorySlots);
        var pendingMailItems = new List<(string ItemId, int Count)>();
        foreach (var ri in rewardItems)
        {
            if (string.IsNullOrWhiteSpace(ri.ItemId) || ri.Count <= 0)
                continue;
            bool hasStack = player.Inventory.Any(s =>
                string.Equals(s.ItemId, ri.ItemId, StringComparison.OrdinalIgnoreCase));
            if (!hasStack && player.Inventory.Count >= maxSlots)
            {
                pendingMailItems.Add((ri.ItemId, ri.Count));
                continue;
            }
            InventoryHelper.AddItem(player.Inventory, ri.ItemId, ri.Count);
        }
        await _storage.Characters.SaveInventoryAsync(
            player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);

        if (pendingMailItems.Count > 0)
        {
            mailed = true;
            var mailItems = pendingMailItems.Select(i => new MailAttachmentItemRecord
            {
                ItemId = i.ItemId,
                Count = i.Count
            });
            await SendMailAsync(
                player.CharacterId,
                player.Name,
                _versionPack.Mail.SystemSenderName,
                $"{def.DisplayName} 奖励补发",
                "背包已满，副本奖励道具已通过邮件发放。",
                0,
                mailItems,
                ct);
        }

        var complete = new DungeonCompleteNotify
        {
            DungeonId = state.DungeonId,
            InstanceId = state.InstanceId,
            RewardGold = rewardGold,
            RewardsMailed = mailed,
            Gold = player.Gold,
            ExitMapId = def.ExitMapId,
            ExitX = def.ExitX,
            ExitY = def.ExitY
        };
        foreach (var ri in rewardItems)
        {
            if (!string.IsNullOrWhiteSpace(ri.ItemId) && ri.Count > 0)
                complete.RewardItems.Add(new InventoryItem { ItemId = ri.ItemId, Count = ri.Count });
        }
        await session.SendAsync(MsgId.DungeonCompleteNotify, 0, complete, ct);

        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "DungeonReward",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new DungeonCompletedEvent(
            session, player.Name, player.CharacterId, state.DungeonId, state.InstanceId, rewardGold), ct);
    }

    private async Task ExitDungeonAsync(
        IClientSession session,
        WorldPlayerEntity player,
        DungeonInstanceState state,
        string reason,
        CancellationToken ct)
    {
        bool completed = state.Completed;
        string dungeonId = state.DungeonId;
        string instanceId = state.InstanceId;
        var def = state.Def;

        player.DungeonId = null;
        player.DungeonInstanceId = null;
        player.DungeonCompleted = false;
        state.MemberCharacterIds.Remove(player.CharacterId);
        _characterToDungeonInstance.TryRemove(player.CharacterId, out _);

        await TransferPlayerToMapAsync(
            session, player, def.ExitMapId, def.ExitX, def.ExitY, reason, ct);

        if (state.MemberCharacterIds.Count == 0)
            await DestroyDungeonInstanceAsync(instanceId, ct);

        await _eventBus.PublishAsync(new DungeonLeftEvent(
            session, player.Name, player.CharacterId, dungeonId, instanceId, completed), ct);
    }

    private async Task DestroyDungeonInstanceAsync(string instanceId, CancellationToken ct)
    {
        if (!_dungeonInstances.TryRemove(instanceId, out var state))
            return;

        foreach (var mid in state.MonsterEntityIds)
            _monsters.TryRemove(mid, out _);

        foreach (var loot in _loots.Values.Where(l =>
                     string.Equals(l.MapId, state.MapId, StringComparison.OrdinalIgnoreCase)).ToList())
            _loots.TryRemove(loot.EntityId, out _);

        _maps.TryRemove(state.MapId, out _);
        _instanceMapConfigs.TryRemove(state.MapId, out _);
        await Task.CompletedTask;
    }

    private bool TryFindOnlinePlayerByCharacterId(string characterId, out WorldPlayerEntity? player)
    {
        player = EnumerateOnlinePlayers().FirstOrDefault(p =>
            string.Equals(p.CharacterId, characterId, StringComparison.Ordinal));
        return player is not null;
    }

    private void SanitizeCharacterMapIfStaleInstance(CharacterRecord character)
    {
        if (!IsInstanceMapId(character.MapId) && _versionPack.Maps.ContainsKey(character.MapId))
            return;

        var any = DungeonCfg.Dungeons.FirstOrDefault();
        if (any is not null)
        {
            character.MapId = any.ExitMapId;
            character.X = any.ExitX;
            character.Y = any.ExitY;
        }
        else
        {
            character.MapId = _versionPack.Map.MapId;
            character.X = _versionPack.Map.SpawnX;
            character.Y = _versionPack.Map.SpawnY;
        }
    }

    private async Task OnDungeonPlayerDisconnectedAsync(WorldPlayerEntity entity, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entity.DungeonInstanceId))
            return;
        if (!_dungeonInstances.TryGetValue(entity.DungeonInstanceId, out var state))
            return;

        entity.DungeonId = null;
        entity.DungeonInstanceId = null;
        entity.DungeonCompleted = false;
        state.MemberCharacterIds.Remove(entity.CharacterId);
        _characterToDungeonInstance.TryRemove(entity.CharacterId, out _);
        entity.MapId = state.Def.ExitMapId;
        entity.X = state.Def.ExitX;
        entity.Y = state.Def.ExitY;
        await _storage.Characters.SavePositionAsync(
            entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);

        if (state.MemberCharacterIds.Count == 0)
            await DestroyDungeonInstanceAsync(state.InstanceId, ct);
    }
}
