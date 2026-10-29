using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly SemaphoreSlim _guildWhGate = new(1, 1);

    private static bool IsGuildLeader(WorldPlayerEntity player, GuildRecord guild)
        => string.Equals(guild.LeaderCharacterId, player.CharacterId, StringComparison.Ordinal);

    private static List<InventorySlot> ParseWarehouse(string? json)
        => InventoryHelper.Parse(json);

    private static int CountUsedSlots(IEnumerable<InventorySlot> slots)
        => slots.Count(s => s.Count > 0 && !string.IsNullOrWhiteSpace(s.ItemId));

    private bool CanDeposit(WorldPlayerEntity player, GuildRecord guild, GuildConfig cfg)
        => cfg.MemberCanDeposit || IsGuildLeader(player, guild);

    private bool CanWithdraw(WorldPlayerEntity player, GuildRecord guild, GuildConfig cfg)
        => cfg.MemberCanWithdraw || IsGuildLeader(player, guild);

    private bool CanClear(WorldPlayerEntity player, GuildRecord guild, GuildConfig cfg)
        => cfg.LeaderCanClear && IsGuildLeader(player, guild);

    private async Task HandleGuildWarehouseListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        _ = GuildWarehouseListReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildWarehouseListResp, frame.Seq, new GuildWarehouseListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildWarehouseListResp, frame.Seq, new GuildWarehouseListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会，无法查看行会仓库"
            }, ct);
            return;
        }

        var guild = await _storage.Guilds.GetByIdAsync(player.GuildId, ct);
        if (guild is null)
        {
            await session.SendAsync(MsgId.GuildWarehouseListResp, frame.Seq, new GuildWarehouseListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                Message = "行会不存在"
            }, ct);
            return;
        }

        var cfg = GuildCfg;
        var items = ParseWarehouse(guild.WarehouseJson);
        var resp = new GuildWarehouseListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok",
            GuildId = guild.GuildId,
            CapacitySlots = cfg.WarehouseCapacitySlots,
            UsedSlots = CountUsedSlots(items),
            MaxStackPerItem = cfg.WarehouseMaxStackPerItem,
            CanDeposit = CanDeposit(player, guild, cfg),
            CanWithdraw = CanWithdraw(player, guild, cfg),
            CanClear = CanClear(player, guild, cfg)
        };
        foreach (var inv in InventoryHelper.ToProto(items))
            resp.Items.Add(inv);
        await session.SendAsync(MsgId.GuildWarehouseListResp, frame.Seq, resp, ct);
    }

    private async Task HandleGuildWarehouseDepositAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = GuildWarehouseDepositReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会，无法存入行会仓库"
            }, ct);
            return;
        }

        if (player.IsDead)
        {
            await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "死亡状态无法操作行会仓库"
            }, ct);
            return;
        }

        string itemId = (req.ItemId ?? string.Empty).Trim();
        int count = req.Count;
        if (string.IsNullOrEmpty(itemId) || count <= 0)
        {
            await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                Message = "物品或数量不合法"
            }, ct);
            return;
        }

        if (_versionPack.Items.Count > 0 && !_versionPack.Items.ContainsKey(itemId))
        {
            await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                Message = $"未知物品：{itemId}"
            }, ct);
            return;
        }

        var cfg = GuildCfg;
        List<InventorySlot> warehouse;
        int usedSlots;
        string guildName;

        await _guildWhGate.WaitAsync(ct);
        try
        {
            var guild = await _storage.Guilds.GetByIdAsync(player.GuildId, ct);
            if (guild is null)
            {
                await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                    Message = "行会不存在"
                }, ct);
                return;
            }

            if (!guild.MemberCharacterIds.Contains(player.CharacterId, StringComparer.Ordinal))
            {
                await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                    Message = "非本行会成员"
                }, ct);
                return;
            }

            if (!CanDeposit(player, guild, cfg))
            {
                await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                    Message = "无权存入行会仓库"
                }, ct);
                return;
            }

            warehouse = ParseWarehouse(guild.WarehouseJson);
            var existing = warehouse.FirstOrDefault(s =>
                string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
            bool needsNewSlot = existing is null;
            if (needsNewSlot && CountUsedSlots(warehouse) >= cfg.WarehouseCapacitySlots)
            {
                await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildWarehouseFull,
                    Message = $"行会仓库已满（{cfg.WarehouseCapacitySlots} 格）"
                }, ct);
                return;
            }

            int currentStack = existing?.Count ?? 0;
            if (currentStack + count > cfg.WarehouseMaxStackPerItem)
            {
                await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                    Message = $"单格堆叠上限 {cfg.WarehouseMaxStackPerItem}"
                }, ct);
                return;
            }

            bool deducted;
            lock (_combatLock)
            {
                deducted = InventoryHelper.TryRemoveItem(player.Inventory, itemId, count);
            }
            if (!deducted)
            {
                await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, new GuildWarehouseDepositResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                    Message = "背包中对应物品数量不足"
                }, ct);
                return;
            }

            InventoryHelper.AddItem(warehouse, itemId, count);
            usedSlots = CountUsedSlots(warehouse);
            guildName = guild.Name;

            await _storage.Characters.SaveInventoryAsync(
                player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
            await _storage.Guilds.SaveWarehouseAsync(
                guild.GuildId, InventoryHelper.Serialize(warehouse), ct);
        }
        finally
        {
            _guildWhGate.Release();
        }

        var resp = new GuildWarehouseDepositResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "存入成功",
            UsedSlots = usedSlots
        };
        foreach (var inv in InventoryHelper.ToProto(warehouse))
            resp.WarehouseItems.Add(inv);
        foreach (var inv in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(inv);
        await session.SendAsync(MsgId.GuildWarehouseDepositResp, frame.Seq, resp, ct);

        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "guild_wh_deposit",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new GuildWarehouseDepositEvent(
            session, player.Name, player.CharacterId, player.GuildId, guildName, itemId, count, usedSlots), ct);
    }

    private async Task HandleGuildWarehouseWithdrawAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = GuildWarehouseWithdrawReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会，无法取出行会仓库"
            }, ct);
            return;
        }

        if (player.IsDead)
        {
            await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "死亡状态无法操作行会仓库"
            }, ct);
            return;
        }

        string itemId = (req.ItemId ?? string.Empty).Trim();
        int count = req.Count;
        if (string.IsNullOrEmpty(itemId) || count <= 0)
        {
            await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                Message = "物品或数量不合法"
            }, ct);
            return;
        }

        var cfg = GuildCfg;
        List<InventorySlot> warehouse;
        int usedSlots;
        string guildName;

        await _guildWhGate.WaitAsync(ct);
        try
        {
            var guild = await _storage.Guilds.GetByIdAsync(player.GuildId, ct);
            if (guild is null)
            {
                await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                    Message = "行会不存在"
                }, ct);
                return;
            }

            if (!guild.MemberCharacterIds.Contains(player.CharacterId, StringComparer.Ordinal))
            {
                await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                    Message = "非本行会成员"
                }, ct);
                return;
            }

            if (!CanWithdraw(player, guild, cfg))
            {
                await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                    Message = "无权取出行会仓库"
                }, ct);
                return;
            }

            warehouse = ParseWarehouse(guild.WarehouseJson);
            if (!InventoryHelper.TryRemoveItem(warehouse, itemId, count))
            {
                await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, new GuildWarehouseWithdrawResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                    Message = "行会仓库中对应物品数量不足"
                }, ct);
                return;
            }

            lock (_combatLock)
            {
                InventoryHelper.AddItem(player.Inventory, itemId, count);
            }

            usedSlots = CountUsedSlots(warehouse);
            guildName = guild.Name;

            await _storage.Characters.SaveInventoryAsync(
                player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
            await _storage.Guilds.SaveWarehouseAsync(
                guild.GuildId, InventoryHelper.Serialize(warehouse), ct);
        }
        finally
        {
            _guildWhGate.Release();
        }

        var resp = new GuildWarehouseWithdrawResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "取出成功",
            UsedSlots = usedSlots
        };
        foreach (var inv in InventoryHelper.ToProto(warehouse))
            resp.WarehouseItems.Add(inv);
        foreach (var inv in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(inv);
        await session.SendAsync(MsgId.GuildWarehouseWithdrawResp, frame.Seq, resp, ct);

        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "guild_wh_withdraw",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new GuildWarehouseWithdrawEvent(
            session, player.Name, player.CharacterId, player.GuildId, guildName, itemId, count, usedSlots), ct);
    }

    private async Task HandleGuildWarehouseClearAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        _ = GuildWarehouseClearReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildWarehouseClearResp, frame.Seq, new GuildWarehouseClearResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildWarehouseClearResp, frame.Seq, new GuildWarehouseClearResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会"
            }, ct);
            return;
        }

        var cfg = GuildCfg;
        int cleared;
        string guildName;

        await _guildWhGate.WaitAsync(ct);
        try
        {
            var guild = await _storage.Guilds.GetByIdAsync(player.GuildId, ct);
            if (guild is null)
            {
                await session.SendAsync(MsgId.GuildWarehouseClearResp, frame.Seq, new GuildWarehouseClearResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                    Message = "行会不存在"
                }, ct);
                return;
            }

            if (!CanClear(player, guild, cfg))
            {
                await session.SendAsync(MsgId.GuildWarehouseClearResp, frame.Seq, new GuildWarehouseClearResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrGuildWarehouseDenied,
                    Message = "仅会长可清空行会仓库"
                }, ct);
                return;
            }

            var warehouse = ParseWarehouse(guild.WarehouseJson);
            cleared = CountUsedSlots(warehouse);
            guildName = guild.Name;
            await _storage.Guilds.SaveWarehouseAsync(guild.GuildId, "[]", ct);
        }
        finally
        {
            _guildWhGate.Release();
        }

        await session.SendAsync(MsgId.GuildWarehouseClearResp, frame.Seq, new GuildWarehouseClearResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = cleared > 0 ? $"已清空 {cleared} 格" : "仓库已空",
            ClearedSlots = cleared
        }, ct);

        await _eventBus.PublishAsync(new GuildWarehouseClearEvent(
            session, player.Name, player.CharacterId, player.GuildId, guildName, cleared), ct);
    }
}
