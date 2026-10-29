namespace Legend.Contracts;

public sealed class PaymentProductDef
{
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceCents { get; set; }
    public int RewardGold { get; set; }
    public string RewardItemId { get; set; } = "";
    public int RewardItemCount { get; set; }
}

public interface IPaymentProvider
{
    string ProviderId { get; }
    /// <summary>Create a mock/sandbox checkout token for an order.</summary>
    string CreatePayToken(string orderId, string productId, int priceCents);
    /// <summary>Verify webhook signature. payload is raw body or canonical string.</summary>
    bool VerifyWebhookSignature(string canonicalPayload, string signature);
    string Sign(string canonicalPayload);
}

/// <summary>Sandbox provider (WeChat/Alipay merchants plug in later via same interface).</summary>
public sealed class MockPaymentProvider : IPaymentProvider
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
