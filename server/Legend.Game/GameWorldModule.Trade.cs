using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly SemaphoreSlim _tradeGate = new(1, 1);

    private TradeConfig TradeCfg => _versionPack.Trade ?? new TradeConfig();

    private static StallListingInfo ToProtoListing(TradeListingRecord r) => new()
    {
        ListingId = r.ListingId,
        SellerCharacterId = r.SellerCharacterId,
        SellerName = r.SellerName,
        ItemId = r.ItemId,
        Count = r.Count,
        UnitPriceGold = r.UnitPriceGold,
        TotalPriceGold = r.TotalPriceGold,
        CreatedUnixMs = r.CreatedAt.ToUnixTimeMilliseconds()
    };

    private async Task HandleStallListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StallListReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (player.IsDead)
        {
            await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "死亡状态无法上架"
            }, ct);
            return;
        }

        var cfg = TradeCfg;
        var map = _versionPack.GetMap(player.MapId);
        if (cfg.RequireSafeZone && !map.IsInSafeZone(player.X, player.Y))
        {
            await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSafeZone,
                Message = "只能在安全区内摆摊上架"
            }, ct);
            return;
        }

        string itemId = (req.ItemId ?? string.Empty).Trim();
        int count = req.Count;
        int unitPrice = req.UnitPriceGold;
        if (string.IsNullOrEmpty(itemId) || count <= 0)
        {
            await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrTradeDenied,
                Message = "物品或数量不合法"
            }, ct);
            return;
        }

        if (_versionPack.Items.Count > 0 && !_versionPack.Items.ContainsKey(itemId))
        {
            await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrTradeDenied,
                Message = $"未知物品：{itemId}"
            }, ct);
            return;
        }

        if (unitPrice < cfg.MinUnitPriceGold || unitPrice > cfg.MaxUnitPriceGold)
        {
            await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrTradeDenied,
                Message = $"单价须在 {cfg.MinUnitPriceGold}–{cfg.MaxUnitPriceGold} 金币之间"
            }, ct);
            return;
        }

        long totalPriceLong = (long)count * unitPrice;
        if (totalPriceLong <= 0 || totalPriceLong > 10_000_000)
        {
            await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrTradeDenied,
                Message = "总价超出上限"
            }, ct);
            return;
        }
        int totalPrice = (int)totalPriceLong;

        TradeListingRecord created;
        await _tradeGate.WaitAsync(ct);
        try
        {
            int activeCount = await _storage.Trades.CountActiveBySellerAsync(player.CharacterId, ct);
            if (activeCount >= cfg.MaxListingsPerChar)
            {
                await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrListingLimit,
                    Message = $"最多同时上架 {cfg.MaxListingsPerChar} 单"
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
                await session.SendAsync(MsgId.StallListResp, frame.Seq, new StallListResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrTradeDenied,
                    Message = "背包中对应物品数量不足"
                }, ct);
                return;
            }

            await _storage.Characters.SaveInventoryAsync(
                player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);

            created = await _storage.Trades.CreateAsync(new TradeListingRecord
            {
                SellerCharacterId = player.CharacterId,
                SellerName = player.Name,
                ItemId = itemId,
                Count = count,
                UnitPriceGold = unitPrice,
                TotalPriceGold = totalPrice
            }, ct);
        }
        finally
        {
            _tradeGate.Release();
        }

        var resp = new StallListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "上架成功",
            Listing = ToProtoListing(created)
        };
        foreach (var inv in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(inv);

        await session.SendAsync(MsgId.StallListResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "stall_list",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new StallListedEvent(
            session, player.Name, player.CharacterId, created.ListingId, created.ItemId, created.Count, created.UnitPriceGold, created.TotalPriceGold), ct);
    }

    private async Task HandleStallBrowseAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StallBrowseReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.StallBrowseResp, frame.Seq, new StallBrowseResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        var list = await _storage.Trades.ListActiveAsync(req.ItemIdFilter, ct);
        var resp = new StallBrowseResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok"
        };
        foreach (var r in list)
            resp.Listings.Add(ToProtoListing(r));
        await session.SendAsync(MsgId.StallBrowseResp, frame.Seq, resp, ct);
    }

    private async Task HandleStallBuyAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StallBuyReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var buyer) || buyer is null)
        {
            await session.SendAsync(MsgId.StallBuyResp, frame.Seq, new StallBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (buyer.IsDead)
        {
            await session.SendAsync(MsgId.StallBuyResp, frame.Seq, new StallBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "死亡状态无法购买"
            }, ct);
            return;
        }

        string listingId = (req.ListingId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(listingId))
        {
            await session.SendAsync(MsgId.StallBuyResp, frame.Seq, new StallBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrListingNotFound,
                Message = "挂单不存在"
            }, ct);
            return;
        }

        TradeListingRecord soldListing;
        int taxGold;
        int netSellerGold;
        WorldPlayerEntity? onlineSeller = null;
        int sellerNewGold = 0;

        await _tradeGate.WaitAsync(ct);
        try
        {
            var cur = await _storage.Trades.GetByIdAsync(listingId, ct);
            if (cur is null || !string.Equals(cur.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendAsync(MsgId.StallBuyResp, frame.Seq, new StallBuyResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrListingNotFound,
                    Message = "挂单不存在或已售出"
                }, ct);
                return;
            }

            if (string.Equals(cur.SellerCharacterId, buyer.CharacterId, StringComparison.Ordinal))
            {
                await session.SendAsync(MsgId.StallBuyResp, frame.Seq, new StallBuyResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrSelfBuyDenied,
                    Message = "不能购买自己的挂单"
                }, ct);
                return;
            }

            if (buyer.Gold < cur.TotalPriceGold)
            {
                await session.SendAsync(MsgId.StallBuyResp, frame.Seq, new StallBuyResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                    Message = $"金币不足（需 {cur.TotalPriceGold}，当前 {buyer.Gold}）"
                }, ct);
                return;
            }

            var (ok, err, claimed) = await _storage.Trades.TryClaimSoldAsync(
                listingId, buyer.CharacterId, buyer.Name, ct);
            if (!ok || claimed is null)
            {
                await session.SendAsync(MsgId.StallBuyResp, frame.Seq, new StallBuyResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrListingNotFound,
                    Message = err ?? "挂单已售出"
                }, ct);
                return;
            }

            soldListing = claimed;
            int taxRate = Math.Clamp(TradeCfg.TaxRatePercent, 0, 90);
            taxGold = (soldListing.TotalPriceGold * taxRate) / 100;
            netSellerGold = Math.Max(0, soldListing.TotalPriceGold - taxGold);

            lock (_combatLock)
            {
                buyer.Gold -= soldListing.TotalPriceGold;
                InventoryHelper.AddItem(buyer.Inventory, soldListing.ItemId, soldListing.Count);
            }
            await _storage.Characters.SaveGoldAsync(buyer.CharacterId, buyer.Gold, ct);
            await _storage.Characters.SaveInventoryAsync(
                buyer.CharacterId, InventoryHelper.Serialize(buyer.Inventory), ct);

            // Credit seller (online or offline)
            onlineSeller = EnumerateOnlinePlayers().FirstOrDefault(
                p => string.Equals(p.CharacterId, soldListing.SellerCharacterId, StringComparison.Ordinal));
            if (onlineSeller != null)
            {
                lock (_combatLock)
                {
                    onlineSeller.Gold += netSellerGold;
                    sellerNewGold = onlineSeller.Gold;
                }
                await _storage.Characters.SaveGoldAsync(onlineSeller.CharacterId, sellerNewGold, ct);
            }
            else
            {
                var sellerChar = await _storage.Characters.GetByIdAsync(soldListing.SellerCharacterId, ct);
                if (sellerChar != null)
                {
                    sellerNewGold = sellerChar.Gold + netSellerGold;
                    await _storage.Characters.SaveGoldAsync(sellerChar.CharacterId, sellerNewGold, ct);
                }
            }
        }
        finally
        {
            _tradeGate.Release();
        }

        var resp = new StallBuyResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "购买成功",
            Purchased = ToProtoListing(soldListing),
            Gold = buyer.Gold
        };
        foreach (var inv in InventoryHelper.ToProto(buyer.Inventory))
            resp.Inventory.Add(inv);

        await session.SendAsync(MsgId.StallBuyResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = buyer.CharacterId,
            Reason = "stall_buy",
            Gold = buyer.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(buyer.Inventory)) n.Items.Add(i); }), ct);

        if (onlineSeller != null)
        {
            await onlineSeller.Session.SendAsync(MsgId.StallSoldNotify, 0, new StallSoldNotify
            {
                ListingId = soldListing.ListingId,
                BuyerCharacterId = buyer.CharacterId,
                BuyerName = buyer.Name,
                ItemId = soldListing.ItemId,
                Count = soldListing.Count,
                GrossGold = soldListing.TotalPriceGold,
                TaxGold = taxGold,
                NetGold = netSellerGold,
                NewGold = sellerNewGold
            }, ct);
            await onlineSeller.Session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
            {
                CharacterId = onlineSeller.CharacterId,
                Reason = "stall_sold",
                Gold = sellerNewGold
            }.Also(n => { foreach (var i in InventoryHelper.ToProto(onlineSeller.Inventory)) n.Items.Add(i); }), ct);
        }

        await _eventBus.PublishAsync(new StallBoughtEvent(
            session, buyer.Name, buyer.CharacterId, soldListing.SellerName, soldListing.SellerCharacterId,
            soldListing.ListingId, soldListing.ItemId, soldListing.Count, soldListing.TotalPriceGold, netSellerGold), ct);
    }

    private async Task HandleStallCancelAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = StallCancelReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var seller) || seller is null)
        {
            await session.SendAsync(MsgId.StallCancelResp, frame.Seq, new StallCancelResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        string listingId = (req.ListingId ?? string.Empty).Trim();
        TradeListingRecord cancelled;
        await _tradeGate.WaitAsync(ct);
        try
        {
            var (ok, err, rec) = await _storage.Trades.TryCancelAsync(listingId, seller.CharacterId, ct);
            if (!ok || rec is null)
            {
                await session.SendAsync(MsgId.StallCancelResp, frame.Seq, new StallCancelResp
                {
                    Success = false,
                    ErrorCode = string.Equals(err, "无权下架他人挂单", StringComparison.Ordinal)
                        ? ProtocolErrorCode.ErrTradeDenied
                        : ProtocolErrorCode.ErrListingNotFound,
                    Message = err ?? "下架失败"
                }, ct);
                return;
            }
            cancelled = rec;
            lock (_combatLock)
            {
                InventoryHelper.AddItem(seller.Inventory, cancelled.ItemId, cancelled.Count);
            }
            await _storage.Characters.SaveInventoryAsync(
                seller.CharacterId, InventoryHelper.Serialize(seller.Inventory), ct);
        }
        finally
        {
            _tradeGate.Release();
        }

        var resp = new StallCancelResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已下架并退回背包",
            ListingId = cancelled.ListingId
        };
        foreach (var inv in InventoryHelper.ToProto(seller.Inventory))
            resp.Inventory.Add(inv);

        await session.SendAsync(MsgId.StallCancelResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = seller.CharacterId,
            Reason = "stall_cancel",
            Gold = seller.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(seller.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new StallCancelledEvent(
            session, seller.Name, seller.CharacterId, cancelled.ListingId, cancelled.ItemId, cancelled.Count), ct);
    }
}
