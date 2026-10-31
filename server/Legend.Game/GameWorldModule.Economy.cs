using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private async Task HandleShopSellAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = ShopSellReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq, FailSell(ProtocolErrorCode.ErrInvalidToken, "Not in world"), ct);
            return;
        }
        if (!TryGetNpc(req.NpcEntityId, out var npc) || !NearNpc(player, npc))
        {
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq, FailSell(ProtocolErrorCode.ErrNpcTooFar, "Too far from NPC"), ct);
            return;
        }
        if (string.IsNullOrEmpty(npc.Template.ShopId))
        {
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq, FailSell(ProtocolErrorCode.ErrShopDenied, "NPC has no shop"), ct);
            return;
        }
        _versionPack.Shops.TryGetValue(npc.Template.ShopId, out var shop);
        int count = Math.Max(1, req.Count);
        if (!_versionPack.Items.TryGetValue(req.ItemId, out var item))
        {
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq, FailSell(ProtocolErrorCode.ErrShopDenied, "Unknown item"), ct);
            return;
        }

        // Recycle table (docs/site/11) overrides plain sell price when the item is listed.
        if (_versionPack.Recycle.TryGetValue(item.ItemId, out var recycle))
        {
            await HandleRecycleSellAsync(session, frame, player, item, recycle, count, ct);
            return;
        }

        int unit = PhaseLRules.SellUnitPrice(item, shop);
        bool removed = false;
        lock (_combatLock)
            removed = InventoryHelper.TryRemoveItem(player.Inventory, item.ItemId, count);
        if (!removed)
        {
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq, FailSell(ProtocolErrorCode.ErrShopDenied, "Not enough items", player.Gold), ct);
            return;
        }
        int gained = unit * count;
        CreditCurrency(player, CurrencyWallet.GoldId, gained);
        await PersistEconomyAsync(player, ct);
        var resp = new ShopSellResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            GoldGained = gained,
            Gold = player.Gold
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        await session.SendAsync(MsgId.ShopSellResp, frame.Seq, resp, ct);
        await PushInventoryAsync(session, player, "ShopSell", ct);
    }

    private async Task HandleRecycleSellAsync(
        IClientSession session,
        PacketFrame frame,
        WorldPlayerEntity player,
        ItemTemplate item,
        RecycleConfigItem recycle,
        int count,
        CancellationToken ct)
    {
        if (!CurrencyWallet.IsEnabled(_versionPack.Currencies, recycle.CurrencyId))
        {
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq,
                FailSell(ProtocolErrorCode.ErrShopDenied, "Currency not enabled", player.Gold), ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        player.RecycleDaily ??= new Dictionary<string, RecycleDailyJson.Entry>(StringComparer.OrdinalIgnoreCase);
        var daily = RecycleDailyJson.GetOrCreate(player.RecycleDaily, item.ItemId, now);
        if (recycle.DailyLimit > 0 && daily.UsedToday + count > recycle.DailyLimit)
        {
            int remain = Math.Max(0, recycle.DailyLimit - daily.UsedToday);
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq,
                FailSell(ProtocolErrorCode.ErrShopDenied, $"Daily recycle limit reached (remain {remain})", player.Gold), ct);
            return;
        }

        bool removed = false;
        lock (_combatLock)
            removed = InventoryHelper.TryRemoveItem(player.Inventory, item.ItemId, count);
        if (!removed)
        {
            await session.SendAsync(MsgId.ShopSellResp, frame.Seq,
                FailSell(ProtocolErrorCode.ErrShopDenied, "Not enough items", player.Gold), ct);
            return;
        }

        long gainedLong = (long)recycle.Amount * count;
        int goldGained = 0;
        CreditCurrency(player, recycle.CurrencyId, gainedLong);
        if (string.Equals(recycle.CurrencyId, CurrencyWallet.GoldId, StringComparison.OrdinalIgnoreCase))
            goldGained = (int)Math.Clamp(gainedLong, 0, int.MaxValue);

        daily.UsedToday += count;
        // destroyItem: item already removed from inventory; no NPC stock in this engine.

        if (recycle.GrantExp > 0)
        {
            int expGain = recycle.GrantExp * count;
            // Exp via existing level curve path when available; otherwise accumulate Exp only.
            player.Exp = Math.Max(0, player.Exp + Math.Max(0, expGain));
        }

        await PersistEconomyAsync(player, ct);
        if (recycle.GrantExp > 0)
            await _storage.Characters.SaveLevelExpAsync(player.CharacterId, player.Level, player.Exp, player.MaxHp, player.MaxMp, ct);

        var resp = new ShopSellResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Recycled",
            GoldGained = goldGained,
            Gold = player.Gold
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        await session.SendAsync(MsgId.ShopSellResp, frame.Seq, resp, ct);
        await PushInventoryAsync(session, player, "Recycle", ct);
    }

    private void CreditCurrency(WorldPlayerEntity player, string currencyId, long amount)
    {
        player.Wallet ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        CurrencyWallet.Credit(player.Wallet, currencyId, amount);
        if (string.Equals(currencyId, CurrencyWallet.GoldId, StringComparison.OrdinalIgnoreCase))
            player.Gold = CurrencyWallet.GetGold(player.Wallet);
        else
            CurrencyWallet.EnsureGoldMirror(player.Wallet, player.Gold);
    }

    private async Task HandleRepairAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = RepairReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.RepairResp, frame.Seq, new RepairResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        if (!TryGetNpc(req.NpcEntityId, out var npc) || !NearNpc(player, npc) || !npc.Template.CanRepair)
        {
            await session.SendAsync(MsgId.RepairResp, frame.Seq, new RepairResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "这里不能修理"
            }, ct);
            return;
        }
        int cost = PhaseLRules.RepairCost(player.Equipment, req.Slot);
        if (cost <= 0)
        {
            await session.SendAsync(MsgId.RepairResp, frame.Seq, new RepairResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "没有可修理的装备",
                Gold = player.Gold
            }, ct);
            return;
        }
        if (player.Gold < cost)
        {
            await session.SendAsync(MsgId.RepairResp, frame.Seq, new RepairResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                Message = "金币不足",
                Gold = player.Gold
            }, ct);
            return;
        }
        player.Gold -= cost;
        foreach (var piece in player.Equipment)
        {
            if (!string.IsNullOrWhiteSpace(req.Slot) &&
                !string.Equals(piece.Slot, req.Slot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (piece.MaxDurability <= 0)
                piece.MaxDurability = PhaseMRules.DurabilityMax;
            piece.Durability = piece.MaxDurability;
        }
        EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
        await PersistEconomyAsync(player, ct);
        await PersistEquipmentAsync(player, ct);
        var resp = new RepairResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "修理完成",
            GoldSpent = cost,
            Gold = player.Gold
        };
        foreach (var eq in EquipmentHelper.ToProto(player.Equipment, _versionPack.Items))
            resp.Equipment.Add(eq);
        await session.SendAsync(MsgId.RepairResp, frame.Seq, resp, ct);
    }

    private async Task HandleStorageListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StorageListReq.Parser.ParseFrom(frame.Payload);
        if (!TryStorageNpc(session, req.NpcEntityId, out var player, out var error))
        {
            await session.SendAsync(MsgId.StorageListResp, frame.Seq, new StorageListResp
            {
                Success = false,
                ErrorCode = error,
                Message = "无法打开仓库"
            }, ct);
            return;
        }
        var resp = OkStorageList(player!);
        await session.SendAsync(MsgId.StorageListResp, frame.Seq, resp, ct);
    }

    private async Task HandleStorageDepositAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StorageDepositReq.Parser.ParseFrom(frame.Payload);
        if (!TryStorageNpc(session, req.NpcEntityId, out var player, out var error) || player is null)
        {
            await session.SendAsync(MsgId.StorageDepositResp, frame.Seq, FailDeposit(error, "无法打开仓库"), ct);
            return;
        }
        int count = Math.Max(1, req.Count);
        player.Storage ??= new();
        bool stackExists = player.Storage.Any(s => string.Equals(s.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase));
        if (!stackExists && player.Storage.Count >= PhaseLRules.StorageCapacity)
        {
            await session.SendAsync(MsgId.StorageDepositResp, frame.Seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "仓库已满"), ct);
            return;
        }
        if (!InventoryHelper.TryRemoveItem(player.Inventory, req.ItemId, count))
        {
            await session.SendAsync(MsgId.StorageDepositResp, frame.Seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "背包数量不足"), ct);
            return;
        }
        InventoryHelper.AddItem(player.Storage, req.ItemId, count);
        await PersistEconomyAsync(player, ct);
        await PersistAppearanceAsync(player, ct);
        await session.SendAsync(MsgId.StorageDepositResp, frame.Seq, OkDeposit(player), ct);
        await PushInventoryAsync(session, player, "StorageDeposit", ct);
    }

    private async Task HandleStorageWithdrawAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StorageWithdrawReq.Parser.ParseFrom(frame.Payload);
        if (!TryStorageNpc(session, req.NpcEntityId, out var player, out var error) || player is null)
        {
            await session.SendAsync(MsgId.StorageWithdrawResp, frame.Seq, FailWithdraw(error, "无法打开仓库"), ct);
            return;
        }
        int count = Math.Max(1, req.Count);
        player.Storage ??= new();
        if (!InventoryHelper.TryRemoveItem(player.Storage, req.ItemId, count))
        {
            await session.SendAsync(MsgId.StorageWithdrawResp, frame.Seq, FailWithdraw(ProtocolErrorCode.ErrShopDenied, "仓库数量不足"), ct);
            return;
        }
        InventoryHelper.AddItem(player.Inventory, req.ItemId, count);
        await PersistEconomyAsync(player, ct);
        await PersistAppearanceAsync(player, ct);
        await session.SendAsync(MsgId.StorageWithdrawResp, frame.Seq, OkWithdraw(player), ct);
        await PushInventoryAsync(session, player, "StorageWithdraw", ct);
    }

    private bool TryStorageNpc(IClientSession session, string npcEntityId, out WorldPlayerEntity? player, out ProtocolErrorCode error)
    {
        player = null;
        error = ProtocolErrorCode.ErrNone;
        if (!TryGetPlayer(session, out player, out _) || player is null)
        {
            error = ProtocolErrorCode.ErrInvalidToken;
            return false;
        }
        if (!TryGetNpc(npcEntityId, out var npc) || !NearNpc(player, npc) || !npc.Template.CanStore)
        {
            error = ProtocolErrorCode.ErrNpcTooFar;
            return false;
        }
        return true;
    }

    private static bool NearNpc(WorldPlayerEntity player, NpcEntity npc)
        => string.Equals(player.MapId, npc.MapId, StringComparison.OrdinalIgnoreCase)
           && CombatMath.Chebyshev(player.X, player.Y, npc.X, npc.Y) <= NpcInteractRange;

    private static ShopSellResp FailSell(ProtocolErrorCode code, string message, int gold = 0) => new()
    {
        Success = false,
        ErrorCode = code,
        Message = message,
        Gold = gold
    };

    private static StorageDepositResp FailDeposit(ProtocolErrorCode code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        Message = message
    };

    private static StorageWithdrawResp FailWithdraw(ProtocolErrorCode code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        Message = message
    };

    private static StorageListResp OkStorageList(WorldPlayerEntity player)
    {
        var resp = new StorageListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            Capacity = PhaseLRules.StorageCapacity
        };
        foreach (var slot in InventoryHelper.ToProto(player.Storage ?? new()))
            resp.Items.Add(slot);
        return resp;
    }

    private static StorageDepositResp OkDeposit(WorldPlayerEntity player)
    {
        var resp = new StorageDepositResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        foreach (var slot in InventoryHelper.ToProto(player.Storage))
            resp.Storage.Add(slot);
        return resp;
    }

    private static StorageWithdrawResp OkWithdraw(WorldPlayerEntity player)
    {
        var resp = new StorageWithdrawResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        foreach (var slot in InventoryHelper.ToProto(player.Storage))
            resp.Storage.Add(slot);
        return resp;
    }

    private async Task PushInventoryAsync(IClientSession session, WorldPlayerEntity player, string reason, CancellationToken ct)
    {
        var notify = new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = reason,
            Gold = player.Gold
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            notify.Items.Add(slot);
        await session.SendAsync(MsgId.InventoryNotify, 0, notify, ct);
    }
}
