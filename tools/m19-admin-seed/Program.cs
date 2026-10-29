using Legend.CliClient;
using Legend.Contracts;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: m19-admin-seed <host> <port> <username> [--stay]");
    return 2;
}

string host = args[0];
int port = int.Parse(args[1]);
string user = args[2];
bool stay = args.Any(a => a == "--stay");

await using var c = new LegendTcpClient();
await c.ConnectAsync(host, port);
if (!(await c.HandshakeAsync(ProtocolConstants.CurrentVersion, "pc-m19-seed")).Accepted)
    return 3;
await c.RegisterAsync(user, "secret12345", "PC");
var login = await c.LoginAsync(user, "secret12345", "PC");
if (!login.Success)
{
    Console.Error.WriteLine(login.Message);
    return 4;
}
var create = await c.CreateCharAsync("M19Curl", "warrior");
string charId = create.Success
    ? create.Character.CharacterId
    : (await c.CharListAsync()).Characters.First(x => x.Name == "M19Curl").CharacterId;
await c.SelectCharAsync(charId);
var realms = await c.GetRealmListAsync();
var enter = await c.EnterWorldAsync(realms.Realms[0].RealmId, charId);
if (!enter.Success)
{
    Console.Error.WriteLine(enter.Message);
    return 5;
}
Console.WriteLine($"OK account={login.AccountId} char={charId} muted={enter.IsMuted}");
Console.Out.Flush();
if (stay)
{
    using var exit = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => exit.Set();
    exit.Wait();
}
return 0;
