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
        public bool Completed { get; set; }
        public bool Rewarded { get; set; }
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
            Portals = new List<PortalConfig>() // leave via DungeonLeave only
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
            InSafeZone = entity.InSafeZone
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

        if (player.Level < def.MinLevel)
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonLevel,
                Message = $"等级不足，需要 Lv.{def.MinLevel}"
            }, ct);
            return;
        }

        if (def.EntryCostGold > 0 && player.Gold < def.EntryCostGold)
        {
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonCost,
                Message = $"金币不足，进入需 {def.EntryCostGold} 金"
            }, ct);
            return;
        }

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
            await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDungeonDenied,
                Message = "副本刷怪配置无效"
            }, ct);
            return;
        }

        if (def.EntryCostGold > 0)
        {
            player.Gold -= def.EntryCostGold;
            await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);
        }

        var state = new DungeonInstanceState
        {
            InstanceId = instanceId,
            DungeonId = def.DungeonId,
            Def = def,
            MapId = instanceMapId,
            MonsterEntityIds = monsterIds,
            TargetKills = targetKills,
            OwnerCharacterId = player.CharacterId
        };
        _dungeonInstances[instanceId] = state;
        _characterToDungeonInstance[player.CharacterId] = instanceId;

        int ex = def.EnterX != 0 || def.EnterY != 0 ? def.EnterX : instMap.SpawnX;
        int ey = def.EnterX != 0 || def.EnterY != 0 ? def.EnterY : instMap.SpawnY;
        await TransferPlayerToMapAsync(session, player, instanceMapId, ex, ey, "DungeonEnter", ct);

        player.DungeonId = def.DungeonId;
        player.DungeonInstanceId = instanceId;
        player.DungeonCompleted = false;

        await session.SendAsync(MsgId.DungeonEnterResp, frame.Seq, new DungeonEnterResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            DungeonId = def.DungeonId,
            InstanceId = instanceId,
            MapId = instanceMapId,
            X = player.X,
            Y = player.Y,
            RemainingKills = targetKills,
            TargetKills = targetKills,
            Gold = player.Gold
        }, ct);

        await session.SendAsync(MsgId.DungeonProgressNotify, 0, new DungeonProgressNotify
        {
            DungeonId = def.DungeonId,
            InstanceId = instanceId,
            RemainingKills = targetKills,
            TargetKills = targetKills,
            Killed = 0
        }, ct);

        await _eventBus.PublishAsync(new DungeonEnteredEvent(
            session, player.Name, player.CharacterId, def.DungeonId, instanceId), ct);

        StructuredLogger.Info("GameWorldModule",
            $"Dungeon enter {player.Name} -> {def.DungeonId} inst={instanceId} kills={targetKills}",
            connId: session.ConnectionId, playerId: player.EntityId);
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
        await ExitDungeonAsync(session, player, state, grantReward: false, reason: "DungeonLeave", ct);

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

        if (!string.IsNullOrEmpty(killer.DungeonInstanceId) &&
            string.Equals(killer.DungeonInstanceId, state.InstanceId, StringComparison.Ordinal))
        {
            await killer.Session.SendAsync(MsgId.DungeonProgressNotify, 0, new DungeonProgressNotify
            {
                DungeonId = state.DungeonId,
                InstanceId = state.InstanceId,
                RemainingKills = remaining,
                TargetKills = state.TargetKills,
                Killed = state.Killed
            }, ct);
        }

        if (remaining > 0)
            return;

        state.Completed = true;
        if (!string.IsNullOrEmpty(state.OwnerCharacterId) &&
            TryFindOnlinePlayerByCharacterId(state.OwnerCharacterId, out var owner) &&
            owner is not null)
        {
            owner.DungeonCompleted = true;
            await CompleteDungeonAndExitAsync(owner.Session, owner, state, ct);
        }
    }

    private async Task CompleteDungeonAndExitAsync(
        IClientSession session, WorldPlayerEntity player, DungeonInstanceState state, CancellationToken ct)
    {
        if (state.Rewarded)
            return;
        state.Rewarded = true;

        var def = state.Def;
        int rewardGold = Math.Max(0, def.RewardGold);
        var rewardItems = def.RewardItems ?? new List<DungeonRewardItemConfig>();
        bool mailed = false;

        player.Gold += rewardGold;
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

        await ExitDungeonAsync(session, player, state, grantReward: false, reason: "DungeonComplete", ct);
    }

    private async Task ExitDungeonAsync(
        IClientSession session,
        WorldPlayerEntity player,
        DungeonInstanceState state,
        bool grantReward,
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
        _characterToDungeonInstance.TryRemove(player.CharacterId, out _);

        await TransferPlayerToMapAsync(
            session, player, def.ExitMapId, def.ExitX, def.ExitY, reason, ct);

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

    /// <summary>If saved map is a lost instance, snap character back to town exit of that dungeon or default map.</summary>
    private void SanitizeCharacterMapIfStaleInstance(CharacterRecord character)
    {
        if (!IsInstanceMapId(character.MapId) && _versionPack.Maps.ContainsKey(character.MapId))
            return;

        // Prefer first dungeon exit if any, else default map spawn.
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

        // Persist safe town coords so Host restart won't trap player in lost instance.
        entity.DungeonId = null;
        entity.DungeonInstanceId = null;
        entity.DungeonCompleted = false;
        _characterToDungeonInstance.TryRemove(entity.CharacterId, out _);
        entity.MapId = state.Def.ExitMapId;
        entity.X = state.Def.ExitX;
        entity.Y = state.Def.ExitY;
        await _storage.Characters.SavePositionAsync(
            entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);
        await DestroyDungeonInstanceAsync(state.InstanceId, ct);
    }
}
