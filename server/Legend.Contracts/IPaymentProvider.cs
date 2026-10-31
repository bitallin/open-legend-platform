namespace Legend.Contracts;

public sealed class PaymentProductDef
{
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceCents { get; set; }
    public int RewardGold { get; set; }
    public string RewardItemId { get; set; } = "";
    public int RewardItemCount { get; set; }
    /// <summary>
    /// Optional fulfillment currency (docs/site/12). Empty or "gold" uses mail AttachGold.
    /// Other currencyIds credit the runtime wallet when enabled in currencies.json.
    /// </summary>
    public string RewardCurrencyId { get; set; } = "";
}

/// <summary>Legacy + shared payment provider surface (mock HMAC still implements this).</summary>
public interface IPaymentProvider
{
    string ProviderId { get; }
    /// <summary>Create a mock/sandbox checkout token for an order.</summary>
    string CreatePayToken(string orderId, string productId, int priceCents);
    /// <summary>Verify webhook signature. payload is raw body or canonical string.</summary>
    bool VerifyWebhookSignature(string canonicalPayload, string signature);
    string Sign(string canonicalPayload);
}

public sealed class PaymentCheckoutRequest
{
    public string OrderId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Description { get; set; } = "";
    public int PriceCents { get; set; }
    public string NotifyUrl { get; set; } = "";
    public string ClientIp { get; set; } = "";
    public string OpenId { get; set; } = "";
}

public sealed class PaymentCheckoutResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public string ProviderOrderId { get; set; } = "";
    /// <summary>JSON or form string the client uses to invoke payment SDK.</summary>
    public string PayPayload { get; set; } = "";
    public string CheckoutUrl { get; set; } = "";
    public bool UsedSandboxSimulator { get; set; }
}

public sealed class PaymentNotifyResult
{
    public bool SignatureValid { get; set; }
    public bool Paid { get; set; }
    public string OrderId { get; set; } = "";
    public string ProviderTxnId { get; set; } = "";
    public int? AmountCents { get; set; }
    public string RawTradeState { get; set; } = "";
    public string Error { get; set; } = "";
    public string SuccessResponseBody { get; set; } = "";
    public string FailureResponseBody { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
}

/// <summary>Merchant (WeChat / Alipay) checkout + notify parsing.</summary>
public interface IMerchantPaymentProvider : IPaymentProvider
{
    Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutRequest order, CancellationToken ct = default);
    Task<PaymentNotifyResult> ParseAndVerifyNotifyAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default);
}

/// <summary>Sandbox provider (WeChat/Alipay merchants plug in later via same interface).</summary>
public sealed class MockPaymentProvider : IMerchantPaymentProvider
{
    private readonly string _secret;
    public MockPaymentProvider(string secret)
    {
        _secret = string.IsNullOrWhiteSpace(secret) ? "legend-mock-pay-secret" : secret;
    }

    public string ProviderId => "mock";

    public string CreatePayToken(string orderId, string productId, int priceCents)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{_secret}|{orderId}|{productId}|{priceCents}"))).ToLowerInvariant();

    public string Sign(string canonicalPayload)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(_secret));
        return Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant();
    }

    public bool VerifyWebhookSignature(string canonicalPayload, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        string expect = Sign(canonicalPayload);
        return string.Equals(expect, signature.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutRequest order, CancellationToken ct = default)
    {
        string token = CreatePayToken(order.OrderId, order.ProductId, order.PriceCents);
        return Task.FromResult(new PaymentCheckoutResult
        {
            Ok = true,
            Message = "ok",
            ProviderOrderId = order.OrderId,
            PayPayload = token,
            CheckoutUrl = "",
            UsedSandboxSimulator = true
        });
    }

    public Task<PaymentNotifyResult> ParseAndVerifyNotifyAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        // Mock uses AdminHttpServer HandlePaymentWebhookAsync path; this is a fallback.
        return Task.FromResult(new PaymentNotifyResult
        {
            SignatureValid = false,
            Error = "use_mock_webhook_endpoint",
            FailureResponseBody = "{\"ok\":false,\"error\":\"use_mock_webhook\"}",
            ContentType = "application/json"
        });
    }
}

public static class PaymentCatalog
{
    public static readonly IReadOnlyList<PaymentProductDef> DefaultProducts = new[]
    {
        new PaymentProductDef
        {
            ProductId = "gold_100",
            Name = "100金币礼包",
            PriceCents = 100,
            RewardGold = 100
        },
        new PaymentProductDef
        {
            ProductId = "gold_1000",
            Name = "1000金币礼包",
            PriceCents = 900,
            RewardGold = 1000
        },
        new PaymentProductDef
        {
            ProductId = "item_hp_pack",
            Name = "红药礼包",
            PriceCents = 50,
            RewardGold = 0,
            RewardItemId = "item_hp_potion",
            RewardItemCount = 10
        }
    };

    public static PaymentProductDef? Find(string productId)
        => DefaultProducts.FirstOrDefault(p =>
            string.Equals(p.ProductId, productId, StringComparison.OrdinalIgnoreCase));
}
