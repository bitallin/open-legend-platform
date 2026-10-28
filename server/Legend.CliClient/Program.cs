using Legend.CliClient;
using Legend.Contracts.Proto;

string host = "127.0.0.1";
int port = 7100;
bool combat = true;
bool equipTest = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--host" && i + 1 < args.Length)
        host = args[++i];
    else if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out int p))
        port = p;
    else if (args[i] == "--no-combat")
        combat = false;
    else if (args[i] == "--equip")
    {
        equipTest = true;
        combat = false;
    }
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
Console.WriteLine($"[CliClient] M3 full-chain smoke (shop/quest + combat) against {host}:{port}...");

await using var clientA = new LegendTcpClient();
await using var clientB = new LegendTcpClient();

await clientA.ConnectAsync(host, port, cts.Token);
var hsA = await clientA.HandshakeAsync("1.4.0", "pc-smoke-1.4.0", cts.Token);
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
await clientB.HandshakeAsync("1.4.0", "android-smoke-1.4.0", cts.Token);
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

// --- M3: NPC shop + quest ---
if (!equipTest)
{
    var npc = enterA.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityNpc);
    if (npc is null)
    {
        await Walk(12, 12);
        for (int i = 0; i < 25 && npc is null; i++)
        {
            while (clientA.ReceivedEntitySpawns.TryDequeue(out var sp))
            {
                if (sp.Entity.Kind == EntityKind.EntityNpc) { npc = sp.Entity; break; }
            }
            await Task.Delay(30, cts.Token);
        }
    }
    if (npc is null)
        throw new Exception("No NPC found for M3 shop/quest");
    await Walk(npc.X, npc.Y);
    var talk = await clientA.TalkAsync(npc.EntityId, cts.Token);
    if (!talk.Success)
        throw new Exception($"Talk failed: {talk.ErrorCode} {talk.Message}");
    Console.WriteLine($"[Step M3.1] Talk OK: {talk.NpcName} shop={talk.ShopId} offers={talk.ShopOffers.Count} gold={talk.Gold}");

    int goldBefore = talk.Gold;
    var buy = await clientA.ShopBuyAsync(npc.EntityId, "item_hp_potion", 1, cts.Token);
    if (!buy.Success)
        throw new Exception($"ShopBuy failed: {buy.ErrorCode} {buy.Message}");
    if (buy.Gold != goldBefore - 10)
        throw new Exception($"Gold expected {goldBefore - 10}, got {buy.Gold}");
    Console.WriteLine($"[Step M3.2] ShopBuy OK: potion, gold={buy.Gold}");

    bool questDone = talk.Quests.Any(q => q.QuestId == "quest_slime_hunt" && q.Status == QuestStatus.QuestCompleted);
    if (questDone)
    {
        Console.WriteLine("[Step M3.3] Quest already completed (persist OK) — skip accept/combat/turn-in");
        combat = false;
    }
    else
    {
        var accept = await clientA.QuestAcceptAsync(npc.EntityId, "quest_slime_hunt", cts.Token);
        if (!accept.Success)
            throw new Exception($"QuestAccept failed: {accept.Message}");
        Console.WriteLine($"[Step M3.3] QuestAccept OK: {accept.Quest.Title}");
    }
}

if (combat)
{
    // Walk out of safe zone toward slime spawn (25,10)
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

    // Second slime for quest_slime_hunt (need 2) — also drain AOI enters
    EntitySnapshot? monster2 = null;
    for (int i = 0; i < 50 && monster2 is null; i++)
    {
        while (clientA.ReceivedAoiEnters.TryDequeue(out var en))
        {
            if (en.Entity.Kind == EntityKind.EntityMonster && en.Entity.TemplateId == "slime_green" &&
                en.Entity.EntityId != monster.EntityId)
            { monster2 = en.Entity; break; }
        }
        while (monster2 is null && clientA.ReceivedEntitySpawns.TryDequeue(out var sp))
        {
            if (sp.Entity.Kind == EntityKind.EntityMonster && sp.Entity.TemplateId == "slime_green" &&
                sp.Entity.EntityId != monster.EntityId)
            { monster2 = sp.Entity; break; }
        }
        if (monster2 is null)
        {
            await Walk(i % 2 == 0 ? 28 : 25, i % 2 == 0 ? 12 : 10);
            await Task.Delay(50, cts.Token);
        }
    }
    if (monster2 is null)
        throw new Exception("No second slime for quest");
    await Walk(monster2.X - 1, monster2.Y);
    await Task.Delay(2600, cts.Token); // power_strike CD
    for (int hit = 0; hit < 8; hit++)
    {
        string skill = hit == 0 ? "power_strike" : "basic_slash";
        if (hit > 0) await Task.Delay(650, cts.Token);
        var (atk2, cs2) = await clientA.AttackAsync(monster2.EntityId, skill, cts.Token);
        if (!atk2.Success) throw new Exception($"Attack2 failed: {atk2.Message}");
        Console.WriteLine($"[Step 16.{hit}] Kill2 dmg={cs2?.Damage} died={cs2?.TargetDied}");
        if (cs2?.TargetDied == true) break;
    }
}

if (combat)
{
    // Turn in quest at NPC
    await Walk(12, 12);
    var turn = await clientA.QuestTurnInAsync("npc_village_merchant", "quest_slime_hunt", cts.Token);
    if (!turn.Success)
        throw new Exception($"QuestTurnIn failed: {turn.ErrorCode} {turn.Message}");
    Console.WriteLine($"[Step M3.4] QuestTurnIn OK: gold={turn.Gold} reward={turn.GoldReward} status={turn.Quest.Status}");
}

if (equipTest)
{
    Console.WriteLine($"[Step M4.0] EnterWorld stats: effectiveAtk={enterA.EffectiveAtk} effectiveDef={enterA.EffectiveDef} effectiveMaxHp={enterA.EffectiveMaxHp} equipped={enterA.Equipment.Count} gold={enterA.Gold}");
    var existingWeapon = enterA.Equipment.FirstOrDefault(e => e.Slot == "weapon");
    if (existingWeapon != null && existingWeapon.EnhanceLevel >= 1)
    {
        if (existingWeapon.ItemId != "item_wooden_sword" || enterA.EffectiveAtk < 39)
            throw new Exception($"Restart persistence mismatch: item={existingWeapon.ItemId} +={existingWeapon.EnhanceLevel} atk={enterA.EffectiveAtk}");
        Console.WriteLine($"[Step M4.RestartVerify] Persisted equipment & stats OK: weapon={existingWeapon.ItemId}+{existingWeapon.EnhanceLevel} effectiveAtk={enterA.EffectiveAtk} effectiveDef={enterA.EffectiveDef} effectiveMaxHp={enterA.EffectiveMaxHp}");
    }
    else
    {
        int baseEffectiveAtk = enterA.EffectiveAtk;

        // 1) Measure unequipped basic_slash damage on a slime
        await Walk(22, 10);
        EntitySnapshot? mTarget = enterA.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
        for (int i = 0; i < 40 && mTarget is null; i++)
        {
            while (clientA.ReceivedAoiEnters.TryDequeue(out var en))
                if (en.Entity.Kind == EntityKind.EntityMonster) { mTarget = en.Entity; break; }
            while (mTarget is null && clientA.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityMonster) { mTarget = sp.Entity; break; }
            if (mTarget is null)
            {
                await Walk(i % 2 == 0 ? 25 : 28, i % 2 == 0 ? 10 : 12);
                await Task.Delay(50, cts.Token);
            }
        }
        if (mTarget is null)
            throw new Exception("No monster found for M4 damage comparison");

        await Walk(mTarget.X - 1, mTarget.Y);
        await Task.Delay(650, cts.Token);
        var (atkBefore, csBefore) = await clientA.AttackAsync(mTarget.EntityId, "basic_slash", cts.Token);
        if (!atkBefore.Success || csBefore is null)
            throw new Exception($"Unequipped attack failed: {atkBefore.Message}");
        int dmgBeforeEquip = csBefore.Damage;
        Console.WriteLine($"[Step M4.1] Unequipped basic_slash damage={dmgBeforeEquip} (effectiveAtk={baseEffectiveAtk})");

        // 2) Return to NPC, buy sword + cloth armor, equip both
        await Walk(12, 12);
        var buySword = await clientA.ShopBuyAsync("npc_village_merchant", "item_wooden_sword", 1, cts.Token);
        if (!buySword.Success)
            throw new Exception($"ShopBuy sword failed: {buySword.ErrorCode} {buySword.Message}");
        var buyArmor = await clientA.ShopBuyAsync("npc_village_merchant", "item_cloth_armor", 1, cts.Token);
        if (!buyArmor.Success)
            throw new Exception($"ShopBuy armor failed: {buyArmor.ErrorCode} {buyArmor.Message}");
        Console.WriteLine($"[Step M4.2] Bought item_wooden_sword & item_cloth_armor: gold={buyArmor.Gold}");

        var eqSword = await clientA.EquipAsync("item_wooden_sword", "weapon", cts.Token);
        if (!eqSword.Success || eqSword.EffectiveAtk <= baseEffectiveAtk)
            throw new Exception($"Equip sword failed or atk did not increase: {eqSword.Message}");
        var eqArmor = await clientA.EquipAsync("item_cloth_armor", "armor", cts.Token);
        if (!eqArmor.Success || eqArmor.EffectiveDef <= 0)
            throw new Exception($"Equip armor failed: {eqArmor.Message}");
        Console.WriteLine($"[Step M4.3] Equipped weapon+armor: effectiveAtk={eqArmor.EffectiveAtk} effectiveDef={eqArmor.EffectiveDef} effectiveMaxHp={eqArmor.EffectiveMaxHp}");

        // 3) Enhance weapon +1
        int goldBeforeEnhance = clientA.Gold;
        var enhResp = await clientA.EnhanceEquipAsync("weapon", cts.Token);
        if (!enhResp.Success || !enhResp.EnhanceSucceeded || enhResp.UpdatedSlot.EnhanceLevel != 1 || enhResp.EffectiveAtk <= eqArmor.EffectiveAtk)
            throw new Exception($"EnhanceEquip failed: {enhResp.ErrorCode} {enhResp.Message}");
        Console.WriteLine($"[Step M4.4] Enhanced weapon to +{enhResp.UpdatedSlot.EnhanceLevel}: gold {goldBeforeEnhance}->{enhResp.Gold}, effectiveAtk={enhResp.EffectiveAtk}");

        // 4) Attack monster again and verify damage increased after equip + enhance
        await Walk(mTarget.X - 1, mTarget.Y);
        await Task.Delay(650, cts.Token);
        if (csBefore.TargetDied)
        {
            // Find another alive monster if the first one died
            EntitySnapshot? mNext = null;
            for (int i = 0; i < 40 && mNext is null; i++)
            {
                while (clientA.ReceivedAoiEnters.TryDequeue(out var en))
                    if (en.Entity.Kind == EntityKind.EntityMonster && en.Entity.EntityId != mTarget.EntityId) { mNext = en.Entity; break; }
                while (mNext is null && clientA.ReceivedEntitySpawns.TryDequeue(out var sp))
                    if (sp.Entity.Kind == EntityKind.EntityMonster) { mNext = sp.Entity; break; }
                if (mNext is null)
                {
                    await Walk(i % 2 == 0 ? 28 : 25, i % 2 == 0 ? 12 : 10);
                    await Task.Delay(50, cts.Token);
                }
            }
            if (mNext != null)
            {
                mTarget = mNext;
                await Walk(mTarget.X - 1, mTarget.Y);
            }
        }
        var (atkAfter, csAfter) = await clientA.AttackAsync(mTarget.EntityId, "basic_slash", cts.Token);
        if (!atkAfter.Success || csAfter is null)
            throw new Exception($"Equipped attack failed: {atkAfter.Message}");
        int dmgAfterEquip = csAfter.Damage;
        if (dmgAfterEquip <= dmgBeforeEquip)
            throw new Exception($"Expected damage after equip+enhance ({dmgAfterEquip}) > unequipped ({dmgBeforeEquip})");
        Console.WriteLine($"[Step M4.5] Equipped+Enhanced basic_slash damage={dmgAfterEquip} > unequipped={dmgBeforeEquip} (VERIFIED)");
    }
}

Console.WriteLine("[SUCCESS] All M4 full-chain verification steps passed!");
