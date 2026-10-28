using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;

namespace Legend.Game;

public sealed class GameWorldModule : IGameModule
{
    private readonly IEventBus _eventBus;
    private readonly LoadedVersionPack _versionPack;
    private readonly Func<string, (bool Valid, string AccountId)> _tokenValidator;
    private readonly IAntiCheatProbe _antiCheatProbe;
    private readonly GridAoiManager _aoi;

    public GameWorldModule(
        IEventBus eventBus,
        LoadedVersionPack versionPack,
        Func<string, (bool Valid, string AccountId)> tokenValidator,
        IAntiCheatProbe? antiCheatProbe = null)
    {
        _eventBus = eventBus;
        _versionPack = versionPack;
        _tokenValidator = tokenValidator;
        _antiCheatProbe = antiCheatProbe ?? new DefaultAntiCheatProbe(maxStepDistance: 2);
        _aoi = new GridAoiManager(versionPack.Map);
    }

    public string ModuleName => "GameWorldModule";
    public int OnlineCount => _aoi.OnlineCount;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public bool CanHandle(MsgId msgId) => msgId is
        MsgId.EnterWorldReq or
        MsgId.MoveReq;

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
        var (valid, accountId) = _tokenValidator(req.SessionToken);
        if (!valid)
        {
            await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, new EnterWorldResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Invalid or expired session token."
            }, ct);
            return;
        }

        string classId = string.IsNullOrWhiteSpace(req.ClassId) ? "warrior" : req.ClassId.Trim().ToLowerInvariant();
        if (!_versionPack.Classes.TryGetValue(classId, out var classConfig))
        {
            classConfig = _versionPack.Classes.Values.First();
            classId = classConfig.ClassId;
        }

        string entityId = $"char_{accountId}";
        string charName = string.IsNullOrWhiteSpace(req.CharacterName) ? accountId : req.CharacterName.Trim();

        session.AccountId = accountId;
        session.EntityId = entityId;
        session.CharacterName = charName;
        session.ClassId = classId;

        var entity = new WorldPlayerEntity
        {
            EntityId = entityId,
            Name = charName,
            ClassId = classId,
            MapId = _versionPack.Map.MapId,
            Session = session,
            X = _versionPack.Map.SpawnX,
            Y = _versionPack.Map.SpawnY,
            Direction = 4,
            Hp = classConfig.BaseHp,
            MaxHp = classConfig.BaseHp
        };

        var nearby = _aoi.AddOrUpdateEntity(entity);

        var resp = new EnterWorldResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            MapId = _versionPack.Map.MapId,
            SelfEntity = entity.ToSnapshot(),
            Message = $"Entered map '{_versionPack.Map.MapName}' ({_versionPack.Map.MapId})"
        };
        foreach (var other in nearby)
        {
            resp.NearbyEntities.Add(other.ToSnapshot());
        }

        await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, resp, ct);

        // Broadcast EntityEnterAoi to nearby players in AOI
        var enterNotice = new EntityEnterAoi { Entity = entity.ToSnapshot() };
        foreach (var viewer in nearby)
        {
            await viewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, enterNotice, ct);
        }

        StructuredLogger.Info("GameWorldModule",
            $"Player '{charName}' ({entityId}) entered '{_versionPack.Map.MapId}' at ({entity.X},{entity.Y}), AOI peers={nearby.Count}",
            connId: session.ConnectionId, playerId: entityId);

        // Publish PlayerEnteredWorldEvent (triggers Lua welcome hook & telemetry plugins)
        await _eventBus.PublishAsync(new PlayerEnteredWorldEvent(
            session, entityId, charName, classId, _versionPack.Map.MapId, entity.X, entity.Y), ct);
    }

    private async Task HandleMoveAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(session.EntityId) || !_aoi.TryGetEntity(session.EntityId, out var entity) || entity == null)
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Reason = "Player has not entered the world."
            }, ct);
            return;
        }

        var req = MoveReq.Parser.ParseFrom(frame.Payload);
        int fromX = entity.X;
        int fromY = entity.Y;
        int toX = req.ToX;
        int toY = req.ToY;

        if (!_aoi.IsWalkable(toX, toY))
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                EntityId = entity.EntityId,
                FromX = fromX,
                FromY = fromY,
                ToX = fromX,
                ToY = fromY,
                Direction = entity.Direction,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Reason = $"Target cell ({toX},{toY}) is blocked or out of map bounds."
            }, ct);
            return;
        }

        if (!_antiCheatProbe.ValidateMove(entity.EntityId, fromX, fromY, toX, toY, out string acReason))
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                EntityId = entity.EntityId,
                FromX = fromX,
                FromY = fromY,
                ToX = fromX,
                ToY = fromY,
                Direction = entity.Direction,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Reason = acReason
            }, ct);
            return;
        }

        var (leftViewers, enteredViewers, currentViewers) = _aoi.MoveEntity(entity.EntityId, toX, toY, req.Direction);

        var moveSync = new MoveSync
        {
            EntityId = entity.EntityId,
            FromX = fromX,
            FromY = fromY,
            ToX = toX,
            ToY = toY,
            Direction = req.Direction,
            Accepted = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Reason = "OK"
        };

        // 1. Confirm move to mover
        await session.SendAsync(MsgId.MoveSync, frame.Seq, moveSync, ct);

        // 2. Broadcast MoveSync to all current AOI viewers
        foreach (var viewer in currentViewers)
        {
            await viewer.Session.SendAsync(MsgId.MoveSync, 0, moveSync, ct);
        }

        // 3. Notify newly entered AOI viewers about each other
        if (enteredViewers.Count > 0)
        {
            var myEnter = new EntityEnterAoi { Entity = entity.ToSnapshot() };
            foreach (var newViewer in enteredViewers)
            {
                await newViewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, myEnter, ct);
                await session.SendAsync(MsgId.EntityEnterAoi, 0, new EntityEnterAoi { Entity = newViewer.ToSnapshot() }, ct);
            }
        }

        // 4. Notify viewers that left AOI range
        if (leftViewers.Count > 0)
        {
            var myLeave = new EntityLeaveAoi { EntityId = entity.EntityId, Reason = "OutOfAoiRange" };
            foreach (var oldViewer in leftViewers)
            {
                await oldViewer.Session.SendAsync(MsgId.EntityLeaveAoi, 0, myLeave, ct);
                await session.SendAsync(MsgId.EntityLeaveAoi, 0, new EntityLeaveAoi { EntityId = oldViewer.EntityId, Reason = "OutOfAoiRange" }, ct);
            }
        }

        await _eventBus.PublishAsync(new PlayerMovedEvent(
            entity.EntityId, entity.Name, entity.MapId, fromX, fromY, toX, toY), ct);
    }

    public async Task OnSessionDisconnectedAsync(IClientSession session, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(session.EntityId))
        {
            var viewers = _aoi.RemoveEntity(session.EntityId);
            var leaveMsg = new EntityLeaveAoi
            {
                EntityId = session.EntityId,
                Reason = "Disconnected"
            };
            foreach (var viewer in viewers)
            {
                await viewer.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leaveMsg, ct);
            }
        }
    }
}
