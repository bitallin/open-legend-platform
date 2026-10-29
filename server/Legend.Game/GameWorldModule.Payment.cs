using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private IPaymentProvider _paymentProvider = new MockPaymentProvider("legend-mock-pay-secret");
    private readonly SemaphoreSlim _paymentGate = new(1, 1);

    public void ConfigurePaymentProvider(IPaymentProvider provider)
    {
        _paymentProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public IPaymentProvider PaymentProvider => _paymentProvider;

    private async Task HandlePaymentCreateOrderAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PaymentCreateOrderReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.PaymentCreateOrderResp, frame.Seq, new PaymentCreateOrderResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        var product = PaymentCatalog.Find(req.ProductId);
        if (product is null)
        {
            await session.SendAsync(MsgId.PaymentCreateOrderResp, frame.Seq, new PaymentCreateOrderResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPaymentInvalid,
                Message = "未知商品"
            }, ct);
            return;
        }

        string orderId = Guid.NewGuid().ToString("N");
        string token = _paymentProvider.CreatePayToken(orderId, product.ProductId, product.PriceCents);
        var order = new PaymentOrderRecord
        {
            OrderId = orderId,
            AccountId = player.Session.AccountId ?? "",
            CharacterId = player.CharacterId,
            ProductId = product.ProductId,
            PriceCents = product.PriceCents,
            RewardGold = product.RewardGold,
            RewardItemId = product.RewardItemId ?? "",
            RewardItemCount = product.RewardItemCount,
            Status = "Pending",
            Provider = _paymentProvider.ProviderId,
            MockPayToken = token,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _storage.Payments.InsertAsync(order, ct);

        await session.SendAsync(MsgId.PaymentCreateOrderResp, frame.Seq, new PaymentCreateOrderResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok",
            OrderId = orderId,
            ProductId = product.ProductId,
            PriceCents = product.PriceCents,
            MockPayToken = token,
            Status = "Pending"
        }, ct);
    }

    private async Task HandlePaymentOrderStatusAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PaymentOrderStatusReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.PaymentOrderStatusResp, frame.Seq, new PaymentOrderStatusResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        var order = await _storage.Payments.GetByIdAsync(req.OrderId, ct);
        if (order is null || order.AccountId != (player.Session.AccountId ?? ""))
        {
            await session.SendAsync(MsgId.PaymentOrderStatusResp, frame.Seq, new PaymentOrderStatusResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPaymentInvalid,
                Message = "订单不存在"
            }, ct);
            return;
        }

        await session.SendAsync(MsgId.PaymentOrderStatusResp, frame.Seq, new PaymentOrderStatusResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok",
            OrderId = order.OrderId,
            ProductId = order.ProductId,
            Status = order.Status,
            FulfilledMailId = order.FulfilledMailId ?? ""
        }, ct);
    }

    /// <summary>
    /// Idempotent fulfill: mark paid (if needed) then deliver mail rewards once.
    /// </summary>
    public async Task<(bool Ok, string Message, PaymentOrderRecord? Order)> FulfillPaymentOrderAsync(
        string orderId,
        string providerTxnId,
        bool requirePaidTransition,
        CancellationToken ct = default)
    {
        await _paymentGate.WaitAsync(ct);
        try
        {
            var existing = await _storage.Payments.GetByIdAsync(orderId, ct);
            if (existing is null)
                return (false, "order_not_found", null);

            if (string.Equals(existing.Status, "Fulfilled", StringComparison.OrdinalIgnoreCase))
                return (true, "already_fulfilled", existing);

            if (requirePaidTransition || string.Equals(existing.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                var (okPaid, errPaid, paid) = await _storage.Payments.TryMarkPaidAsync(orderId, providerTxnId, ct);
                if (!okPaid || paid is null)
                    return (false, errPaid ?? "mark_paid_failed", paid);
                existing = paid;
            }

            if (string.Equals(existing.Status, "Fulfilled", StringComparison.OrdinalIgnoreCase))
                return (true, "already_fulfilled", existing);

            var items = new List<MailAttachmentItemRecord>();
            if (!string.IsNullOrWhiteSpace(existing.RewardItemId) && existing.RewardItemCount > 0)
            {
                items.Add(new MailAttachmentItemRecord
                {
                    ItemId = existing.RewardItemId,
                    Count = existing.RewardItemCount
                });
            }

            var charRec = await _storage.Characters.GetByIdAsync(existing.CharacterId, ct);
            string nameHint = charRec?.Name ?? "";

            var (okMail, mailErr, mail) = await SendMailAsync(
                existing.CharacterId,
                nameHint,
                "系统充值",
                $"充值发货：{existing.ProductId}",
                $"订单 {existing.OrderId} 已支付，附件请查收。",
                existing.RewardGold,
                items,
                ct);

            if (!okMail || mail is null)
                return (false, mailErr ?? "mail_failed", existing);

            var (okF, errF, fulfilled) = await _storage.Payments.TryMarkFulfilledAsync(orderId, mail.MailId, ct);
            if (!okF || fulfilled is null)
                return (false, errF ?? "mark_fulfilled_failed", fulfilled);

            // Notify online player
            var online = EnumerateOnlinePlayers().FirstOrDefault(
                p => string.Equals(p.CharacterId, existing.CharacterId, StringComparison.Ordinal));
            if (online != null)
            {
                await online.Session.SendAsync(MsgId.PaymentFulfilledNotify, 0, new PaymentFulfilledNotify
                {
                    OrderId = fulfilled.OrderId,
                    ProductId = fulfilled.ProductId,
                    MailId = mail.MailId,
                    RewardGold = fulfilled.RewardGold,
                    RewardItemId = fulfilled.RewardItemId ?? "",
                    RewardItemCount = fulfilled.RewardItemCount
                }, ct);
            }

            return (true, "fulfilled", fulfilled);
        }
        finally
        {
            _paymentGate.Release();
        }
    }

    /// <summary>Admin direct recharge by character id/name + product.</summary>
    public async Task<(bool Ok, string Message, PaymentOrderRecord? Order)> AdminRechargeAsync(
        string? characterId,
        string? characterName,
        string productId,
        CancellationToken ct = default)
    {
        CharacterRecord? ch = null;
        if (!string.IsNullOrWhiteSpace(characterId))
            ch = await _storage.Characters.GetByIdAsync(characterId, ct);
        if (ch is null && !string.IsNullOrWhiteSpace(characterName))
            ch = await _storage.Characters.GetByNameAsync(characterName, ct);
        if (ch is null)
            return (false, "character_not_found", null);

        var product = PaymentCatalog.Find(productId);
        if (product is null)
            return (false, "unknown_product", null);

        string orderId = Guid.NewGuid().ToString("N");
        var order = new PaymentOrderRecord
        {
            OrderId = orderId,
            AccountId = ch.AccountId,
            CharacterId = ch.CharacterId,
            ProductId = product.ProductId,
            PriceCents = product.PriceCents,
            RewardGold = product.RewardGold,
            RewardItemId = product.RewardItemId ?? "",
            RewardItemCount = product.RewardItemCount,
            Status = "Pending",
            Provider = "admin",
            MockPayToken = "admin",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _storage.Payments.InsertAsync(order, ct);
        return await FulfillPaymentOrderAsync(orderId, $"admin-{orderId}", requirePaidTransition: true, ct);
    }
}
