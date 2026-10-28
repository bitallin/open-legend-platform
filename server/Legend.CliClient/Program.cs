using Legend.CliClient;

string host = "127.0.0.1";
int port = 7100;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--host" && i + 1 < args.Length)
        host = args[++i];
    else if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out int p))
        port = p;
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

Console.WriteLine($"[CliClient] Starting M0 full-chain smoke test against {host}:{port}...");

await using var clientA = new LegendTcpClient();
await using var clientB = new LegendTcpClient();

// 1. Connect & Handshake Client A (PC)
await clientA.ConnectAsync(host, port, cts.Token);
var hsA = await clientA.HandshakeAsync("1.0.0", "pc-smoke-1.0.0", cts.Token);
if (!hsA.Accepted)
    throw new Exception($"Client A Handshake failed: {hsA.Message}");
Console.WriteLine($"[Step 1] Client A Handshake OK (ServerVersion={hsA.ServerProtocolVersion}, Nonce={hsA.SessionNonce})");

// 2. Login Client A
var loginA = await clientA.LoginAsync("hero_pc", "pass123", "PC", cts.Token);
if (!loginA.Success)
    throw new Exception($"Client A Login failed: {loginA.Message}");
Console.WriteLine($"[Step 2] Client A Login OK (AccountId={loginA.AccountId}, Token={loginA.SessionToken[..16]}...)");

// 3. RealmList Client A
var realms = await clientA.GetRealmListAsync(cts.Token);
if (!realms.Success || realms.Realms.Count == 0)
    throw new Exception("Client A failed to fetch RealmList.");
var targetRealm = realms.Realms[0];
Console.WriteLine($"[Step 3] RealmList OK: Realm='{targetRealm.RealmName}' ({targetRealm.RealmId}), VersionPack={targetRealm.VersionPackId}");

// 4. EnterWorld Client A & verify Lua welcome hook
var enterA = await clientA.EnterWorldAsync(targetRealm.RealmId, "烈焰武者", "warrior", cts.Token);
if (!enterA.Success)
    throw new Exception($"Client A EnterWorld failed: {enterA.Message}");
Console.WriteLine($"[Step 4] Client A EnterWorld OK: Entity={enterA.SelfEntity.EntityId}, Pos=({enterA.SelfEntity.X},{enterA.SelfEntity.Y}), HP={enterA.SelfEntity.Hp}");

// Wait briefly for Lua welcome notice pushed asynchronously
for (int i = 0; i < 20 && clientA.ReceivedServerNotices.IsEmpty; i++)
    await Task.Delay(25, cts.Token);

if (!clientA.ReceivedServerNotices.TryDequeue(out var luaNotice))
    throw new Exception("Client A did not receive Lua welcome ServerNotice!");
Console.WriteLine($"[Step 5] Lua Sandbox Hook OK: [{luaNotice.NoticeType}] {luaNotice.Title} - {luaNotice.Content} (Granted: {luaNotice.GrantedItemId} x{luaNotice.GrantedItemCount})");

// 5. Connect & EnterWorld Client B (Android) into same map to verify same-screen AOI
await clientB.ConnectAsync(host, port, cts.Token);
await clientB.HandshakeAsync("1.0.0", "android-smoke-1.0.0", cts.Token);
await clientB.LoginAsync("hero_mobile", "pass456", "Android", cts.Token);
var enterB = await clientB.EnterWorldAsync(targetRealm.RealmId, "苍穹术士", "mage", cts.Token);
if (!enterB.Success)
    throw new Exception($"Client B EnterWorld failed: {enterB.Message}");
Console.WriteLine($"[Step 6] Client B (Android) EnterWorld OK: NearbyEntitiesCount={enterB.NearbyEntities.Count}");

for (int i = 0; i < 20 && clientA.ReceivedAoiEnters.IsEmpty; i++)
    await Task.Delay(25, cts.Token);

if (!clientA.ReceivedAoiEnters.TryDequeue(out var bEnterOnA))
    throw new Exception("Client A did not receive EntityEnterAoi when Client B spawned nearby!");
Console.WriteLine($"[Step 7] AOI Spawn Broadcast OK: Client A saw '{bEnterOnA.Entity.Name}' ({bEnterOnA.Entity.EntityId}) enter AOI at ({bEnterOnA.Entity.X},{bEnterOnA.Entity.Y})");

// 6. Client A moves from (10,10) -> (11,10), verify Client B receives MoveSync broadcast
var moveA = await clientA.MoveAsync(11, 10, direction: 2, cts.Token);
if (!moveA.Accepted)
    throw new Exception($"Client A Move failed: {moveA.Reason}");

for (int i = 0; i < 20 && clientB.ReceivedMoveSyncs.IsEmpty; i++)
    await Task.Delay(25, cts.Token);

if (!clientB.ReceivedMoveSyncs.TryDequeue(out var moveOnB))
    throw new Exception("Client B did not receive AOI MoveSync when Client A moved!");
Console.WriteLine($"[Step 8] AOI Movement Broadcast OK: Client B saw '{moveOnB.EntityId}' move ({moveOnB.FromX},{moveOnB.FromY}) -> ({moveOnB.ToX},{moveOnB.ToY})");

// 7. Heartbeat Ping/Pong
var pong = await clientA.PingAsync(cts.Token);
Console.WriteLine($"[Step 9] Heartbeat Ping/Pong OK: RTT={(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - pong.ClientTimeUnixMs)}ms");

Console.WriteLine("[SUCCESS] All M0 full-chain verification steps passed!");
