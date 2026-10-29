using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legend.CliClient;
using Legend.Contracts;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: cd-phase-seed <host> <port> <username> [--stay] [--pay-secret SECRET]");
    return 2;
}

string host = args[0];
int port = int.Parse(args[1]);
string user = args[2];
bool stay = args.Any(a => a == "--stay");
string secret = "legend-mock-pay-secret";
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--pay-secret" && i + 1 < args.Length)
        secret = args[++i];
}

await using var c = new LegendTcpClient();
await c.ConnectAsync(host, port);
if (!(await c.HandshakeAsync(ProtocolConstants.CurrentVersion, "pc-cd-seed")).Accepted)
    return 3;
await c.RegisterAsync(user, "secret12345", "PC");
var login = await c.LoginAsync(user, "secret12345", "PC");
if (!login.Success)
{
    Console.Error.WriteLine(login.Message);
    return 4;
}
string charName = "CdVerify";
var create = await c.CreateCharAsync(charName, "warrior");
string charId = create.Success
    ? create.Character.CharacterId
    : (await c.CharListAsync()).Characters.First(x => x.Name == charName).CharacterId;
await c.SelectCharAsync(charId);
var realms = await c.GetRealmListAsync();
Console.WriteLine($"REALMS={realms.Realms.Count}");
foreach (var r in realms.Realms)
    Console.WriteLine($"REALM {r.RealmId} {r.RealmName} {r.Host}:{r.Port}");
var enter = await c.EnterWorldAsync(realms.Realms[0].RealmId, charId);
if (!enter.Success)
{
    Console.Error.WriteLine(enter.Message);
    return 5;
}

var order = await c.PaymentCreateOrderAsync("gold_100");
if (!order.Success)
{
    Console.Error.WriteLine(order.Message);
    return 6;
}

string txn = $"seed-txn-{Guid.NewGuid():N}";
string status = "paid";
string canonical = $"{order.OrderId}|{txn}|{status}";
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
string sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

Console.WriteLine($"OK account={login.AccountId} char={charId} name={charName}");
Console.WriteLine($"ORDER={order.OrderId}");
Console.WriteLine($"TXN={txn}");
Console.WriteLine($"SIG={sig}");
Console.WriteLine($"STATUS={status}");
Console.Out.Flush();

if (stay)
{
    using var exit = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => exit.Set();
    exit.Wait();
}
return 0;
