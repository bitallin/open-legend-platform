using System.Security.Cryptography;
using System.Text;
using Legend.CliClient;
using Legend.Contracts;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: phase-e-payment-seed <host> <port> <username> [--stay] [--pay-secret SECRET] [--count N]");
    return 2;
}

string host = args[0];
int port = int.Parse(args[1]);
string user = args[2];
bool stay = args.Any(a => a == "--stay");
string secret = "legend-mock-pay-secret";
int count = 3;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--pay-secret" && i + 1 < args.Length) secret = args[++i];
    else if (args[i] == "--count" && i + 1 < args.Length) count = int.Parse(args[++i]);
}

await using var c = new LegendTcpClient();
await c.ConnectAsync(host, port);
if (!(await c.HandshakeAsync(ProtocolConstants.CurrentVersion, "pc-e-seed")).Accepted)
    return 3;
await c.RegisterAsync(user, "secret12345", "PC");
var login = await c.LoginAsync(user, "secret12345", "PC");
if (!login.Success) { Console.Error.WriteLine(login.Message); return 4; }
string charName = "PhaseEHero";
var create = await c.CreateCharAsync(charName, "warrior");
string charId = create.Success
    ? create.Character.CharacterId
    : (await c.CharListAsync()).Characters.First(x => x.Name == charName).CharacterId;
await c.SelectCharAsync(charId);
var realms = await c.GetRealmListAsync();
var enter = await c.EnterWorldAsync(realms.Realms[0].RealmId, charId);
if (!enter.Success) { Console.Error.WriteLine(enter.Message); return 5; }

Console.WriteLine($"OK account={login.AccountId} char={charId} name={charName} gold={enter.Gold}");

var orderIds = new List<string>();
for (int i = 1; i <= count; i++)
{
    var order = await c.PaymentCreateOrderAsync("gold_100");
    if (!order.Success) { Console.Error.WriteLine(order.Message); return 6; }
    orderIds.Add(order.OrderId);
    Console.WriteLine($"ORDER{i}={order.OrderId}");
    Console.WriteLine($"PROVIDER{i}={order.ProviderId}");
    Console.WriteLine($"PRICE{i}={order.PriceCents}");
}

string txn = $"seed-txn-{Guid.NewGuid():N}";
string status = "paid";
string canonical = $"{orderIds[0]}|{txn}|{status}";
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
string sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
Console.WriteLine($"MOCK_ORDER={orderIds[0]}");
Console.WriteLine($"MOCK_TXN={txn}");
Console.WriteLine($"MOCK_STATUS={status}");
Console.WriteLine($"MOCK_SIG={sig}");
Console.Out.Flush();

if (stay)
{
    var deadline = DateTime.UtcNow.AddSeconds(90);
    int got = 0;
    while (DateTime.UtcNow < deadline && got < count)
    {
        try
        {
            var n = await c.WaitPaymentFulfilledAsync(500);
            Console.WriteLine($"FULFILLED={n.OrderId}");
            Console.Out.Flush();
            got++;
        }
        catch (TimeoutException) { /* retry */ }
    }
    for (int i = 0; i < orderIds.Count; i++)
    {
        var st = await c.PaymentOrderStatusAsync(orderIds[i]);
        Console.WriteLine($"STATUS{i + 1}={st.Status}|{st.FulfilledMailId}");
    }
    Console.Out.Flush();
}
return 0;
