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

    /// <summary>
    /// Keep CharacterRecord.Gold and wallet["gold"] identical after any cash change.
    /// Direct <c>player.Gold +=/-=</c> without this desyncs the wallet and lets a later
    /// gold Credit reinflate Gold from the stale wallet balance.
    /// </summary>
    private static void AdjustGold(WorldPlayerEntity player, int delta)
    {
        if (delta == 0)
            return;
        long next = (long)player.Gold + delta;
        player.Gold = (int)Math.Clamp(next, 0, int.MaxValue);
        player.Wallet ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        CurrencyWallet.EnsureGoldMirror(player.Wallet, player.Gold);
    }

    private void CreditCurrency(WorldPlayerEntity player, string currencyId, long amount)
    {
        player.Wallet ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        // Mirror first so a prior Gold-only debit cannot be erased by Credit.
        if (string.Equals(currencyId, CurrencyWallet.GoldId, StringComparison.OrdinalIgnoreCase))
            CurrencyWallet.EnsureGoldMirror(player.Wallet, player.Gold);
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
        AdjustGold(player, -cost);
        foreach (var piece in player.Equipment)
        {
            if (!string.IsNullOrWhiteSpace(req.Slot) &&
                !string.Equals(piece.Slot, req.Slot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (piece.MaxDurability <= 0)
                piece.MaxDurability = PhaseMRules.DurabilityMax;
            piece.Durability = piece.MaxDurability;
        }
        EquipmentHelper.RecalculateStats(player, _versionPack);
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

    private static bool IsZodiacContainer(string? containerId)
        => string.Equals(containerId, "zodiac", StringComparison.OrdinalIgnoreCase);

    private static bool IsWarehouseContainer(string? containerId)
        => string.IsNullOrWhiteSpace(containerId)
           || string.Equals(containerId, "warehouse", StringComparison.OrdinalIgnoreCase);

    private async Task HandleStorageListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StorageListReq.Parser.ParseFrom(frame.Payload);
        if (IsZodiacContainer(req.ContainerId))
        {
            if (!TryGetPlayer(session, out var player, out _) || player is null)
            {
                await session.SendAsync(MsgId.StorageListResp, frame.Seq, new StorageListResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                    Message = "无法打开生肖盒",
                    ContainerId = "zodiac"
                }, ct);
                return;
            }
            await session.SendAsync(MsgId.StorageListResp, frame.Seq, OkZodiacList(player), ct);
            return;
        }

        if (!TryStorageNpc(session, req.NpcEntityId, out var warehousePlayer, out var error))
        {
            await session.SendAsync(MsgId.StorageListResp, frame.Seq, new StorageListResp
            {
                Success = false,
                ErrorCode = error,
                Message = "无法打开仓库",
                ContainerId = "warehouse"
            }, ct);
            return;
        }
        var resp = OkStorageList(warehousePlayer!);
        await session.SendAsync(MsgId.StorageListResp, frame.Seq, resp, ct);
    }

    private async Task HandleStorageDepositAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StorageDepositReq.Parser.ParseFrom(frame.Payload);
        if (IsZodiacContainer(req.ContainerId))
        {
            await HandleZodiacDepositAsync(session, frame.Seq, req, ct);
            return;
        }

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
        await session.SendAsync(MsgId.StorageDepositResp, frame.Seq, OkDeposit(player, "warehouse"), ct);
        await PushInventoryAsync(session, player, "StorageDeposit", ct);
    }

    private async Task HandleStorageWithdrawAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StorageWithdrawReq.Parser.ParseFrom(frame.Payload);
        if (IsZodiacContainer(req.ContainerId))
        {
            await HandleZodiacWithdrawAsync(session, frame.Seq, req, ct);
            return;
        }

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
        await session.SendAsync(MsgId.StorageWithdrawResp, frame.Seq, OkWithdraw(player, "warehouse"), ct);
        await PushInventoryAsync(session, player, "StorageWithdraw", ct);
    }

    private async Task HandleZodiacDepositAsync(
        IClientSession session, uint seq, StorageDepositReq req, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrInvalidToken, "无法打开生肖盒"), ct);
            return;
        }
        if (!_versionPack.Items.TryGetValue(req.ItemId, out var tmpl))
        {
            await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "未知物品"), ct);
            return;
        }
        var allowed = tmpl.AllowedContainers ?? new List<string>();
        if (!allowed.Any(c => string.Equals(c, "zodiac", StringComparison.OrdinalIgnoreCase)))
        {
            await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "该物品不能放入生肖盒"), ct);
            return;
        }
        int count = Math.Max(1, req.Count);
        if (count != 1)
        {
            await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "生肖格每次只能放入一件"), ct);
            return;
        }
        var zodiac = _versionPack.EquipmentSlots.Zodiac ?? new ZodiacContainerConfig();
        var slotIds = zodiac.SlotIds ?? new List<string>();
        if (slotIds.Count == 0)
        {
            await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "生肖盒未配置"), ct);
            return;
        }
        player.ZodiacSlots ??= new();
        var occupied = new HashSet<string>(
            player.ZodiacSlots.Select(z => z.Slot), StringComparer.OrdinalIgnoreCase);
        string? targetSlot = null;
        if (!string.IsNullOrWhiteSpace(req.Slot))
        {
            if (!slotIds.Contains(req.Slot, StringComparer.OrdinalIgnoreCase))
            {
                await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "无效生肖槽"), ct);
                return;
            }
            if (occupied.Contains(req.Slot))
            {
                await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "生肖槽已占用"), ct);
                return;
            }
            targetSlot = slotIds.First(s => string.Equals(s, req.Slot, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(tmpl.PreferredSlot)
                 && slotIds.Contains(tmpl.PreferredSlot, StringComparer.OrdinalIgnoreCase)
                 && !occupied.Contains(tmpl.PreferredSlot))
        {
            targetSlot = slotIds.First(s => string.Equals(s, tmpl.PreferredSlot, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var mask = tmpl.ZodiacSlotMask ?? new List<string>();
            IEnumerable<string> candidates = slotIds;
            if (mask.Count > 0)
                candidates = slotIds.Where(s => mask.Any(m => string.Equals(m, s, StringComparison.OrdinalIgnoreCase)));
            targetSlot = candidates.FirstOrDefault(s => !occupied.Contains(s));
        }
        if (string.IsNullOrWhiteSpace(targetSlot))
        {
            await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "生肖盒已满"), ct);
            return;
        }
        if (!InventoryHelper.TryRemoveItem(player.Inventory, req.ItemId, 1))
        {
            await session.SendAsync(MsgId.StorageDepositResp, seq, FailDeposit(ProtocolErrorCode.ErrShopDenied, "背包数量不足"), ct);
            return;
        }
        player.ZodiacSlots.Add(new EquippedItemState
        {
            Slot = targetSlot,
            ItemId = tmpl.ItemId,
            EnhanceLevel = 0,
            ArtifactLevel = 0,
            Durability = PhaseMRules.DurabilityMax,
            MaxDurability = PhaseMRules.DurabilityMax,
            Identified = !tmpl.NeedsIdentify
        });
        EquipmentHelper.RecalculateStats(player, _versionPack);
        await PersistEconomyAsync(player, ct);
        await PersistAppearanceAsync(player, ct);
        await session.SendAsync(MsgId.StorageDepositResp, seq, OkZodiacMutation(player), ct);
        await PushInventoryAsync(session, player, "ZodiacDeposit", ct);
    }

    private async Task HandleZodiacWithdrawAsync(
        IClientSession session, uint seq, StorageWithdrawReq req, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.StorageWithdrawResp, seq, FailWithdraw(ProtocolErrorCode.ErrInvalidToken, "无法打开生肖盒"), ct);
            return;
        }
        player.ZodiacSlots ??= new();
        EquippedItemState? found = null;
        if (!string.IsNullOrWhiteSpace(req.Slot))
        {
            found = player.ZodiacSlots.FirstOrDefault(z =>
                string.Equals(z.Slot, req.Slot, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            found = player.ZodiacSlots.FirstOrDefault(z =>
                string.Equals(z.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase));
        }
        if (found is null || string.IsNullOrWhiteSpace(found.ItemId))
        {
            await session.SendAsync(MsgId.StorageWithdrawResp, seq, FailWithdraw(ProtocolErrorCode.ErrShopDenied, "生肖槽没有该物品"), ct);
            return;
        }
        int count = Math.Max(1, req.Count);
        if (count != 1)
        {
            await session.SendAsync(MsgId.StorageWithdrawResp, seq, FailWithdraw(ProtocolErrorCode.ErrShopDenied, "生肖格每次只能取出一件"), ct);
            return;
        }
        player.ZodiacSlots.Remove(found);
        InventoryHelper.AddItem(player.Inventory, found.ItemId, 1);
        EquipmentHelper.RecalculateStats(player, _versionPack);
        await PersistEconomyAsync(player, ct);
        await PersistAppearanceAsync(player, ct);
        await session.SendAsync(MsgId.StorageWithdrawResp, seq, OkZodiacWithdraw(player), ct);
        await PushInventoryAsync(session, player, "ZodiacWithdraw", ct);
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
            Capacity = PhaseLRules.StorageCapacity,
            ContainerId = "warehouse"
        };
        foreach (var slot in InventoryHelper.ToProto(player.Storage ?? new()))
            resp.Items.Add(slot);
        return resp;
    }

    private StorageListResp OkZodiacList(WorldPlayerEntity player)
    {
        int cap = _versionPack.EquipmentSlots.Zodiac?.SlotIds?.Count
                  ?? Math.Max(1, _versionPack.EquipmentSlots.Zodiac?.SlotCount ?? 12);
        var resp = new StorageListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            Capacity = cap,
            ContainerId = "zodiac"
        };
        foreach (var z in player.ZodiacSlots ?? new())
        {
            if (string.IsNullOrWhiteSpace(z.ItemId))
                continue;
            resp.Items.Add(new InventoryItem
            {
                ItemId = z.ItemId,
                Count = 1
            });
        }
        return resp;
    }

    private static StorageDepositResp OkDeposit(WorldPlayerEntity player, string containerId = "warehouse")
    {
        var resp = new StorageDepositResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ContainerId = containerId
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        foreach (var slot in InventoryHelper.ToProto(player.Storage))
            resp.Storage.Add(slot);
        return resp;
    }

    private StorageDepositResp OkZodiacMutation(WorldPlayerEntity player)
    {
        var resp = new StorageDepositResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ContainerId = "zodiac"
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        foreach (var z in player.ZodiacSlots ?? new())
        {
            if (string.IsNullOrWhiteSpace(z.ItemId))
                continue;
            resp.Storage.Add(new InventoryItem { ItemId = z.ItemId, Count = 1 });
        }
        return resp;
    }

    private static StorageWithdrawResp OkWithdraw(WorldPlayerEntity player, string containerId = "warehouse")
    {
        var resp = new StorageWithdrawResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ContainerId = containerId
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        foreach (var slot in InventoryHelper.ToProto(player.Storage))
            resp.Storage.Add(slot);
        return resp;
    }

    private StorageWithdrawResp OkZodiacWithdraw(WorldPlayerEntity player)
    {
        var resp = new StorageWithdrawResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ContainerId = "zodiac"
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        foreach (var z in player.ZodiacSlots ?? new())
        {
            if (string.IsNullOrWhiteSpace(z.ItemId))
                continue;
            resp.Storage.Add(new InventoryItem { ItemId = z.ItemId, Count = 1 });
        }
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
