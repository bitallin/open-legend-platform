using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed class GameWorldModule : IGameModule
{
    private readonly IEventBus _eventBus;
    private readonly LoadedVersionPack _versionPack;
    private readonly ILegendStorage _storage;
    private readonly IAntiCheatProbe _antiCheatProbe;
    private readonly ConcurrentDictionary<string, GridAoiManager> _maps = new(StringComparer.OrdinalIgnoreCase);

    public GameWorldModule(
        IEventBus eventBus,
        LoadedVersionPack versionPack,
        ILegendStorage storage,
        IAntiCheatProbe? antiCheatProbe = null)
    {
        _eventBus = eventBus;
        _versionPack = versionPack;
        _storage = storage;
        _antiCheatProbe = antiCheatProbe ?? new DefaultAntiCheatProbe(maxStepDistance: 2);

        foreach (var map in versionPack.Maps.Values)
            _maps[map.MapId] = new GridAoiManager(map);
    }

    public string ModuleName => "GameWorldModule";
    public int OnlineCount => _maps.Values.Sum(m => m.OnlineCount);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public bool CanHandle(MsgId msgId) => msgId is MsgId.EnterWorldReq or MsgId.MoveReq;

    private GridAoiManager GetAoi(string mapId)
    {
        if (_maps.TryGetValue(mapId, out var aoi))
            return aoi;
        var map = _versionPack.GetMap(mapId);
        return _maps.GetOrAdd(map.MapId, _ => new GridAoiManager(map));
    }

    public async Task HandlePacketAsync(IClientSession session, PacketFrame frame, CancellationToken ct = default)
    {
        switch (frame.MsgId)
        {
            case MsgId.EnterWorldReq:
                await HandleEnterWorldAsync(session, frame, ct);
                break;
            case MsgId.MoveReq:
                await HandleMoveAsync(session, frame, ct);
                break;
        }
    }

    private async Task HandleEnterWorldAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = EnterWorldReq.Parser.ParseFrom(frame.Payload);
        var sess = await _storage.Sessions.GetSessionAsync(req.SessionToken, ct);
        if (sess is null)
        {
            await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, new EnterWorldResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Invalid or expired session token."
            }, ct);
            return;
        }

        string characterId = !string.IsNullOrWhiteSpace(req.CharacterId)
            ? req.CharacterId
            : sess.CharacterId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(characterId))
        {
            await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, new EnterWorldResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "未选择角色，请先 SelectChar。"
            }, ct);
            return;
        }

        var character = await _storage.Characters.GetByIdAsync(characterId, ct);
        if (character is null || character.AccountId != sess.AccountId)
        {
            await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, new EnterWorldResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "角色不存在或不属于当前账号。"
            }, ct);
            return;
        }

        // Remove from any previous map if reconnecting
        if (!string.IsNullOrEmpty(session.EntityId))
        {
            foreach (var aoi in _maps.Values)
            {
                if (aoi.TryGetEntity(session.EntityId, out _))
                {
                    var oldViewers = aoi.RemoveEntity(session.EntityId);
                    var leave = new EntityLeaveAoi { EntityId = session.EntityId, Reason = "Reenter" };
                    foreach (var v in oldViewers)
                        await v.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leave, ct);
                }
            }
        }

        string entityId = $"ent_{character.CharacterId}";
        session.AccountId = sess.AccountId;
        session.SessionToken = req.SessionToken;
        session.EntityId = entityId;
        session.CharacterId = character.CharacterId;
        session.CharacterName = character.Name;
        session.ClassId = character.ClassId;

        await _storage.Sessions.BindCharacterAsync(req.SessionToken, character.CharacterId, ct);

        var map = _versionPack.GetMap(character.MapId);
        // Clamp to spawn if saved position is blocked (map data changed)
        int x = character.X;
        int y = character.Y;
        if (map.IsBlocked(x, y))
        {
            x = map.SpawnX;
            y = map.SpawnY;
        }

        var entity = new WorldPlayerEntity
        {
            EntityId = entityId,
            CharacterId = character.CharacterId,
            Name = character.Name,
            ClassId = character.ClassId,
            MapId = map.MapId,
            Session = session,
            X = x,
            Y = y,
            Direction = 4,
            Hp = character.Hp,
            MaxHp = character.MaxHp,
            Mp = character.Mp,
            MaxMp = character.MaxMp
        };

        var aoiMgr = GetAoi(map.MapId);
        var nearby = aoiMgr.AddOrUpdateEntity(entity);

        var resp = new EnterWorldResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            MapId = map.MapId,
            SelfEntity = entity.ToSnapshot(),
            Message = $"Entered map '{map.MapName}' ({map.MapId})",
            InSafeZone = entity.InSafeZone
        };
        foreach (var other in nearby)
            resp.NearbyEntities.Add(other.ToSnapshot());

        await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, resp, ct);

        var enterNotice = new EntityEnterAoi { Entity = entity.ToSnapshot() };
        foreach (var viewer in nearby)
            await viewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, enterNotice, ct);

        StructuredLogger.Info("GameWorldModule",
            $"Player '{character.Name}' entered '{map.MapId}' at ({x},{y}) safe={entity.InSafeZone}",
            connId: session.ConnectionId, playerId: entityId);

        await _eventBus.PublishAsync(new PlayerEnteredWorldEvent(
            session, entityId, character.Name, character.ClassId, map.MapId, x, y), ct);
    }

    private async Task HandleMoveAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(session.EntityId) || string.IsNullOrEmpty(session.CharacterId))
        {
            await RejectMove(session, frame, null, "Player has not entered the world.", ct);
            return;
        }

        WorldPlayerEntity? entity = null;
        GridAoiManager? aoi = null;
        foreach (var kv in _maps)
        {
            if (kv.Value.TryGetEntity(session.EntityId, out var e) && e != null)
            {
                entity = e;
                aoi = kv.Value;
                break;
            }
        }

        if (entity is null || aoi is null)
        {
            await RejectMove(session, frame, null, "Player has not entered the world.", ct);
            return;
        }

        var req = MoveReq.Parser.ParseFrom(frame.Payload);
        int fromX = entity.X;
        int fromY = entity.Y;
        int toX = req.ToX;
        int toY = req.ToY;

        if (!aoi.IsWalkable(toX, toY))
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                EntityId = entity.EntityId,
                FromX = fromX, FromY = fromY, ToX = fromX, ToY = fromY,
                Direction = entity.Direction,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Reason = $"Target cell ({toX},{toY}) is blocked or out of map bounds.",
                InSafeZone = entity.InSafeZone
            }, ct);
            return;
        }

        if (!_antiCheatProbe.ValidateMove(entity.EntityId, fromX, fromY, toX, toY, out string acReason))
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                EntityId = entity.EntityId,
                FromX = fromX, FromY = fromY, ToX = fromX, ToY = fromY,
                Direction = entity.Direction,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Reason = acReason,
                InSafeZone = entity.InSafeZone
            }, ct);
            return;
        }

        var (leftViewers, enteredViewers, currentViewers) = aoi.MoveEntity(entity.EntityId, toX, toY, req.Direction);

        var moveSync = new MoveSync
        {
            EntityId = entity.EntityId,
            FromX = fromX, FromY = fromY, ToX = toX, ToY = toY,
            Direction = req.Direction,
            Accepted = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Reason = "OK",
            InSafeZone = entity.InSafeZone
        };

        await session.SendAsync(MsgId.MoveSync, frame.Seq, moveSync, ct);
        foreach (var viewer in currentViewers)
            await viewer.Session.SendAsync(MsgId.MoveSync, 0, moveSync, ct);

        if (enteredViewers.Count > 0)
        {
            var myEnter = new EntityEnterAoi { Entity = entity.ToSnapshot() };
            foreach (var newViewer in enteredViewers)
            {
                await newViewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, myEnter, ct);
                await session.SendAsync(MsgId.EntityEnterAoi, 0, new EntityEnterAoi { Entity = newViewer.ToSnapshot() }, ct);
            }
        }

        if (leftViewers.Count > 0)
        {
            var myLeave = new EntityLeaveAoi { EntityId = entity.EntityId, Reason = "OutOfAoiRange" };
            foreach (var oldViewer in leftViewers)
            {
                await oldViewer.Session.SendAsync(MsgId.EntityLeaveAoi, 0, myLeave, ct);
                await session.SendAsync(MsgId.EntityLeaveAoi, 0, new EntityLeaveAoi { EntityId = oldViewer.EntityId, Reason = "OutOfAoiRange" }, ct);
            }
        }

        await _storage.Characters.SavePositionAsync(
            entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);

        await _eventBus.PublishAsync(new PlayerMovedEvent(
            entity.EntityId, entity.Name, entity.MapId, fromX, fromY, toX, toY), ct);

        // Portal check after successful move
        var portal = aoi.Map.FindPortalAt(toX, toY);
        if (portal is not null)
            await TransferViaPortalAsync(session, entity, aoi, portal, ct);
    }

    private async Task TransferViaPortalAsync(
        IClientSession session,
        WorldPlayerEntity entity,
        GridAoiManager fromAoi,
        PortalConfig portal,
        CancellationToken ct)
    {
        if (!_versionPack.Maps.ContainsKey(portal.TargetMapId) &&
            !string.Equals(portal.TargetMapId, _versionPack.Map.MapId, StringComparison.OrdinalIgnoreCase))
        {
            StructuredLogger.Warn("GameWorldModule", $"Portal {portal.PortalId} target map '{portal.TargetMapId}' missing");
            return;
        }

        string fromMapId = entity.MapId;
        var leaveViewers = fromAoi.RemoveEntity(entity.EntityId);
        var leaveMsg = new EntityLeaveAoi { EntityId = entity.EntityId, Reason = "Portal" };
        foreach (var v in leaveViewers)
            await v.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leaveMsg, ct);

        var toMap = _versionPack.GetMap(portal.TargetMapId);
        int tx = portal.TargetX;
        int ty = portal.TargetY;
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
            PortalId = portal.PortalId,
            InSafeZone = entity.InSafeZone
        };
        foreach (var n in nearby)
            notify.NearbyEntities.Add(n.ToSnapshot());

        await session.SendAsync(MsgId.MapChangeNotify, 0, notify, ct);

        var enterNotice = new EntityEnterAoi { Entity = entity.ToSnapshot() };
        foreach (var viewer in nearby)
            await viewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, enterNotice, ct);

        StructuredLogger.Info("GameWorldModule",
            $"Portal '{portal.PortalId}': {entity.Name} {fromMapId}-> {toMap.MapId} ({tx},{ty})",
            connId: session.ConnectionId, playerId: entity.EntityId);
    }

    private static Task RejectMove(IClientSession session, PacketFrame frame, WorldPlayerEntity? entity, string reason, CancellationToken ct)
        => session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
        {
            EntityId = entity?.EntityId ?? string.Empty,
            Accepted = false,
            ErrorCode = ProtocolErrorCode.ErrInvalidToken,
            Reason = reason
        }, ct);

    public async Task OnSessionDisconnectedAsync(IClientSession session, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(session.EntityId))
            return;

        foreach (var aoi in _maps.Values)
        {
            if (!aoi.TryGetEntity(session.EntityId, out var entity) || entity is null)
                continue;

            await _storage.Characters.SavePositionAsync(
                entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);

            var viewers = aoi.RemoveEntity(session.EntityId);
            var leaveMsg = new EntityLeaveAoi { EntityId = session.EntityId, Reason = "Disconnected" };
            foreach (var viewer in viewers)
                await viewer.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leaveMsg, ct);
            break;
        }

        if (!string.IsNullOrEmpty(session.AccountId))
            await _storage.Sessions.SetOnlineAsync(session.AccountId, false, ct);
    }
}
