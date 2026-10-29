using System.Text.Json;
using Legend.Contracts.Payment;

// CLI: generate signed WeChat / Alipay sandbox notify payloads for local Admin HTTP.
// Usage:
//   payment-sandbox-sim wechat --order <id> --txn <id> --amount 100 [--key-out /tmp/wx.json]
//   payment-sandbox-sim alipay --order <id> --txn <id> --amount 100
//
// When keying against a live Host, the Host's simulator keys are ephemeral per process.
// Prefer the Host-integrated path used by verify-phase-e-payment.sh / integration tests.
// This tool is for offline payload inspection and docs demos.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Legend payment sandbox simulator
          wechat --order <out_trade_no> --txn <transaction_id> --amount <cents>
          alipay --order <out_trade_no> --txn <trade_no> --amount <cents>
        Emits JSON: { body, headers, mode: sandbox_sim }
        Note: Host uses in-process simulator keys; for E2E use verify-phase-e-payment.sh.
        """);
    return 0;
}

string mode = args[0].ToLowerInvariant();
string order = "";
string txn = "";
int amount = 100;
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--order" && i + 1 < args.Length) order = args[++i];
    else if (args[i] == "--txn" && i + 1 < args.Length) txn = args[++i];
    else if (args[i] == "--amount" && i + 1 < args.Length) amount = int.Parse(args[++i]);
}
if (string.IsNullOrWhiteSpace(order)) order = Guid.NewGuid().ToString("N");
if (string.IsNullOrWhiteSpace(txn)) txn = "sim-" + Guid.NewGuid().ToString("N");

object payload;
if (mode == "wechat")
{
    using var sim = new LocalWechatSandboxSimulator();
    var (body, headers) = sim.BuildPaidNotify(order, txn, amount);
    payload = new
    {
        mode = "sandbox_sim",
        provider = "wechat",
        body,
        headers,
        platformSerial = sim.PlatformSerial,
        apiV3KeyHint = "(ephemeral; Host uses its own sim keys)"
    };
}
else if (mode == "alipay")
{
    using var sim = new LocalAlipaySandboxSimulator();
    var (body, headers) = sim.BuildPaidNotify(order, txn, amount);
    payload = new
    {
        mode = "sandbox_sim",
        provider = "alipay",
        body,
        headers,
        appId = sim.AppId
    };
}
else
{
    Console.Error.WriteLine("unknown mode: " + mode);
    return 1;
}

Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
return 0;
