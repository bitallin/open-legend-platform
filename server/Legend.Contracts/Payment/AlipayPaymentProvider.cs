using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Legend.Contracts.Payment;

public sealed class AlipayPaymentOptions
{
    public string AppId { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public string AlipayPublicKeyPem { get; set; } = "";
    public string Gateway { get; set; } = "https://openapi.alipaydev.com/gateway.do";
    public string NotifyUrl { get; set; } = "";
    public bool ForceSimulator { get; set; }

    public bool HasMerchantCredentials =>
        !string.IsNullOrWhiteSpace(AppId)
        && !string.IsNullOrWhiteSpace(PrivateKeyPem)
        && !string.IsNullOrWhiteSpace(AlipayPublicKeyPem);

    public static AlipayPaymentOptions FromEnvironment()
    {
        static string Env(string k) => Environment.GetEnvironmentVariable(k) ?? "";
        string gw = Env("ALIPAY_GATEWAY");
        return new AlipayPaymentOptions
        {
            AppId = Env("ALIPAY_APP_ID"),
            PrivateKeyPem = Env("ALIPAY_PRIVATE_KEY_PEM"),
            AlipayPublicKeyPem = Env("ALIPAY_ALIPAY_PUBLIC_KEY_PEM"),
            Gateway = string.IsNullOrWhiteSpace(gw) ? "https://openapi.alipaydev.com/gateway.do" : gw,
            NotifyUrl = Env("ALIPAY_NOTIFY_URL"),
            ForceSimulator = string.Equals(Env("ALIPAY_FORCE_SIM"), "1", StringComparison.OrdinalIgnoreCase)
        };
    }
}

/// <summary>
/// Alipay openapi sandbox provider (RSA2). Without keys uses LocalAlipaySandboxSimulator.
/// </summary>
public sealed class AlipayPaymentProvider : IMerchantPaymentProvider, IDisposable
{
    private readonly AlipayPaymentOptions _opts;
    private readonly HttpClient _http;
    private readonly bool _ownHttp;
    private readonly LocalAlipaySandboxSimulator? _sim;
    private readonly RSA? _appPrivate;
    private readonly RSA? _alipayPublic;
    private bool _disposed;

    public AlipayPaymentProvider(AlipayPaymentOptions? options = null, HttpClient? httpClient = null)
    {
        _opts = options ?? AlipayPaymentOptions.FromEnvironment();
        _ownHttp = httpClient is null;
        _http = httpClient ?? new HttpClient();
        bool useSim = _opts.ForceSimulator || !_opts.HasMerchantCredentials;
        if (useSim)
        {
            _sim = new LocalAlipaySandboxSimulator();
            Console.Error.WriteLine("[payment:alipay] mode=sandbox_sim (no merchant keys or ALIPAY_FORCE_SIM=1)");
        }
        else
        {
            _appPrivate = PaymentCrypto.CreateRsaFromPem(_opts.PrivateKeyPem);
            _alipayPublic = PaymentCrypto.CreateRsaFromPem(_opts.AlipayPublicKeyPem);
            Console.Error.WriteLine($"[payment:alipay] mode=merchant gateway={_opts.Gateway}");
        }
    }

    public string ProviderId => "alipay";
    public bool IsSimulator => _sim is not null;
    public LocalAlipaySandboxSimulator? Simulator => _sim;

    public string CreatePayToken(string orderId, string productId, int priceCents)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"alipay|{orderId}|{productId}|{priceCents}"))).ToLowerInvariant();

    public string Sign(string canonicalPayload)
    {
        if (_sim is not null) return _sim.Sign(canonicalPayload);
        if (_appPrivate is null) return "";
        return PaymentCrypto.SignSha256WithRsaBase64(_appPrivate, canonicalPayload);
    }

    public bool VerifyWebhookSignature(string canonicalPayload, string signature)
    {
        if (_sim is not null) return _sim.Verify(canonicalPayload, signature);
        if (_alipayPublic is null) return false;
        return PaymentCrypto.VerifySha256WithRsaBase64(_alipayPublic, canonicalPayload, signature);
    }

    public async Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutRequest order, CancellationToken ct = default)
    {
        if (_sim is not null)
            return _sim.CreateCheckout(order);

        // alipay.trade.precreate
        var biz = new Dictionary<string, object?>
        {
            ["out_trade_no"] = order.OrderId,
            ["total_amount"] = (order.PriceCents / 100.0).ToString("0.00"),
            ["subject"] = string.IsNullOrWhiteSpace(order.Description) ? order.ProductId : order.Description
        };
        var param = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = _opts.AppId,
            ["method"] = "alipay.trade.precreate",
            ["format"] = "JSON",
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2",
            ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["version"] = "1.0",
            ["notify_url"] = string.IsNullOrWhiteSpace(order.NotifyUrl) ? _opts.NotifyUrl : order.NotifyUrl,
            ["biz_content"] = JsonSerializer.Serialize(biz)
        };
        string canonical = string.Join("&", param.Select(kv => $"{kv.Key}={kv.Value}"));
        string sign = PaymentCrypto.SignSha256WithRsaBase64(_appPrivate!, canonical);
        param["sign"] = sign;

        var form = new FormUrlEncodedContent(param);
        using var resp = await _http.PostAsync(_opts.Gateway, form, ct);
        string respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            return new PaymentCheckoutResult
            {
                Ok = false,
                Message = $"alipay_http_{(int)resp.StatusCode}:{Truncate(respBody, 200)}",
                UsedSandboxSimulator = false
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(respBody);
            // response key varies: alipay_trade_precreate_response
            JsonElement root = doc.RootElement;
            JsonElement bizResp = root;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.EndsWith("_response", StringComparison.Ordinal))
                {
                    bizResp = prop.Value;
                    break;
                }
            }
            string code = bizResp.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
            if (code != "10000")
            {
                string sub = bizResp.TryGetProperty("sub_msg", out var sm) ? sm.GetString() ?? "" : "";
                return new PaymentCheckoutResult
                {
                    Ok = false,
                    Message = $"alipay_biz:{code}:{sub}",
                    UsedSandboxSimulator = false
                };
            }
            string qr = bizResp.TryGetProperty("qr_code", out var q) ? q.GetString() ?? "" : "";
            var payload = new Dictionary<string, string>
            {
                ["qr_code"] = qr,
                ["out_trade_no"] = order.OrderId,
                ["order_str"] = canonical + "&sign=" + Uri.EscapeDataString(sign)
            };
            return new PaymentCheckoutResult
            {
                Ok = true,
                Message = "ok",
                ProviderOrderId = order.OrderId,
                CheckoutUrl = qr,
                PayPayload = JsonSerializer.Serialize(payload),
                UsedSandboxSimulator = false
            };
        }
        catch (Exception ex)
        {
            return new PaymentCheckoutResult
            {
                Ok = false,
                Message = "alipay_parse:" + ex.Message,
                UsedSandboxSimulator = false
            };
        }
    }

    public Task<PaymentNotifyResult> ParseAndVerifyNotifyAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        if (_sim is not null)
            return Task.FromResult(_sim.ParseAndVerifyNotify(rawBody));

        var dict = ParseForm(rawBody);
        var fail = new PaymentNotifyResult
        {
            SignatureValid = false,
            ContentType = "text/plain",
            SuccessResponseBody = "success",
            FailureResponseBody = "failure"
        };
        if (!dict.TryGetValue("sign", out var sign) || string.IsNullOrWhiteSpace(sign))
        {
            fail.Error = "no_sign";
            return Task.FromResult(fail);
        }

        string canonical = BuildAlipayCanonical(dict);
        if (_alipayPublic is null || !PaymentCrypto.VerifySha256WithRsaBase64(_alipayPublic, canonical, sign))
        {
            fail.Error = "bad_signature";
            return Task.FromResult(fail);
        }

        string status = dict.GetValueOrDefault("trade_status", "");
        bool paid = status is "TRADE_SUCCESS" or "TRADE_FINISHED";
        string orderId = dict.GetValueOrDefault("out_trade_no", "");
        string txn = dict.GetValueOrDefault("trade_no", "");
        int? amountCents = null;
        if (dict.TryGetValue("total_amount", out var amtStr)
            && decimal.TryParse(amtStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var yuan))
            amountCents = (int)Math.Round(yuan * 100m);

        return Task.FromResult(new PaymentNotifyResult
        {
            SignatureValid = true,
            Paid = paid,
            OrderId = orderId,
            ProviderTxnId = txn,
            AmountCents = amountCents,
            RawTradeState = status,
            ContentType = "text/plain",
            SuccessResponseBody = "success",
            FailureResponseBody = "failure",
            Error = paid ? "" : "not_paid"
        });
    }

    internal static string BuildAlipayCanonical(IReadOnlyDictionary<string, string> dict)
    {
        var sorted = dict
            .Where(kv => !string.Equals(kv.Key, "sign", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(kv.Key, "sign_type", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal);
        return string.Join("&", sorted.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    internal static Dictionary<string, string> ParseForm(string rawBody)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rawBody)) return result;
        // Support JSON for tests as well as form
        string trimmed = rawBody.Trim();
        if (trimmed.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(trimmed);
            foreach (var p in doc.RootElement.EnumerateObject())
                result[p.Name] = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString() ?? ""
                    : p.Value.ToString();
            return result;
        }
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string k = Uri.UnescapeDataString(part[..eq]);
            string v = Uri.UnescapeDataString(part[(eq + 1)..]);
            result[k] = v;
        }
        return result;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _appPrivate?.Dispose();
        _alipayPublic?.Dispose();
        _sim?.Dispose();
        if (_ownHttp) _http.Dispose();
    }
}

public sealed class LocalAlipaySandboxSimulator : IDisposable
{
    private readonly RSA _alipayPrivate; // signs notifies as Alipay platform
    private readonly RSA _alipayPublic;
    private readonly RSA _appPrivate;
    private readonly string _appId;

    public LocalAlipaySandboxSimulator(string? appId = null)
    {
        var platform = PaymentCrypto.GenerateRsa2048();
        _alipayPrivate = platform.Private;
        _alipayPublic = platform.Public;
        var app = PaymentCrypto.GenerateRsa2048();
        _appPrivate = app.Private;
        _appId = string.IsNullOrWhiteSpace(appId) ? "sandbox_sim_app" : appId;
    }

    public string AppId => _appId;
    public string AlipayPublicPem => _alipayPublic.ExportSubjectPublicKeyInfoPem();

    public PaymentCheckoutResult CreateCheckout(PaymentCheckoutRequest order)
    {
        string qr = $"https://sandbox-sim.alipay.local/qr/{order.OrderId}";
        var payload = new Dictionary<string, string>
        {
            ["qr_code"] = qr,
            ["out_trade_no"] = order.OrderId,
            ["mode"] = "sandbox_sim",
            ["order_str"] = $"app_id={_appId}&out_trade_no={order.OrderId}&total_amount={(order.PriceCents / 100.0):0.00}"
        };
        return new PaymentCheckoutResult
        {
            Ok = true,
            Message = "sandbox_sim",
            ProviderOrderId = "alisim_" + order.OrderId,
            CheckoutUrl = qr,
            PayPayload = JsonSerializer.Serialize(payload),
            UsedSandboxSimulator = true
        };
    }

    public string Sign(string canonical) => PaymentCrypto.SignSha256WithRsaBase64(_appPrivate, canonical);
    public bool Verify(string canonical, string signature)
        => PaymentCrypto.VerifySha256WithRsaBase64(_alipayPublic, canonical, signature);

    public (string Body, Dictionary<string, string> Headers) BuildPaidNotify(
        string outTradeNo, string tradeNo, int amountCents, string tradeStatus = "TRADE_SUCCESS")
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = _appId,
            ["out_trade_no"] = outTradeNo,
            ["trade_no"] = tradeNo,
            ["trade_status"] = tradeStatus,
            ["total_amount"] = (amountCents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ["seller_id"] = "2088SIM",
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2"
        };
        string canonical = AlipayPaymentProvider.BuildAlipayCanonical(dict);
        string sign = PaymentCrypto.SignSha256WithRsaBase64(_alipayPrivate, canonical);
        dict["sign"] = sign;
        string body = string.Join("&", dict.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = "application/x-www-form-urlencoded"
        };
        return (body, headers);
    }

    public PaymentNotifyResult ParseAndVerifyNotify(string rawBody)
    {
        var dict = AlipayPaymentProvider.ParseForm(rawBody);
        var fail = new PaymentNotifyResult
        {
            SignatureValid = false,
            ContentType = "text/plain",
            SuccessResponseBody = "success",
            FailureResponseBody = "failure"
        };
        if (!dict.TryGetValue("sign", out var sign))
        {
            fail.Error = "no_sign";
            return fail;
        }
        string canonical = AlipayPaymentProvider.BuildAlipayCanonical(dict);
        if (!PaymentCrypto.VerifySha256WithRsaBase64(_alipayPublic, canonical, sign))
        {
            fail.Error = "bad_signature";
            return fail;
        }
        string status = dict.GetValueOrDefault("trade_status", "");
        bool paid = status is "TRADE_SUCCESS" or "TRADE_FINISHED";
        int? amountCents = null;
        if (dict.TryGetValue("total_amount", out var amtStr)
            && decimal.TryParse(amtStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var yuan))
            amountCents = (int)Math.Round(yuan * 100m);

        return new PaymentNotifyResult
        {
            SignatureValid = true,
            Paid = paid,
            OrderId = dict.GetValueOrDefault("out_trade_no", ""),
            ProviderTxnId = dict.GetValueOrDefault("trade_no", ""),
            AmountCents = amountCents,
            RawTradeState = status,
            ContentType = "text/plain",
            SuccessResponseBody = "success",
            FailureResponseBody = "failure",
            Error = paid ? "" : "not_paid"
        };
    }

    public void Dispose()
    {
        _alipayPrivate.Dispose();
        _alipayPublic.Dispose();
        _appPrivate.Dispose();
    }
}
