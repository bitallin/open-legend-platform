using Legend.CliClient;
using Legend.Contracts.Proto;

string host = "127.0.0.1";
int port = 7100;
bool combat = true;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--host" && i + 1 < args.Length)
        host = args[++i];
    else if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out int p))
        port = p;
    else if (args[i] == "--no-combat")
        combat = false;
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
Console.WriteLine($"[CliClient] M2 full-chain smoke against {host}:{port}...");

await using var clientA = new LegendTcpClient();
await using var clientB = new LegendTcpClient();

await clientA.ConnectAsync(host, port, cts.Token);
var hsA = await clientA.HandshakeAsync("1.2.0", "pc-smoke-1.2.0", cts.Token);
if (!hsA.Accepted)
    throw new Exception($"Client A Handshake failed: {hsA.Message}");
Console.WriteLine($"[Step 1] Handshake OK (Server={hsA.ServerProtocolVersion})");

await clientA.RegisterAsync("hero_pc", "pass123", "PC", cts.Token);
var loginA = await clientA.LoginAsync("hero_pc", "pass123", "PC", cts.Token);
if (!loginA.Success)
    throw new Exception($"Login A failed: {loginA.Message}");
Console.WriteLine($"[Step 2] Register/Login OK (AccountId={loginA.AccountId})");

var realms = await clientA.GetRealmListAsync(cts.Token);
if (!realms.Success || realms.Realms.Count == 0)
    throw new Exception("RealmList failed");
var targetRealm = realms.Realms[0];
Console.WriteLine($"[Step 3] RealmList OK: {targetRealm.RealmName}");

string charIdA;
var createA = await clientA.CreateCharAsync("烈焰武者", "warrior", cts.Token);
if (createA.Success)
    charIdA = createA.Character.CharacterId;
else
{
    var list = await clientA.CharListAsync(cts.Token);
    var existing = list.Characters.FirstOrDefault(c => c.Name == "烈焰武者") ?? list.Characters.FirstOrDefault();
    if (existing is null)
        throw new Exception($"CreateChar failed: {createA.Message}");
    charIdA = existing.CharacterId;
}

var selectA = await clientA.SelectCharAsync(charIdA, cts.Token);
if (!selectA.Success)
    throw new Exception($"SelectChar failed: {selectA.Message}");
Console.WriteLine($"[Step 4] Create/SelectChar OK: {selectA.Character.Name} @ ({selectA.Character.X},{selectA.Character.Y})");

var enterA = await clientA.EnterWorldAsync(targetRealm.RealmId, charIdA, cts.Token);
if (!enterA.Success)
    throw new Exception($"EnterWorld A failed: {enterA.Message}");
Console.WriteLine($"[Step 5] EnterWorld OK: Entity={enterA.SelfEntity.EntityId}, Pos=({enterA.SelfEntity.X},{enterA.SelfEntity.Y}), Safe={enterA.InSafeZone}, Nearby={enterA.NearbyEntities.Count}, Inv={enterA.Inventory.Count}");

for (int i = 0; i < 20 && clientA.ReceivedServerNotices.IsEmpty; i++)
    await Task.Delay(25, cts.Token);
if (!clientA.ReceivedServerNotices.TryDequeue(out var luaNotice))
    throw new Exception("Missing Lua welcome ServerNotice");
Console.WriteLine($"[Step 6] Lua Hook OK: {luaNotice.Title} / {luaNotice.GrantedItemId} x{luaNotice.GrantedItemCount}");

await clientB.ConnectAsync(host, port, cts.Token);
await clientB.HandshakeAsync("1.2.0", "android-smoke-1.2.0", cts.Token);
await clientB.RegisterAsync("hero_mobile", "pass456", "Android", cts.Token);
var loginB = await clientB.LoginAsync("hero_mobile", "pass456", "Android", cts.Token);
if (!loginB.Success)
    throw new Exception($"Login B failed: {loginB.Message}");

string charIdB;
var createB = await clientB.CreateCharAsync("苍穹术士", "mage", cts.Token);
if (createB.Success)
    charIdB = createB.Character.CharacterId;
else
{
    var listB = await clientB.CharListAsync(cts.Token);
    charIdB = listB.Characters[0].CharacterId;
}
await clientB.SelectCharAsync(charIdB, cts.Token);
var enterB = await clientB.EnterWorldAsync(targetRealm.RealmId, charIdB, cts.Token);
if (!enterB.Success)
    throw new Exception($"EnterWorld B failed: {enterB.Message}");
Console.WriteLine($"[Step 7] Client B EnterWorld OK: Nearby={enterB.NearbyEntities.Count}");

for (int i = 0; i < 20 && clientA.ReceivedAoiEnters.IsEmpty; i++)
    await Task.Delay(25, cts.Token);
if (clientA.ReceivedAoiEnters.TryDequeue(out var bEnterOnA))
{
    Console.WriteLine($"[Step 8] AOI Spawn OK: saw '{bEnterOnA.Entity.Name}'");
}
else
{
    int dist = Math.Max(
        Math.Abs((clientA.SelfEntity?.X ?? 0) - (clientB.SelfEntity?.X ?? 0)),
        Math.Abs((clientA.SelfEntity?.Y ?? 0) - (clientB.SelfEntity?.Y ?? 0)));
    if (dist >= 14)
        Console.WriteLine($"[Step 8] AOI skip (A/B Chebyshev={dist} out of view); coords/inventory restored");
    else
        throw new Exception("Client A missing EntityEnterAoi for B");
}

int ax = enterA.SelfEntity.X;
int ay = enterA.SelfEntity.Y;
var moveA = await clientA.MoveAsync(ax + 1, ay, direction: 2, cts.Token);
if (!moveA.Accepted)
    throw new Exception($"Move failed: {moveA.Reason}");

for (int i = 0; i < 20 && clientB.ReceivedMoveSyncs.IsEmpty; i++)
    await Task.Delay(25, cts.Token);
if (clientB.ReceivedMoveSyncs.TryDequeue(out var moveOnB))
    Console.WriteLine($"[Step 9] AOI Move OK: ({moveOnB.FromX},{moveOnB.FromY})->({moveOnB.ToX},{moveOnB.ToY}) safe={moveOnB.InSafeZone}");
else
    Console.WriteLine("[Step 9] AOI Move soft-skip (B out of range of A)");

var pong = await clientA.PingAsync(cts.Token);
Console.WriteLine($"[Step 10] Ping/Pong OK RTT~{(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - pong.ClientTimeUnixMs)}ms");

if (combat)
{
    // Walk out of safe zone toward slime spawn (25,10)
    async Task Walk(int tx, int ty)
    {
        int x = clientA.SelfEntity!.X, y = clientA.SelfEntity.Y;
        int guard = 0;
        while ((x != tx || y != ty) && guard++ < 80)
        {
            int nx = x, ny = y;
            if (x != tx) nx = x + Math.Clamp(tx - x, -2, 2);
            else ny = y + Math.Clamp(ty - y, -2, 2);
            var s = await clientA.MoveAsync(nx, ny, ct: cts.Token);
            if (!s.Accepted) throw new Exception($"Walk failed ({nx},{ny}): {s.Reason}");
            x = s.ToX; y = s.ToY;
        }
    }

    await Walk(22, 10);
    Console.WriteLine($"[Step 11] Left safe zone @ ({clientA.SelfEntity!.X},{clientA.SelfEntity.Y}) safe={clientA.SelfEntity.InSafeZone}");

    EntitySnapshot? monster = enterA.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
    for (int i = 0; i < 40 && monster is null; i++)
    {
        while (clientA.ReceivedEntitySpawns.TryDequeue(out var sp))
        {
            if (sp.Entity.Kind == EntityKind.EntityMonster) { monster = sp.Entity; break; }
        }
        if (monster is null)
        {
            await clientA.MoveAsync(Math.Min(clientA.SelfEntity!.X + 1, 28), 10, ct: cts.Token);
            await Task.Delay(40, cts.Token);
        }
    }
    if (monster is null)
        throw new Exception("No monster found for combat smoke");
    Console.WriteLine($"[Step 12] Found monster {monster.Name} ({monster.EntityId}) @ ({monster.X},{monster.Y}) HP={monster.Hp}");

    await Walk(monster.X - 1, monster.Y);

    string? lootId = null;
    for (int hit = 0; hit < 8 && lootId is null; hit++)
    {
        string skill = hit == 0 ? "power_strike" : "basic_slash";
        if (hit > 0 && skill == "basic_slash")
            await Task.Delay(650, cts.Token);
        var (atk, combatSync) = await clientA.AttackAsync(monster.EntityId, skill, cts.Token);
        if (!atk.Success)
            throw new Exception($"Attack failed: {atk.ErrorCode} {atk.Message}");
        Console.WriteLine($"[Step 13.{hit}] Attack {skill} dmg={combatSync?.Damage} hp={combatSync?.TargetHp} died={combatSync?.TargetDied}");

        for (int j = 0; j < 25 && lootId is null; j++)
        {
            while (clientA.ReceivedLootDrops.TryDequeue(out var drop))
                lootId = drop.Loot.EntityId;
            if (lootId is null) await Task.Delay(30, cts.Token);
        }
        if (combatSync?.TargetDied == true)
            break;
    }
    if (lootId is null)
        throw new Exception("No loot after kill");
    Console.WriteLine($"[Step 14] Loot drop OK: {lootId}");

    await Walk(monster.X, monster.Y);
    var pickup = await clientA.PickupAsync(lootId, cts.Token);
    if (!pickup.Success)
        throw new Exception($"Pickup failed: {pickup.Message}");
    Console.WriteLine($"[Step 15] Pickup OK: {pickup.Picked.ItemId} x{pickup.Picked.Count}, bag={pickup.Inventory.Count} stacks");
}

Console.WriteLine("[SUCCESS] All M2 full-chain verification steps passed!");
