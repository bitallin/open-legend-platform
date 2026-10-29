namespace Legend.Contracts.Payment;

/// <summary>Holds mock + merchant providers; selects active for CreateOrder.</summary>
public sealed class PaymentProviderRegistry : IDisposable
{
    public MockPaymentProvider Mock { get; }
    public WechatPaymentProvider Wechat { get; }
    public AlipayPaymentProvider Alipay { get; }
    public IMerchantPaymentProvider Active { get; }
    public string ActiveId { get; }

    public PaymentProviderRegistry(
        string activeProviderId,
        string? mockSecret = null,
        WechatPaymentOptions? wechatOptions = null,
        AlipayPaymentOptions? alipayOptions = null,
        HttpClient? httpClient = null)
    {
        Mock = new MockPaymentProvider(mockSecret ?? "legend-mock-pay-secret");
        Wechat = new WechatPaymentProvider(wechatOptions, httpClient);
        Alipay = new AlipayPaymentProvider(alipayOptions, httpClient);
        ActiveId = (activeProviderId ?? "mock").Trim().ToLowerInvariant();
        Active = ActiveId switch
        {
            "wechat" => Wechat,
            "alipay" => Alipay,
            _ => Mock
        };
    }

    public IMerchantPaymentProvider Resolve(string providerId)
    {
        return (providerId ?? "").Trim().ToLowerInvariant() switch
        {
            "wechat" => Wechat,
            "alipay" => Alipay,
            "mock" => Mock,
            _ => Active
        };
    }

    public static PaymentProviderRegistry FromEnvironment(string? activeOverride = null, string? mockSecret = null)
    {
        string active = activeOverride
            ?? Environment.GetEnvironmentVariable("LEGEND_PAYMENT_PROVIDER")
            ?? "mock";
        return new PaymentProviderRegistry(active, mockSecret);
    }

    public void Dispose()
    {
        Wechat.Dispose();
        Alipay.Dispose();
    }
}
