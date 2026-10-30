using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private async Task BroadcastPlayerSnapshotAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(player.MapId) || !_maps.TryGetValue(player.MapId, out var aoi))
            return;
        var msg = new EntityEnterAoi { Entity = player.ToSnapshot() };
        foreach (var viewer in aoi.QueryPlayersNear(player.X, player.Y, player.EntityId))
            await viewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, msg, ct);
    }

    private async Task HandleLeaveWorldAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(session.EntityId))
        {
            await session.SendAsync(MsgId.LeaveWorldResp, frame.Seq, new LeaveWorldResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "已在选角"
            }, ct);
            return;
        }

        foreach (var aoi in _maps.Values)
        {
            if (!aoi.TryGetEntity(session.EntityId, out var entity) || entity is null)
                continue;

            await _storage.Characters.SavePositionAsync(
                entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);
            await _storage.Characters.SaveInventoryAsync(
                entity.CharacterId, InventoryHelper.Serialize(entity.Inventory), ct);
            await _storage.Characters.SaveEquipmentAsync(
                entity.CharacterId, EquipmentHelper.Serialize(entity.Equipment), ct);

            await CleanupPartyOnDisconnectAsync(entity, ct);
            await OnDungeonPlayerDisconnectedAsync(entity, ct);
            if (!string.IsNullOrEmpty(entity.ActivePetEntityId))
            {
                string? eid = entity.ActivePetEntityId;
                if (!string.IsNullOrEmpty(eid) && _pets.TryRemove(eid, out var pet))
                {
                    await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.EntityDespawn,
                        new EntityDespawn { EntityId = pet.EntityId, Reason = "leave", MapId = pet.MapId }, ct);
                }
                entity.ActivePetEntityId = string.Empty;
                await PersistAppearanceAsync(entity, ct);
            }

            var viewers = aoi.RemoveEntity(session.EntityId);
            var leaveMsg = new EntityLeaveAoi { EntityId = session.EntityId, Reason = "LeaveWorld" };
            foreach (var viewer in viewers)
                await viewer.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leaveMsg, ct);
            break;
        }

        session.EntityId = "";
        await session.SendAsync(MsgId.LeaveWorldResp, frame.Seq, new LeaveWorldResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已返回选角"
        }, ct);
    }
}
