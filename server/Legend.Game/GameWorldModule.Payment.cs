using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private IPaymentProvider _paymentProvider = new MockPaymentProvider("legend-mock-pay-secret");
    private readonly SemaphoreSlim _paymentGate = new(1, 1);
    private string _paymentNotifyBaseUrl = "";

    public void ConfigurePaymentProvider(IPaymentProvider provider)
    {
        _paymentProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>Optional public base for merchant notify_url (e.g. http://host:adminPort).</summary>
    public void ConfigurePaymentNotifyBaseUrl(string? baseUrl)
    {
        _paymentNotifyBaseUrl = (baseUrl ?? "").TrimEnd('/');
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

        string checkoutUrl = "";
        string payPayload = token;
        string providerOrderId = orderId;
        string providerId = _paymentProvider.ProviderId;

        if (_paymentProvider is IMerchantPaymentProvider merchant)
        {
            string notifyUrl = providerId switch
            {
                "wechat" => string.IsNullOrEmpty(_paymentNotifyBaseUrl) ? "" : _paymentNotifyBaseUrl + "/admin/payment/wechat/notify",
                "alipay" => string.IsNullOrEmpty(_paymentNotifyBaseUrl) ? "" : _paymentNotifyBaseUrl + "/admin/payment/alipay/notify",
                _ => ""
            };
            var checkout = await merchant.CreateCheckoutAsync(new PaymentCheckoutRequest
            {
                OrderId = orderId,
                ProductId = product.ProductId,
                Description = product.Name,
                PriceCents = product.PriceCents,
                NotifyUrl = notifyUrl
            }, ct);
            if (!checkout.Ok)
            {
                await session.SendAsync(MsgId.PaymentCreateOrderResp, frame.Seq, new PaymentCreateOrderResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrPaymentInvalid,
                    Message = checkout.Message,
                    ProviderId = providerId
                }, ct);
                return;
            }
            checkoutUrl = checkout.CheckoutUrl ?? "";
            payPayload = string.IsNullOrWhiteSpace(checkout.PayPayload) ? token : checkout.PayPayload;
            providerOrderId = string.IsNullOrWhiteSpace(checkout.ProviderOrderId) ? orderId : checkout.ProviderOrderId;
            if (checkout.UsedSandboxSimulator)
                StructuredLoggerMaybe($"payment checkout sandbox_sim provider={providerId} order={orderId}");
        }

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
            Provider = providerId,
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
            Status = "Pending",
            ProviderId = providerId,
            CheckoutUrl = checkoutUrl,
            PayPayload = payPayload,
            ProviderOrderId = providerOrderId
        }, ct);
    }

    private static void StructuredLoggerMaybe(string msg)
    {
        try
        {
            // Avoid hard dependency if Core logger shape changes
            Console.Error.WriteLine($"[Legend.Game] {msg}");
        }
        catch { /* ignore */ }
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
    /// When <paramref name="paidAmountCents"/> is set, it must match order.PriceCents.
    /// </summary>
    public async Task<(bool Ok, string Message, PaymentOrderRecord? Order)> FulfillPaymentOrderAsync(
        string orderId,
        string providerTxnId,
        bool requirePaidTransition,
        CancellationToken ct = default,
        int? paidAmountCents = null)
    {
        await _paymentGate.WaitAsync(ct);
        try
        {
            var existing = await _storage.Payments.GetByIdAsync(orderId, ct);
            if (existing is null)
                return (false, "order_not_found", null);

            if (paidAmountCents is int amt && amt != existing.PriceCents)
                return (false, "amount_mismatch", existing);

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

            var product = PaymentCatalog.Find(existing.ProductId);
            string rewardCurrencyId = product?.RewardCurrencyId?.Trim() ?? "";
            bool multiCurrencyGrant = !string.IsNullOrEmpty(rewardCurrencyId)
                && !string.Equals(rewardCurrencyId, CurrencyWallet.GoldId, StringComparison.OrdinalIgnoreCase)
                && CurrencyWallet.IsEnabled(_versionPack.Currencies, rewardCurrencyId);

            // Sample / default: gold via mail. Multi-currency packs may grant a non-gold currency into the wallet.
            int mailGold = multiCurrencyGrant ? 0 : existing.RewardGold;

            var charRec = await _storage.Characters.GetByIdAsync(existing.CharacterId, ct);
            string nameHint = charRec?.Name ?? "";

            var (okMail, mailErr, mail) = await SendMailAsync(
                existing.CharacterId,
                nameHint,
                "系统充值",
                $"充值发货：{existing.ProductId}",
                multiCurrencyGrant
                    ? $"订单 {existing.OrderId} 已支付，{rewardCurrencyId} x{existing.RewardGold} 已入账。"
                    : $"订单 {existing.OrderId} 已支付，附件请查收。",
                mailGold,
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
                if (multiCurrencyGrant && existing.RewardGold > 0)
                {
                    CreditCurrency(online, rewardCurrencyId, existing.RewardGold);
                    await PersistEconomyAsync(online, ct);
                }
                await online.Session.SendAsync(MsgId.PaymentFulfilledNotify, 0, new PaymentFulfilledNotify
                {
                    OrderId = fulfilled.OrderId,
                    ProductId = fulfilled.ProductId,
                    MailId = mail.MailId,
                    RewardGold = multiCurrencyGrant ? 0 : fulfilled.RewardGold,
                    RewardItemId = fulfilled.RewardItemId ?? "",
                    RewardItemCount = fulfilled.RewardItemCount
                }, ct);
            }
            else if (multiCurrencyGrant && existing.RewardGold > 0 && charRec != null)
            {
                // Offline: fold currency into appearance_json wallet.
                var state = AppearanceJson.Parse(charRec.AppearanceJson);
                state.Wallet ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                CurrencyWallet.Credit(state.Wallet, rewardCurrencyId, existing.RewardGold);
                CurrencyWallet.EnsureGoldMirror(state.Wallet, charRec.Gold);
                await _storage.Characters.SaveAppearanceAsync(
                    charRec.CharacterId, AppearanceJson.Serialize(state), ct);
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
