using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Legend.Contracts.Payment;

public sealed class WechatPaymentOptions
{
    public string MchId { get; set; } = "";
    public string AppId { get; set; } = "";
    public string ApiV3Key { get; set; } = "";
    public string MchSerialNo { get; set; } = "";
    public string MchPrivateKeyPem { get; set; } = "";
    public string PlatformCertPem { get; set; } = "";
    public string NotifyUrl { get; set; } = "";
    public string ApiBase { get; set; } = "https://api.mch.weixin.qq.com";
    /// <summary>Force local simulator even when keys present (tests).</summary>
    public bool ForceSimulator { get; set; }

    public bool HasMerchantCredentials =>
        !string.IsNullOrWhiteSpace(MchId)
        && !string.IsNullOrWhiteSpace(AppId)
        && !string.IsNullOrWhiteSpace(ApiV3Key)
        && !string.IsNullOrWhiteSpace(MchSerialNo)
        && !string.IsNullOrWhiteSpace(MchPrivateKeyPem);

    public static WechatPaymentOptions FromEnvironment()
    {
        static string Env(string k) => Environment.GetEnvironmentVariable(k) ?? "";
        return new WechatPaymentOptions
        {
            MchId = Env("WECHAT_MCH_ID"),
            AppId = Env("WECHAT_APP_ID"),
            ApiV3Key = Env("WECHAT_API_V3_KEY"),
            MchSerialNo = Env("WECHAT_MCH_SERIAL_NO"),
            MchPrivateKeyPem = Env("WECHAT_MCH_PRIVATE_KEY_PEM"),
            PlatformCertPem = Env("WECHAT_PLATFORM_CERT_PEM"),
            NotifyUrl = Env("WECHAT_NOTIFY_URL"),
            ApiBase = string.IsNullOrWhiteSpace(Env("WECHAT_API_BASE"))
                ? "https://api.mch.weixin.qq.com"
                : Env("WECHAT_API_BASE"),
            ForceSimulator = string.Equals(Env("WECHAT_FORCE_SIM"), "1", StringComparison.OrdinalIgnoreCase)
        };
    }
}

/// <summary>
/// WeChat Pay APIv3-style provider. Without merchant keys uses <see cref="LocalWechatSandboxSimulator"/>
/// and logs mode=sandbox_sim — never pretends a live cloud call succeeded.
/// </summary>
public sealed class WechatPaymentProvider : IMerchantPaymentProvider, IDisposable
{
    private readonly WechatPaymentOptions _opts;
    private readonly HttpClient _http;
    private readonly bool _ownHttp;
    private readonly LocalWechatSandboxSimulator? _sim;
    private readonly RSA? _mchPrivate;
    private readonly RSA? _platformPublic;
    private bool _disposed;

    public WechatPaymentProvider(WechatPaymentOptions? options = null, HttpClient? httpClient = null)
    {
        _opts = options ?? WechatPaymentOptions.FromEnvironment();
        _ownHttp = httpClient is null;
        _http = httpClient ?? new HttpClient();
        bool useSim = _opts.ForceSimulator || !_opts.HasMerchantCredentials;
        if (useSim)
        {
            _sim = new LocalWechatSandboxSimulator();
            Console.Error.WriteLine("[payment:wechat] mode=sandbox_sim (no merchant keys or WECHAT_FORCE_SIM=1)");
        }
        else
        {
            _mchPrivate = PaymentCrypto.CreateRsaFromPem(_opts.MchPrivateKeyPem);
            if (!string.IsNullOrWhiteSpace(_opts.PlatformCertPem))
                _platformPublic = PaymentCrypto.CreateRsaFromPem(_opts.PlatformCertPem);
            Console.Error.WriteLine("[payment:wechat] mode=merchant_live_or_sandbox_gateway");
        }
    }

    public string ProviderId => "wechat";
    public bool IsSimulator => _sim is not null;
    public LocalWechatSandboxSimulator? Simulator => _sim;

    public string CreatePayToken(string orderId, string productId, int priceCents)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"wechat|{orderId}|{productId}|{priceCents}"))).ToLowerInvariant();

    public string Sign(string canonicalPayload)
    {
        if (_sim is not null)
            return _sim.SignCanonical(canonicalPayload);
        if (_mchPrivate is null) return "";
        return PaymentCrypto.SignSha256WithRsaBase64(_mchPrivate, canonicalPayload);
    }

    public bool VerifyWebhookSignature(string canonicalPayload, string signature)
    {
        if (_sim is not null)
            return _sim.VerifyCanonical(canonicalPayload, signature);
        if (_platformPublic is null) return false;
        return PaymentCrypto.VerifySha256WithRsaBase64(_platformPublic, canonicalPayload, signature);
    }

    public async Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutRequest order, CancellationToken ct = default)
    {
        if (_sim is not null)
            return _sim.CreateCheckout(order);

        // Native unified order (APIv3)
        string path = "/v3/pay/transactions/native";
        string url = _opts.ApiBase.TrimEnd('/') + path;
        var bodyObj = new Dictionary<string, object?>
        {
            ["appid"] = _opts.AppId,
            ["mchid"] = _opts.MchId,
            ["description"] = string.IsNullOrWhiteSpace(order.Description) ? order.ProductId : order.Description,
            ["out_trade_no"] = order.OrderId,
            ["notify_url"] = string.IsNullOrWhiteSpace(order.NotifyUrl) ? _opts.NotifyUrl : order.NotifyUrl,
            ["amount"] = new Dictionary<string, object?>
            {
                ["total"] = order.PriceCents,
                ["currency"] = "CNY"
            }
        };
        string body = JsonSerializer.Serialize(bodyObj);
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce = Guid.NewGuid().ToString("N");
        string message = $"POST\n{path}\n{timestamp}\n{nonce}\n{body}\n";
        string signature = PaymentCrypto.SignSha256WithRsaBase64(_mchPrivate!, message);
        string auth =
            $"WECHATPAY2-SHA256-RSA2048 mchid=\"{_opts.MchId}\",nonce_str=\"{nonce}\",signature=\"{signature}\",timestamp=\"{timestamp}\",serial_no=\"{_opts.MchSerialNo}\"";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Authorization", auth);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("User-Agent", "LegendPlatform-WechatPay/1.0");

        using var resp = await _http.SendAsync(req, ct);
        string respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            return new PaymentCheckoutResult
            {
                Ok = false,
                Message = $"wechat_http_{(int)resp.StatusCode}:{Truncate(respBody, 200)}",
                UsedSandboxSimulator = false
            };
        }

        using var doc = JsonDocument.Parse(respBody);
        string codeUrl = doc.RootElement.TryGetProperty("code_url", out var cu) ? cu.GetString() ?? "" : "";
        string prepay = doc.RootElement.TryGetProperty("prepay_id", out var pi) ? pi.GetString() ?? "" : "";
        var payload = new Dictionary<string, string>
        {
            ["code_url"] = codeUrl,
            ["prepay_id"] = prepay,
            ["out_trade_no"] = order.OrderId
        };
        return new PaymentCheckoutResult
        {
            Ok = true,
            Message = "ok",
            ProviderOrderId = string.IsNullOrEmpty(prepay) ? order.OrderId : prepay,
            CheckoutUrl = codeUrl,
            PayPayload = JsonSerializer.Serialize(payload),
            UsedSandboxSimulator = false
        };
    }

    public Task<PaymentNotifyResult> ParseAndVerifyNotifyAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        if (_sim is not null)
            return Task.FromResult(_sim.ParseAndVerifyNotify(rawBody, headers));

        string timestamp = GetHeader(headers, "Wechatpay-Timestamp") ?? GetHeader(headers, "wechatpay-timestamp") ?? "";
        string nonce = GetHeader(headers, "Wechatpay-Nonce") ?? GetHeader(headers, "wechatpay-nonce") ?? "";
        string signature = GetHeader(headers, "Wechatpay-Signature") ?? GetHeader(headers, "wechatpay-signature") ?? "";
        string serial = GetHeader(headers, "Wechatpay-Serial") ?? GetHeader(headers, "wechatpay-serial") ?? "";

        var fail = new PaymentNotifyResult
        {
            SignatureValid = false,
            ContentType = "application/json",
            FailureResponseBody = "{\"code\":\"FAIL\",\"message\":\"sign\"}",
            SuccessResponseBody = "{\"code\":\"SUCCESS\",\"message\":\"成功\"}"
        };

        if (_platformPublic is null)
        {
            fail.Error = "platform_cert_missing";
            return Task.FromResult(fail);
        }

        string message = $"{timestamp}\n{nonce}\n{rawBody}\n";
        if (!PaymentCrypto.VerifySha256WithRsaBase64(_platformPublic, message, signature))
        {
            fail.Error = "bad_signature";
            return Task.FromResult(fail);
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("resource", out var resource))
            {
                fail.Error = "no_resource";
                fail.SignatureValid = true;
                return Task.FromResult(fail);
            }

            string ciphertext = resource.GetProperty("ciphertext").GetString() ?? "";
            string resNonce = resource.GetProperty("nonce").GetString() ?? "";
            string associated = resource.TryGetProperty("associated_data", out var ad)
                ? ad.GetString() ?? "transaction"
                : "transaction";
            string plain = PaymentCrypto.AesGcmDecrypt(_opts.ApiV3Key, associated, resNonce, ciphertext);
            using var inner = JsonDocument.Parse(plain);
            string outTradeNo = inner.RootElement.GetProperty("out_trade_no").GetString() ?? "";
            string txnId = inner.RootElement.TryGetProperty("transaction_id", out var tid)
                ? tid.GetString() ?? ""
                : "";
            string tradeState = inner.RootElement.TryGetProperty("trade_state", out var ts)
                ? ts.GetString() ?? ""
                : "";
            int? amount = null;
            if (inner.RootElement.TryGetProperty("amount", out var amt)
                && amt.TryGetProperty("total", out var total))
                amount = total.GetInt32();

            bool paid = string.Equals(tradeState, "SUCCESS", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new PaymentNotifyResult
            {
                SignatureValid = true,
                Paid = paid,
                OrderId = outTradeNo,
                ProviderTxnId = txnId,
                AmountCents = amount,
                RawTradeState = tradeState,
                ContentType = "application/json",
                SuccessResponseBody = "{\"code\":\"SUCCESS\",\"message\":\"成功\"}",
                FailureResponseBody = "{\"code\":\"FAIL\",\"message\":\"fail\"}",
                Error = paid ? "" : "not_paid"
            });
        }
        catch (Exception ex)
        {
            fail.SignatureValid = true;
            fail.Error = "decrypt_or_parse:" + ex.Message;
            return Task.FromResult(fail);
        }
    }

    private static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mchPrivate?.Dispose();
        _platformPublic?.Dispose();
        _sim?.Dispose();
        if (_ownHttp) _http.Dispose();
    }
}

/// <summary>
/// Protocol-level WeChat APIv3 notify simulator for CI / local (no real mch id).
/// </summary>
public sealed class LocalWechatSandboxSimulator : IDisposable
{
    private readonly RSA _platformPrivate;
    private readonly RSA _platformPublic;
    private readonly string _apiV3Key;
    private readonly string _serial;

    public LocalWechatSandboxSimulator(string? apiV3Key = null)
    {
        var keys = PaymentCrypto.GenerateRsa2048();
        _platformPrivate = keys.Private;
        _platformPublic = keys.Public;
        // 32-byte key
        _apiV3Key = string.IsNullOrWhiteSpace(apiV3Key)
            ? "legend-wechat-sim-key-0123456789" // exactly 32 chars
            : apiV3Key;
        if (Encoding.UTF8.GetByteCount(_apiV3Key) != 32)
            throw new InvalidOperationException("sim api v3 key must be 32 bytes");
        _serial = "SIM" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
    }

    public string PlatformSerial => _serial;
    public string ApiV3Key => _apiV3Key;
    public string PlatformPublicPem => _platformPublic.ExportSubjectPublicKeyInfoPem();

    public PaymentCheckoutResult CreateCheckout(PaymentCheckoutRequest order)
    {
        string codeUrl = $"weixin://wxpay/bizpayurl?pr=sim_{order.OrderId}";
        var payload = new Dictionary<string, string>
        {
            ["code_url"] = codeUrl,
            ["out_trade_no"] = order.OrderId,
            ["mode"] = "sandbox_sim"
        };
        return new PaymentCheckoutResult
        {
            Ok = true,
            Message = "sandbox_sim",
            ProviderOrderId = "wxsim_" + order.OrderId,
            CheckoutUrl = codeUrl,
            PayPayload = JsonSerializer.Serialize(payload),
            UsedSandboxSimulator = true
        };
    }

    public string SignCanonical(string canonical)
        => PaymentCrypto.SignSha256WithRsaBase64(_platformPrivate, canonical);

    public bool VerifyCanonical(string canonical, string signature)
        => PaymentCrypto.VerifySha256WithRsaBase64(_platformPublic, canonical, signature);

    /// <summary>Build a signed + encrypted notify body that ParseAndVerifyNotify accepts.</summary>
    public (string Body, Dictionary<string, string> Headers) BuildPaidNotify(
        string outTradeNo, string transactionId, int amountCents)
    {
        var resourcePlain = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["out_trade_no"] = outTradeNo,
            ["transaction_id"] = transactionId,
            ["trade_state"] = "SUCCESS",
            ["amount"] = new Dictionary<string, object?> { ["total"] = amountCents, ["currency"] = "CNY" }
        });
        string nonce = Guid.NewGuid().ToString("N")[..12];
        string ciphertext = PaymentCrypto.AesGcmEncryptToBase64(_apiV3Key, "transaction", nonce, resourcePlain);
        var envelope = new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["create_time"] = DateTimeOffset.UtcNow.ToString("O"),
            ["resource_type"] = "encrypt-resource",
            ["event_type"] = "TRANSACTION.SUCCESS",
            ["summary"] = "支付成功",
            ["resource"] = new Dictionary<string, object?>
            {
                ["algorithm"] = "AEAD_AES_256_GCM",
                ["ciphertext"] = ciphertext,
                ["nonce"] = nonce,
                ["associated_data"] = "transaction",
                ["original_type"] = "transaction"
            }
        };
        string body = JsonSerializer.Serialize(envelope);
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string hdrNonce = Guid.NewGuid().ToString("N");
        string message = $"{timestamp}\n{hdrNonce}\n{body}\n";
        string signature = PaymentCrypto.SignSha256WithRsaBase64(_platformPrivate, message);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wechatpay-Timestamp"] = timestamp,
            ["Wechatpay-Nonce"] = hdrNonce,
            ["Wechatpay-Signature"] = signature,
            ["Wechatpay-Serial"] = _serial,
            ["Content-Type"] = "application/json"
        };
        return (body, headers);
    }

    public PaymentNotifyResult ParseAndVerifyNotify(string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        string timestamp = Get(headers, "Wechatpay-Timestamp") ?? "";
        string nonce = Get(headers, "Wechatpay-Nonce") ?? "";
        string signature = Get(headers, "Wechatpay-Signature") ?? "";
        var fail = new PaymentNotifyResult
        {
            SignatureValid = false,
            ContentType = "application/json",
            FailureResponseBody = "{\"code\":\"FAIL\",\"message\":\"sign\"}",
            SuccessResponseBody = "{\"code\":\"SUCCESS\",\"message\":\"成功\"}"
        };
        string message = $"{timestamp}\n{nonce}\n{rawBody}\n";
        if (!PaymentCrypto.VerifySha256WithRsaBase64(_platformPublic, message, signature))
        {
            fail.Error = "bad_signature";
            return fail;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var resource = doc.RootElement.GetProperty("resource");
            string ciphertext = resource.GetProperty("ciphertext").GetString() ?? "";
            string resNonce = resource.GetProperty("nonce").GetString() ?? "";
            string associated = resource.TryGetProperty("associated_data", out var ad)
                ? ad.GetString() ?? "transaction"
                : "transaction";
            string plain = PaymentCrypto.AesGcmDecrypt(_apiV3Key, associated, resNonce, ciphertext);
            using var inner = JsonDocument.Parse(plain);
            string outTradeNo = inner.RootElement.GetProperty("out_trade_no").GetString() ?? "";
            string txnId = inner.RootElement.GetProperty("transaction_id").GetString() ?? "";
            string tradeState = inner.RootElement.GetProperty("trade_state").GetString() ?? "";
            int? amount = null;
            if (inner.RootElement.TryGetProperty("amount", out var amt)
                && amt.TryGetProperty("total", out var total))
                amount = total.GetInt32();
            bool paid = string.Equals(tradeState, "SUCCESS", StringComparison.OrdinalIgnoreCase);
            return new PaymentNotifyResult
            {
                SignatureValid = true,
                Paid = paid,
                OrderId = outTradeNo,
                ProviderTxnId = txnId,
                AmountCents = amount,
                RawTradeState = tradeState,
                ContentType = "application/json",
                SuccessResponseBody = "{\"code\":\"SUCCESS\",\"message\":\"成功\"}",
                FailureResponseBody = "{\"code\":\"FAIL\",\"message\":\"fail\"}"
            };
        }
        catch (Exception ex)
        {
            fail.SignatureValid = true;
            fail.Error = "parse:" + ex.Message;
            return fail;
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string> headers, string name)
    {
        foreach (var kv in headers)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    public void Dispose()
    {
        _platformPrivate.Dispose();
        _platformPublic.Dispose();
    }
}
