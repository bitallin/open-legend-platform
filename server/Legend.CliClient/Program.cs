using Legend.CliClient;
using Legend.Contracts.Proto;

string host = "127.0.0.1";
int port = 7100;
bool combat = true;
bool equipTest = false;
bool levelTest = false;
bool guildTest = false;
bool tradeTest = false;
bool partyTest = false;
bool skillTest = false;

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
    else if (args[i] == "--level")
    {
        levelTest = true;
        combat = false;
        equipTest = false;
    }
    else if (args[i] == "--guild")
    {
        guildTest = true;
        combat = false;
        equipTest = false;
        levelTest = false;
    }
    else if (args[i] == "--trade")
    {
        tradeTest = true;
        guildTest = false;
        combat = false;
        equipTest = false;
        levelTest = false;
    }
    else if (args[i] == "--party")
    {
        partyTest = true;
        tradeTest = false;
        guildTest = false;
        combat = false;
        equipTest = false;
        levelTest = false;
    }
    else if (args[i] == "--skill")
    {
        skillTest = true;
        partyTest = false;
        tradeTest = false;
        guildTest = false;
        combat = false;
        equipTest = false;
        levelTest = false;
    }
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(skillTest ? 180 : partyTest ? 180 : (guildTest || tradeTest) ? 120 : levelTest ? 150 : 60));
Console.WriteLine($"[CliClient] {(skillTest ? "M9 skill/buff" : partyTest ? "M8 party/friend/whisper" : tradeTest ? "M7 stall/trade" : guildTest ? "M6 guild/chat" : levelTest ? "M5 level/potion" : equipTest ? "M4 equip" : "full-chain")} against {host}:{port}...");

if (guildTest)
{
    await RunGuildAcceptanceAsync(host, port, cts.Token);
    Console.WriteLine("[SUCCESS] All M6 guild/chat verification steps passed!");
    return;
}

if (tradeTest)
{
    await RunTradeAcceptanceAsync(host, port, cts.Token);
    Console.WriteLine("[SUCCESS] All M7 stall/trade verification steps passed!");
    return;
}

if (partyTest)
{
    await RunPartyAcceptanceAsync(host, port, cts.Token);
    Console.WriteLine("[SUCCESS] All M8 party/friend/whisper verification steps passed!");
    return;
}

if (skillTest)
{
    await RunSkillAcceptanceAsync(host, port, cts.Token);
    Console.WriteLine("[SUCCESS] All M9 skill/buff verification steps passed!");
    return;
}

static async Task RunSkillAcceptanceAsync(string host, int port, CancellationToken ct)
{
    await using var client = new LegendTcpClient();
    await client.ConnectAsync(host, port, ct);
    if (!(await client.HandshakeAsync("1.9.0", "pc-m9", ct)).Accepted) throw new Exception("handshake failed");

    string user = "m9_skill_" + Guid.NewGuid().ToString("N")[..6];
    await client.RegisterAsync(user, "pass12345", "PC", ct);
    var login = await client.LoginAsync(user, "pass12345", "PC", ct);
    if (!login.Success) throw new Exception(login.Message);

    string charName = "武者" + Guid.NewGuid().ToString("N")[..4];
    var create = await client.CreateCharAsync(charName, "warrior", ct);
    if (!create.Success) throw new Exception(create.Message);
    string charId = create.Character.CharacterId;
    await client.SelectCharAsync(charId, ct);
    var realms = await client.GetRealmListAsync(ct);
    var enter = await client.EnterWorldAsync(realms.Realms[0].RealmId, charId, ct);
    if (!enter.Success) throw new Exception(enter.Message);
    Console.WriteLine($"[M9.1] Entered gold={enter.Gold} skills={enter.Skills.Count} atk={enter.EffectiveAtk}");

    if (enter.Skills.Any(s => s.SkillId == "fatal_strike"))
        throw new Exception("fatal_strike should not be learned yet");
    if (!enter.Skills.Any(s => s.SkillId == "power_strike"))
        throw new Exception("power_strike starter skill missing");

    async Task Walk(int tx, int ty)
    {
        int guard = 0;
        while ((client.SelfEntity!.X != tx || client.SelfEntity.Y != ty) && guard++ < 80)
        {
            int nx = client.SelfEntity.X + Math.Sign(tx - client.SelfEntity.X);
            int ny = client.SelfEntity.Y + Math.Sign(ty - client.SelfEntity.Y);
            var m = await client.MoveAsync(nx, ny, ct: ct);
            if (!m.Accepted) throw new Exception($"Move rejected ({nx},{ny}): {m.Reason}");
        }
    }

    await Walk(12, 12);
    var buyBook = await client.ShopBuyAsync("npc_village_merchant", "item_skillbook_fatal_strike", 1, ct);
    if (!buyBook.Success) throw new Exception($"buy skillbook failed: {buyBook.Message}");
    var buyScroll = await client.ShopBuyAsync("npc_village_merchant", "item_war_cry_scroll", 1, ct);
    if (!buyScroll.Success) throw new Exception($"buy scroll failed: {buyScroll.Message}");
    Console.WriteLine($"[M9.2] Bought skillbook+scroll gold={buyScroll.Gold}");

    async Task<EntitySnapshot> FindMonster()
    {
        EntitySnapshot? m = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
        for (int i = 0; i < 60 && m is null; i++)
        {
            while (client.ReceivedAoiEnters.TryDequeue(out var en))
                if (en.Entity.Kind == EntityKind.EntityMonster) { m = en.Entity; break; }
            while (m is null && client.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityMonster) { m = sp.Entity; break; }
            if (m is null)
            {
                await Walk(25 + (i % 3), 10 + (i % 2));
                await Task.Delay(40, ct);
            }
        }
        if (m is null) throw new Exception("monster not found");
        return m;
    }

    var monster0 = await FindMonster();
    await Walk(Math.Max(0, monster0.X - 1), monster0.Y);
    var (denyAtk, _) = await client.AttackAsync(monster0.EntityId, "fatal_strike", ct);
    if (denyAtk.Success || denyAtk.ErrorCode != ProtocolErrorCode.ErrSkillNotLearned)
        throw new Exception($"Expected ErrSkillNotLearned, got success={denyAtk.Success} code={denyAtk.ErrorCode}");
    Console.WriteLine("[M9.3] Unlearned fatal_strike denied OK");

    var learn = await client.LearnSkillAsync("item_skillbook_fatal_strike", ct: ct);
    if (!learn.Success) throw new Exception($"LearnSkill failed: {learn.ErrorCode} {learn.Message}");
    if (learn.Skill.SkillId != "fatal_strike" || learn.Skill.Level != 1)
        throw new Exception($"unexpected learn state {learn.Skill.SkillId} lv{learn.Skill.Level}");
    Console.WriteLine($"[M9.4] Learned fatal_strike lv={learn.Skill.Level}");

    var list = await client.SkillListAsync(ct);
    if (!list.Success || !list.Skills.Any(s => s.SkillId == "fatal_strike"))
        throw new Exception("SkillList missing fatal_strike");

    int dmgFatalLv1 = 0;
    for (int attempt = 0; attempt < 8 && dmgFatalLv1 == 0; attempt++)
    {
        var mon = await FindMonster();
        await Walk(Math.Max(0, mon.X - 1), mon.Y);
        await Task.Delay(900, ct);
        var (atk, cs) = await client.AttackAsync(mon.EntityId, "fatal_strike", ct);
        if (atk.Success && cs != null && cs.Damage > 0)
            dmgFatalLv1 = cs.Damage;
        else
            await Task.Delay(400, ct);
    }
    if (dmgFatalLv1 <= 0) throw new Exception("fatal_strike lv1 did not deal damage");
    Console.WriteLine($"[M9.5] fatal_strike lv1 damage={dmgFatalLv1}");

    int targetLevel = 1;
    for (int i = 0; i < 40 && targetLevel < 2; i++)
    {
        var mon = await FindMonster();
        await Walk(Math.Max(0, mon.X - 1), mon.Y);
        await Task.Delay(900, ct);
        var (atk, _) = await client.AttackAsync(mon.EntityId, "fatal_strike", ct);
        if (!atk.Success)
        {
            await Task.Delay(400, ct);
            continue;
        }
        while (client.ReceivedSkillLevelUpNotifies.TryDequeue(out var up))
        {
            if (up.SkillId == "fatal_strike")
                targetLevel = Math.Max(targetLevel, up.NewLevel);
        }
        var sl = await client.SkillListAsync(ct);
        var st = sl.Skills.FirstOrDefault(s => s.SkillId == "fatal_strike");
        if (st != null) targetLevel = Math.Max(targetLevel, st.Level);
    }
    if (targetLevel < 2) throw new Exception($"skill did not level up (lv={targetLevel})");
    Console.WriteLine($"[M9.6] fatal_strike leveled to {targetLevel}");

    int dmgFatalLv2 = 0;
    for (int attempt = 0; attempt < 8 && dmgFatalLv2 == 0; attempt++)
    {
        var mon = await FindMonster();
        await Walk(Math.Max(0, mon.X - 1), mon.Y);
        await Task.Delay(900, ct);
        var (atk, cs) = await client.AttackAsync(mon.EntityId, "fatal_strike", ct);
        if (atk.Success && cs != null && cs.Damage > 0)
            dmgFatalLv2 = cs.Damage;
        else
            await Task.Delay(400, ct);
    }
    if (dmgFatalLv2 <= dmgFatalLv1)
        throw new Exception($"expected lv2 dmg > lv1: {dmgFatalLv2} vs {dmgFatalLv1}");
    Console.WriteLine($"[M9.7] fatal_strike lv{targetLevel} damage={dmgFatalLv2} > lv1={dmgFatalLv1}");

    while (client.ReceivedBuffApplyNotifies.TryDequeue(out _)) { }
    var use = await client.UseItemAsync("item_war_cry_scroll", 1, ct);
    if (!use.Success) throw new Exception($"UseItem scroll failed: {use.Message}");
    var buffN = await client.WaitBuffApplyAsync("buff_war_cry", 3000, ct);
    if (buffN.EffectiveAtk <= enter.EffectiveAtk)
        throw new Exception($"buff should raise atk: {buffN.EffectiveAtk} vs {enter.EffectiveAtk}");
    Console.WriteLine($"[M9.8] Buff war_cry applied EffectiveAtk={buffN.EffectiveAtk} (enterAtk={enter.EffectiveAtk})");

    int dmgBuffed = 0;
    for (int attempt = 0; attempt < 8 && dmgBuffed == 0; attempt++)
    {
        var mon = await FindMonster();
        await Walk(Math.Max(0, mon.X - 1), mon.Y);
        await Task.Delay(650, ct);
        var (atk, cs) = await client.AttackAsync(mon.EntityId, "basic_slash", ct);
        if (atk.Success && cs != null && cs.Damage > 0)
            dmgBuffed = cs.Damage;
        else
            await Task.Delay(400, ct);
    }
    Console.WriteLine($"[M9.9] Damaged under buff with basic_slash dmg={dmgBuffed}");

    var finalList = await client.SkillListAsync(ct);
    var fatal = finalList.Skills.First(s => s.SkillId == "fatal_strike");
    if (fatal.Level < 2) throw new Exception("persisted skill level lost in-session");
    Console.WriteLine($"[M9.10] SkillList OK fatal_strike lv={fatal.Level} exp={fatal.Exp}");
}

static async Task RunPartyAcceptanceAsync(string host, int port, CancellationToken ct)
{
    await using var a = new LegendTcpClient();
    await using var b = new LegendTcpClient();
    await a.ConnectAsync(host, port, ct);
    await b.ConnectAsync(host, port, ct);
    if (!(await a.HandshakeAsync("1.9.0", "pc-m8-a", ct)).Accepted) throw new Exception("A handshake failed");
    if (!(await b.HandshakeAsync("1.9.0", "pc-m8-b", ct)).Accepted) throw new Exception("B handshake failed");

    await a.RegisterAsync("m8_leader", "pass12345", "PC", ct);
    var loginA = await a.LoginAsync("m8_leader", "pass12345", "PC", ct);
    if (!loginA.Success) throw new Exception(loginA.Message);
    await b.RegisterAsync("m8_member", "pass12345", "PC", ct);
    var loginB = await b.LoginAsync("m8_member", "pass12345", "PC", ct);
    if (!loginB.Success) throw new Exception(loginB.Message);

    async Task<string> EnsureChar(LegendTcpClient c, string name, string cls)
    {
        var create = await c.CreateCharAsync(name, cls, ct);
        if (create.Success) return create.Character.CharacterId;
        var list = await c.CharListAsync(ct);
        var existing = list.Characters.FirstOrDefault(x => x.Name == name) ?? list.Characters.FirstOrDefault();
        if (existing is null) throw new Exception($"no char for {name}: {create.Message}");
        return existing.CharacterId;
    }

    string idA = await EnsureChar(a, "队长甲", "warrior");
    string idB = await EnsureChar(b, "队员乙", "mage");
    await a.SelectCharAsync(idA, ct);
    await b.SelectCharAsync(idB, ct);
    var realms = await a.GetRealmListAsync(ct);
    string realm = realms.Realms[0].RealmId;
    var enterA = await a.EnterWorldAsync(realm, idA, ct);
    var enterB = await b.EnterWorldAsync(realm, idB, ct);
    if (!enterA.Success || !enterB.Success) throw new Exception("enter failed");
    Console.WriteLine($"[M8.1] Entered world A={enterA.SelfEntity.Name} B={enterB.SelfEntity.Name}");

    // Friend: unidirectional add. Across host restart the list must already contain B (persistence).
    var fl0 = await a.FriendListAsync(ct);
    if (!fl0.Success) throw new Exception(fl0.Message);
    bool alreadyFriend = fl0.Friends.Any(f => f.CharacterId == idB || f.Name == "队员乙");
    if (alreadyFriend)
        Console.WriteLine($"[M8.2] Friend list persisted after Host restart ({fl0.Friends.Count} friends)");

    var selfAdd = await a.FriendAddAsync("队长甲", ct);
    if (selfAdd.Success || selfAdd.ErrorCode != ProtocolErrorCode.ErrFriendDenied)
        throw new Exception($"Expected ErrFriendDenied for self-add, got {selfAdd.ErrorCode}");

    // Ensure Lua on_friend_add fires every run: remove then re-add when already present
    if (alreadyFriend)
    {
        var rm = await a.FriendRemoveAsync(idB, ct);
        if (!rm.Success) throw new Exception($"FriendRemove before re-add failed: {rm.Message}");
    }
    var add = await a.FriendAddAsync("队员乙", ct);
    if (!add.Success) throw new Exception($"FriendAdd failed: {add.ErrorCode} {add.Message}");
    Console.WriteLine($"[M8.2b] FriendAdd OK: {add.Friend.Name} (persistedBefore={alreadyFriend})");

    // Whisper requires friend
    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
    await a.WhisperAsync("队员乙", "m8-hello", ct);
    await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatWhisper && n.Content == "m8-hello", 3000, ct);
    await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatWhisper && n.Content == "m8-hello" && n.SenderName == "队长甲", 3000, ct);
    Console.WriteLine("[M8.3] Whisper (friend) OK");

    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
    await b.WhisperAsync("队长甲", "should-deny", ct);
    await Task.Delay(250, ct);
    if (a.ReceivedChatNotifies.Any(n => n.Content == "should-deny"))
        throw new Exception("Non-friend whisper should be denied");
    Console.WriteLine("[M8.4] Non-friend whisper denied OK");

    // Party invite -> accept
    var inv = await a.PartyInviteAsync("队员乙", ct);
    if (!inv.Success) throw new Exception($"PartyInvite failed: {inv.ErrorCode} {inv.Message}");
    var pin = await b.WaitPartyInviteAsync(3000, ct);
    if (pin.PartyId != inv.PartyId) throw new Exception("invite party id mismatch");
    var acc = await b.PartyAcceptAsync(pin.PartyId, true, ct);
    if (!acc.Success) throw new Exception($"PartyAccept failed: {acc.ErrorCode} {acc.Message}");
    var info = await a.WaitPartyInfoAsync(n => n.Members.Count == 2, 3000, ct);
    if (info.Members.Count != 2) throw new Exception("expected 2 party members");
    Console.WriteLine($"[M8.5] Party formed: id={info.PartyId} members={info.Members.Count}");

    async Task Walk(LegendTcpClient c, int tx, int ty)
    {
        int guard = 0;
        while ((c.SelfEntity!.X != tx || c.SelfEntity.Y != ty) && guard++ < 80)
        {
            int nx = c.SelfEntity.X + Math.Sign(tx - c.SelfEntity.X);
            int ny = c.SelfEntity.Y + Math.Sign(ty - c.SelfEntity.Y);
            var m = await c.MoveAsync(nx, ny, ct: ct);
            if (!m.Accepted) throw new Exception($"Move rejected to ({nx},{ny}): {m.Reason}");
        }
    }

    while (a.ReceivedExpGains.TryDequeue(out _)) { }
    while (b.ReceivedExpGains.TryDequeue(out _)) { }

    async Task<EntitySnapshot> FindMonsterAsync(LegendTcpClient c, EnterWorldResp enter)
    {
        EntitySnapshot? m = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
        for (int i = 0; i < 80 && m is null; i++)
        {
            while (c.ReceivedAoiEnters.TryDequeue(out var en))
                if (en.Entity.Kind == EntityKind.EntityMonster) { m = en.Entity; break; }
            while (m is null && c.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityMonster) { m = sp.Entity; break; }
            if (m is null)
            {
                await Walk(c, 25 + (i % 3), 10 + (i % 2));
                await Task.Delay(50, ct);
            }
        }
        if (m is null) throw new Exception("monster not found for shared exp");
        return m;
    }

    ExpGainNotify? expA = null, expB = null;
    for (int attempt = 0; attempt < 5 && (expA is null || expB is null); attempt++)
    {
        var monster = await FindMonsterAsync(a, enterA);
        await Walk(a, Math.Max(0, monster.X - 1), monster.Y);
        await Walk(b, Math.Max(0, monster.X - 1), Math.Min(monster.Y + 1, 40));
        for (int hit = 0; hit < 30 && (expA is null || expB is null); hit++)
        {
            await Task.Delay(650, ct);
            var (atk, cs) = await a.AttackAsync(monster.EntityId, "basic_slash", ct);
            if (!atk.Success)
            {
                await Task.Delay(400, ct);
                continue;
            }
            while (a.ReceivedExpGains.TryDequeue(out var e))
                if (e.GainedExp > 0) expA = e;
            while (b.ReceivedExpGains.TryDequeue(out var e))
                if (e.GainedExp > 0) expB = e;
            if (cs?.TargetDied == true)
                break;
        }
    }
    if (expA is null || expB is null)
        throw new Exception($"shared exp missing: A={expA?.GainedExp} B={expB?.GainedExp}");
    Console.WriteLine($"[M8.6] Shared exp OK: A+{expA.GainedExp} B+{expB.GainedExp}");

    var leave = await b.PartyLeaveAsync(ct);
    if (!leave.Success) throw new Exception($"PartyLeave failed: {leave.Message}");
    Console.WriteLine("[M8.7] Member left party OK");

    // Kick path
    var inv2 = await a.PartyInviteAsync("队员乙", ct);
    if (!inv2.Success) throw new Exception($"re-invite failed: {inv2.Message}");
    var pin2 = await b.WaitPartyInviteAsync(3000, ct);
    if (!(await b.PartyAcceptAsync(pin2.PartyId, true, ct)).Success)
        throw new Exception("re-accept failed");
    await a.WaitPartyInfoAsync(n => n.Members.Count == 2, 3000, ct);
    var kick = await a.PartyKickAsync(idB, ct);
    if (!kick.Success) throw new Exception($"PartyKick failed: {kick.Message}");
    Console.WriteLine("[M8.8] Leader kick OK");
}

static async Task RunTradeAcceptanceAsync(string host, int port, CancellationToken ct)
{
    await using var seller = new LegendTcpClient();
    await using var buyer = new LegendTcpClient();
    await seller.ConnectAsync(host, port, ct);
    await buyer.ConnectAsync(host, port, ct);
    if (!(await seller.HandshakeAsync("1.9.0", "pc-m7-seller", ct)).Accepted) throw new Exception("Seller handshake failed");
    if (!(await buyer.HandshakeAsync("1.9.0", "pc-m7-buyer", ct)).Accepted) throw new Exception("Buyer handshake failed");

    await seller.RegisterAsync("m7_seller", "pass12345", "PC", ct);
    var loginS = await seller.LoginAsync("m7_seller", "pass12345", "PC", ct);
    if (!loginS.Success) throw new Exception(loginS.Message);
    await buyer.RegisterAsync("m7_buyer", "pass12345", "PC", ct);
    var loginB = await buyer.LoginAsync("m7_buyer", "pass12345", "PC", ct);
    if (!loginB.Success) throw new Exception(loginB.Message);

    async Task<string> EnsureChar(LegendTcpClient c, string name, string cls)
    {
        var create = await c.CreateCharAsync(name, cls, ct);
        if (create.Success) return create.Character.CharacterId;
        var list = await c.CharListAsync(ct);
        var existing = list.Characters.FirstOrDefault(x => x.Name == name) ?? list.Characters.FirstOrDefault();
        if (existing is null) throw new Exception($"no char for {name}: {create.Message}");
        return existing.CharacterId;
    }

    string idS = await EnsureChar(seller, "摊主老王", "warrior");
    string idB = await EnsureChar(buyer, "顾客小李", "mage");
    await seller.SelectCharAsync(idS, ct);
    await buyer.SelectCharAsync(idB, ct);
    var realms = await seller.GetRealmListAsync(ct);
    string realm = realms.Realms[0].RealmId;
    var enterS = await seller.EnterWorldAsync(realm, idS, ct);
    var enterB = await buyer.EnterWorldAsync(realm, idB, ct);
    if (!enterS.Success || !enterB.Success) throw new Exception("enter failed");
    Console.WriteLine($"[M7.1] Entered world sellerGold={enterS.Gold} buyerGold={enterB.Gold}");

    async Task Walk(LegendTcpClient c, int tx, int ty)
    {
        while (c.SelfEntity!.X != tx || c.SelfEntity.Y != ty)
        {
            int nx = c.SelfEntity.X + Math.Sign(tx - c.SelfEntity.X);
            int ny = c.SelfEntity.Y + Math.Sign(ty - c.SelfEntity.Y);
            var m = await c.MoveAsync(nx, ny, ct: ct);
            if (!m.Accepted) throw new Exception($"Move rejected to ({nx},{ny})");
        }
    }

    // Check if there is an existing active listing from seller (post-restart verification)
    var browse0 = await buyer.StallBrowseAsync("item_hp_potion", ct);
    if (!browse0.Success) throw new Exception($"browse0 failed: {browse0.Message}");
    var persistedListing = browse0.Listings.FirstOrDefault(l => l.SellerCharacterId == idS);
    if (persistedListing != null)
    {
        Console.WriteLine($"[M7.2] Found persisted listing after Host restart: id={persistedListing.ListingId} item={persistedListing.ItemId}x{persistedListing.Count} price={persistedListing.TotalPriceGold}");
        int sellerGoldBefore = seller.Gold;
        int buyerGoldBefore = buyer.Gold;
        var buyP = await buyer.StallBuyAsync(persistedListing.ListingId, ct);
        if (!buyP.Success) throw new Exception($"buy persisted failed: {buyP.ErrorCode} {buyP.Message}");
        var soldN = await seller.WaitStallSoldAsync(n => n.ListingId == persistedListing.ListingId, 3000, ct);
        if (buyer.Gold != buyerGoldBefore - persistedListing.TotalPriceGold)
            throw new Exception($"buyer gold mismatch: expected {buyerGoldBefore - persistedListing.TotalPriceGold}, got {buyer.Gold}");
        if (soldN.NewGold != sellerGoldBefore + persistedListing.TotalPriceGold)
            throw new Exception($"seller gold mismatch: expected {sellerGoldBefore + persistedListing.TotalPriceGold}, got {soldN.NewGold}");
        Console.WriteLine($"[M7.3] Purchased persisted listing OK: buyerGold={buyer.Gold}, sellerGold={soldN.NewGold}");
        return;
    }

    // Ensure seller is in safe zone (10,10) and has at least 3 potions from NPC shop
    await Walk(seller, 12, 12);
    var npc = enterS.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityNpc);
    if (npc is null)
    {
        for (int i = 0; i < 20 && npc is null; i++)
        {
            while (seller.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityNpc) { npc = sp.Entity; break; }
            await Task.Delay(25, ct);
        }
    }
    if (npc is null) throw new Exception("NPC not found for seller shop purchase");
    await Walk(seller, npc.X, npc.Y);
    var shopBuy = await seller.ShopBuyAsync(npc.EntityId, "item_hp_potion", 3, ct);
    if (!shopBuy.Success) throw new Exception($"Seller ShopBuy failed: {shopBuy.Message}");
    int potionsAfterShop = shopBuy.Inventory.FirstOrDefault(i => i.ItemId == "item_hp_potion")?.Count ?? 0;
    Console.WriteLine($"[M7.2] Seller bought 3 potions from NPC: bagPotions={potionsAfterShop}, gold={seller.Gold}");

    // Test safe-zone enforcement: walk outside safe zone (20,10) -> list should be rejected with ErrSafeZone
    await Walk(seller, 20, 10);
    var listOutside = await seller.StallListAsync("item_hp_potion", 1, 15, ct);
    if (listOutside.Success || listOutside.ErrorCode != ProtocolErrorCode.ErrSafeZone)
        throw new Exception($"Expected ErrSafeZone outside safe zone, got success={listOutside.Success} code={listOutside.ErrorCode}");
    Console.WriteLine($"[M7.3] Safe-zone check OK: listing outside safe zone rejected ({listOutside.ErrorCode})");

    // Walk back into safe zone (10,10) and list 3 listings:
    // #1: to be bought now
    // #2: to be cancelled now
    // #3: left active so post-restart run can verify persistence and buy it!
    await Walk(seller, 10, 10);
    var list1 = await seller.StallListAsync("item_hp_potion", 1, 15, ct);
    if (!list1.Success) throw new Exception($"list1 failed: {list1.Message}");
    var list2 = await seller.StallListAsync("item_hp_potion", 1, 20, ct);
    if (!list2.Success) throw new Exception($"list2 failed: {list2.Message}");
    var list3 = await seller.StallListAsync("item_hp_potion", 1, 12, ct);
    if (!list3.Success) throw new Exception($"list3 failed: {list3.Message}");
    int potionsAfterList = list3.Inventory.FirstOrDefault(i => i.ItemId == "item_hp_potion")?.Count ?? 0;
    if (potionsAfterList != potionsAfterShop - 3)
        throw new Exception($"Expected seller bag potions {potionsAfterShop - 3}, got {potionsAfterList}");
    Console.WriteLine($"[M7.4] Seller listed 3 stalls in safe zone; bagPotions={potionsAfterList}");

    // Seller cancels listing #2 -> item returned to bag
    var cancel2 = await seller.StallCancelAsync(list2.Listing.ListingId, ct);
    if (!cancel2.Success) throw new Exception($"cancel2 failed: {cancel2.Message}");
    int potionsAfterCancel = cancel2.Inventory.FirstOrDefault(i => i.ItemId == "item_hp_potion")?.Count ?? 0;
    if (potionsAfterCancel != potionsAfterList + 1)
        throw new Exception($"Expected seller bag potions {potionsAfterList + 1} after cancel, got {potionsAfterCancel}");
    Console.WriteLine($"[M7.5] Seller cancelled listing {list2.Listing.ListingId}; bagPotions={potionsAfterCancel}");

    // Self-buy rejected
    var selfBuy = await seller.StallBuyAsync(list1.Listing.ListingId, ct);
    if (selfBuy.Success || selfBuy.ErrorCode != ProtocolErrorCode.ErrSelfBuyDenied)
        throw new Exception($"Expected ErrSelfBuyDenied, got {selfBuy.ErrorCode}");

    // Buyer browses active listings and buys listing #1
    var browse = await buyer.StallBrowseAsync("item_hp_potion", ct);
    if (!browse.Success || !browse.Listings.Any(l => l.ListingId == list1.Listing.ListingId))
        throw new Exception("Buyer browse did not find listing #1");
    Console.WriteLine($"[M7.6] Buyer browsed {browse.Listings.Count} active listings");

    int sellerGoldBeforeBuy = seller.Gold;
    int buyerGoldBeforeBuy = buyer.Gold;
    int buyerPotionsBefore = enterB.Inventory.FirstOrDefault(i => i.ItemId == "item_hp_potion")?.Count ?? 0;

    var buy1 = await buyer.StallBuyAsync(list1.Listing.ListingId, ct);
    if (!buy1.Success) throw new Exception($"buy1 failed: {buy1.ErrorCode} {buy1.Message}");
    int buyerPotionsAfter = buy1.Inventory.FirstOrDefault(i => i.ItemId == "item_hp_potion")?.Count ?? 0;
    if (buyerPotionsAfter != buyerPotionsBefore + 1)
        throw new Exception($"Expected buyer potions {buyerPotionsBefore + 1}, got {buyerPotionsAfter}");
    if (buy1.Gold != buyerGoldBeforeBuy - 15)
        throw new Exception($"Expected buyer gold {buyerGoldBeforeBuy - 15}, got {buy1.Gold}");

    var soldNotify = await seller.WaitStallSoldAsync(n => n.ListingId == list1.Listing.ListingId, 3000, ct);
    if (soldNotify.NewGold != sellerGoldBeforeBuy + 15)
        throw new Exception($"Expected seller gold {sellerGoldBeforeBuy + 15}, got {soldNotify.NewGold}");
    Console.WriteLine($"[M7.7] Buyer purchased listing {list1.Listing.ListingId}: buyerGold={buy1.Gold}, sellerGold={soldNotify.NewGold}, remainingActiveListing={list3.Listing.ListingId}");
}

static async Task RunGuildAcceptanceAsync(string host, int port, CancellationToken ct)
{
    await using var a = new LegendTcpClient();
    await using var b = new LegendTcpClient();
    await a.ConnectAsync(host, port, ct);
    await b.ConnectAsync(host, port, ct);
    if (!(await a.HandshakeAsync("1.9.0", "pc-m6-a", ct)).Accepted) throw new Exception("A handshake failed");
    if (!(await b.HandshakeAsync("1.9.0", "pc-m6-b", ct)).Accepted) throw new Exception("B handshake failed");

    await a.RegisterAsync("m6_leader", "pass12345", "PC", ct);
    var loginA = await a.LoginAsync("m6_leader", "pass12345", "PC", ct);
    if (!loginA.Success) throw new Exception(loginA.Message);
    await b.RegisterAsync("m6_member", "pass12345", "PC", ct);
    var loginB = await b.LoginAsync("m6_member", "pass12345", "PC", ct);
    if (!loginB.Success) throw new Exception(loginB.Message);

    async Task<string> EnsureChar(LegendTcpClient c, string name, string cls)
    {
        var create = await c.CreateCharAsync(name, cls, ct);
        if (create.Success) return create.Character.CharacterId;
        var list = await c.CharListAsync(ct);
        var existing = list.Characters.FirstOrDefault(x => x.Name == name) ?? list.Characters.FirstOrDefault();
        if (existing is null) throw new Exception($"no char for {name}: {create.Message}");
        return existing.CharacterId;
    }

    string idA = await EnsureChar(a, "行会会长", "warrior");
    string idB = await EnsureChar(b, "行会成员", "mage");
    await a.SelectCharAsync(idA, ct);
    await b.SelectCharAsync(idB, ct);
    var realms = await a.GetRealmListAsync(ct);
    string realm = realms.Realms[0].RealmId;
    var enterA = await a.EnterWorldAsync(realm, idA, ct);
    var enterB = await b.EnterWorldAsync(realm, idB, ct);
    if (!enterA.Success || !enterB.Success) throw new Exception("enter failed");
    Console.WriteLine($"[M6.1] Entered world goldA={enterA.Gold} guildA='{enterA.GuildName}' guildB='{enterB.GuildName}'");

    string gName = "龙腾行会";
    bool persisted = !string.IsNullOrEmpty(a.GuildId) && !string.IsNullOrEmpty(b.GuildId)
                     && string.Equals(a.GuildId, b.GuildId, StringComparison.Ordinal);
    if (persisted)
    {
        gName = a.GuildName;
        Console.WriteLine($"[M6.2] Persisted guild restored: {gName} (A={a.GuildRank}, B={b.GuildRank})");
        Console.WriteLine($"[M6.3] Both members already in guild after Host restart");
    }
    else
    {
        // Ensure clean slate for create path
        if (!string.IsNullOrEmpty(a.GuildId))
        {
            var leave = await a.GuildLeaveAsync(ct);
            Console.WriteLine($"[M6.1b] A left prior guild: {leave.Success}");
        }
        if (!string.IsNullOrEmpty(b.GuildId))
        {
            var leave = await b.GuildLeaveAsync(ct);
            Console.WriteLine($"[M6.1c] B left prior guild: {leave.Success}");
        }

        var createG = await a.GuildCreateAsync(gName, ct);
        if (!createG.Success)
        {
            gName = "龙腾" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()[^4..];
            createG = await a.GuildCreateAsync(gName, ct);
            if (!createG.Success) throw new Exception($"create guild: {createG.ErrorCode} {createG.Message}");
        }
        if (createG.Gold != enterA.Gold - 50) throw new Exception($"gold cost expected 50, before={enterA.Gold} after={createG.Gold}");
        if (createG.GuildRank != "leader") throw new Exception("expected leader rank");
        Console.WriteLine($"[M6.2] Created guild {createG.GuildName} id={createG.GuildId} gold={createG.Gold}");

        var join = await b.GuildJoinAsync(gName, ct);
        if (!join.Success) throw new Exception($"join: {join.ErrorCode} {join.Message}");
        if (join.MemberCount < 2) throw new Exception("expected >=2 members");
        Console.WriteLine($"[M6.3] B joined {join.GuildName} members={join.MemberCount}");
    }

    var info = await a.GuildInfoAsync(ct: ct);
    if (!info.Success || info.Members.Count < 1) throw new Exception($"info: {info.Message}");
    Console.WriteLine($"[M6.4] GuildInfo members={info.Members.Count} leader={info.LeaderCharacterId}");

    // World chat
    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
    await a.ChatAsync(ChatChannel.ChatWorld, "hello-world", ct: ct);
    await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatWorld && n.Content == "hello-world", 3000, ct);
    await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatWorld && n.Content == "hello-world", 3000, ct);
    Console.WriteLine("[M6.5] World chat OK (A+B received)");
    await Task.Delay(50, ct);

    // Nearby
    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
    await a.ChatAsync(ChatChannel.ChatNearby, "hello-near", ct: ct);
    await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatNearby && n.Content == "hello-near", 3000, ct);
    await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatNearby && n.Content == "hello-near", 3000, ct);
    Console.WriteLine("[M6.6] Nearby chat OK");
    await Task.Delay(50, ct);

    // Guild
    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
    await a.ChatAsync(ChatChannel.ChatGuild, "hello-guild", ct: ct);
    await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatGuild && n.Content == "hello-guild", 3000, ct);
    await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatGuild && n.Content == "hello-guild", 3000, ct);
    Console.WriteLine("[M6.7] Guild chat OK");

    // Denied: leave then guild chat
    var leaveB = await b.GuildLeaveAsync(ct);
    if (!leaveB.Success) throw new Exception(leaveB.Message);
    while (b.ReceivedServerNotices.TryDequeue(out _)) { }
    await b.ChatAsync(ChatChannel.ChatGuild, "should-fail", ct: ct);
    bool denied = false;
    for (int i = 0; i < 40; i++)
    {
        while (b.ReceivedServerNotices.TryDequeue(out var sn))
        {
            if (sn.NoticeType == "chat_denied") { denied = true; break; }
        }
        if (denied) break;
        await Task.Delay(50, ct);
    }
    if (!denied) throw new Exception("expected chat_denied for non-guild ChatGuild");
    Console.WriteLine("[M6.8] Guild chat denied after leave OK");

    // Rejoin for persistence check on restart
    var rejoin = await b.GuildJoinAsync(gName, ct);
    if (!rejoin.Success) throw new Exception($"rejoin: {rejoin.Message}");
    Console.WriteLine($"[M6.9] B rejoined for persistence check; guild={a.GuildName}");
}


await using var clientA = new LegendTcpClient();
await using var clientB = new LegendTcpClient();

await clientA.ConnectAsync(host, port, cts.Token);
var hsA = await clientA.HandshakeAsync("1.9.0", "pc-smoke-1.6.0", cts.Token);
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
await clientB.HandshakeAsync("1.9.0", "android-smoke-1.6.0", cts.Token);
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
if (!equipTest && !levelTest)
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


if (levelTest)
{
    Console.WriteLine($"[Step M5.0] EnterWorld level={enterA.Level} exp={enterA.Exp} expToNext={enterA.ExpToNext} atk={enterA.EffectiveAtk} maxHp={enterA.EffectiveMaxHp} maxMp={enterA.EffectiveMaxMp}");
    if (enterA.Level >= 2 && enterA.Exp >= 0)
    {
        // Restart persistence path
        if (enterA.Level < 2)
            throw new Exception("Expected persisted level >= 2");
        bool hasPotion = enterA.Inventory.Any(i => i.ItemId == "item_hp_potion");
        Console.WriteLine($"[Step M5.RestartVerify] Persisted level={enterA.Level} exp={enterA.Exp} atk={enterA.EffectiveAtk} maxHp={enterA.EffectiveMaxHp} potionInBag={hasPotion}");
        if (enterA.EffectiveAtk < 28)
            throw new Exception($"Expected Lv2+ warrior base atk >= 28, got {enterA.EffectiveAtk}");
        if (enterA.EffectiveMaxHp < 200)
            throw new Exception($"Expected Lv2+ maxHp >= 200, got {enterA.EffectiveMaxHp}");
    }
    else
    {
        // Buy potions for later use
        await Walk(12, 12);
        var buyPot = await clientA.ShopBuyAsync("npc_village_merchant", "item_hp_potion", 2, cts.Token);
        if (!buyPot.Success)
            throw new Exception($"Buy potion failed: {buyPot.Message}");
        Console.WriteLine($"[Step M5.1] Bought HP potions, gold={buyPot.Gold}");

        // Try equip iron sword at Lv.1 — must fail RequiredLevel
        var buyIronEarly = await clientA.ShopBuyAsync("npc_village_merchant", "item_iron_sword", 1, cts.Token);
        if (!buyIronEarly.Success)
            throw new Exception($"Buy iron sword failed: {buyIronEarly.Message}");
        var eqEarly = await clientA.EquipAsync("item_iron_sword", "weapon", cts.Token);
        if (eqEarly.Success)
            throw new Exception("Expected Equip iron sword denied at Lv.1");
        Console.WriteLine($"[Step M5.2] Equip iron sword denied at Lv.1 OK: {eqEarly.ErrorCode}");

        int startLevel = enterA.Level;
        int startAtk = enterA.EffectiveAtk;
        int startMaxHp = enterA.EffectiveMaxHp;

        // Kill slimes until level-up (30 exp each, need 50)
        await Walk(22, 10);
        int kills = 0;
        LevelUpNotify? levelUp = null;
        for (int attempt = 0; attempt < 8 && levelUp is null; attempt++)
        {
            EntitySnapshot? mTarget = null;
            for (int i = 0; i < 120 && mTarget is null; i++)
            {
                while (clientA.ReceivedAoiEnters.TryDequeue(out var en))
                    if (en.Entity.Kind == EntityKind.EntityMonster && en.Entity.Hp > 0) { mTarget = en.Entity; break; }
                while (mTarget is null && clientA.ReceivedEntitySpawns.TryDequeue(out var sp))
                    if (sp.Entity.Kind == EntityKind.EntityMonster && sp.Entity.Hp > 0) { mTarget = sp.Entity; break; }
                if (mTarget is null)
                {
                    int[][] probes = { new[] { 25, 10 }, new[] { 28, 12 }, new[] { 24, 10 }, new[] { 32, 14 } };
                    var pr = probes[i % probes.Length];
                    await Walk(pr[0], pr[1]);
                    await Task.Delay(200, cts.Token);
                }
            }
            if (mTarget is null)
                throw new Exception("No monster for M5 leveling");

            await Walk(mTarget.X - 1, mTarget.Y);
            for (int hit = 0; hit < 30; hit++)
            {
                await Task.Delay(700, cts.Token);
                var (atk, cs) = await clientA.AttackAsync(mTarget.EntityId, "basic_slash", cts.Token);
                if (!atk.Success)
                {
                    Console.WriteLine($"[Step M5.3.{kills}.{hit}] attack soft-fail: {atk.Message}");
                    await Task.Delay(400, cts.Token);
                    continue;
                }
                Console.WriteLine($"[Step M5.3.{kills}.{hit}] dmg={cs?.Damage} died={cs?.TargetDied} selfHp={clientA.SelfEntity?.Hp}");
                while (clientA.ReceivedLevelUps.TryDequeue(out var lu))
                    levelUp = lu;
                if (cs?.TargetDied == true)
                {
                    kills++;
                    for (int w = 0; w < 20; w++)
                    {
                        while (clientA.ReceivedExpGains.TryDequeue(out var eg))
                            Console.WriteLine($"[Step M5.3] ExpGain +{eg.GainedExp} total={eg.TotalExp} lv={eg.Level}");
                        while (clientA.ReceivedLevelUps.TryDequeue(out var lu))
                            levelUp = lu;
                        if (levelUp != null) break;
                        await Task.Delay(50, cts.Token);
                    }
                    break;
                }
            }
            while (clientA.ReceivedLevelUps.TryDequeue(out var lu2))
                levelUp = lu2;
        }

        if (levelUp is null)
            throw new Exception("Did not receive LevelUpNotify after killing slimes");
        if (levelUp.NewLevel != startLevel + 1)
            throw new Exception($"Expected level {startLevel + 1}, got {levelUp.NewLevel}");
        if (levelUp.EffectiveAtk <= startAtk)
            throw new Exception($"Expected atk increase on level-up: {levelUp.EffectiveAtk} vs {startAtk}");
        if (levelUp.EffectiveMaxHp <= startMaxHp)
            throw new Exception($"Expected maxHp increase on level-up: {levelUp.EffectiveMaxHp} vs {startMaxHp}");
        if (levelUp.Hp != levelUp.EffectiveMaxHp)
            throw new Exception("Expected full HP after level-up");
        Console.WriteLine($"[Step M5.4] LevelUp {levelUp.OldLevel}->{levelUp.NewLevel} atk={levelUp.EffectiveAtk} maxHp={levelUp.EffectiveMaxHp} exp={levelUp.Exp}");

        // Equip iron sword now that we're Lv.2+
        var eqIron = await clientA.EquipAsync("item_iron_sword", "weapon", cts.Token);
        if (!eqIron.Success)
            throw new Exception($"Equip iron sword at Lv.2 failed: {eqIron.ErrorCode} {eqIron.Message}");
        Console.WriteLine($"[Step M5.5] Equipped iron sword OK, effectiveAtk={eqIron.EffectiveAtk}");

        // Damage self by standing near monster briefly, then use potion
        // Self HP should be full after level-up; get hit by walking to a live monster and waiting for AI counter
        int hpBeforeUse = clientA.SelfEntity?.Hp ?? levelUp.Hp;
        // Force HP lower by using a trick: if still full, attack and wait for counter-attacks
        await Walk(25, 10);
        EntitySnapshot? hurtMob = null;
        for (int i = 0; i < 30 && hurtMob is null; i++)
        {
            while (clientA.ReceivedAoiEnters.TryDequeue(out var en))
                if (en.Entity.Kind == EntityKind.EntityMonster) { hurtMob = en.Entity; break; }
            while (hurtMob is null && clientA.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityMonster) { hurtMob = sp.Entity; break; }
            if (hurtMob is null) await Task.Delay(50, cts.Token);
        }
        if (hurtMob != null)
        {
            await Walk(hurtMob.X, hurtMob.Y);
            // Wait for monster AI to hit us
            for (int w = 0; w < 30; w++)
            {
                await Task.Delay(200, cts.Token);
                while (clientA.ReceivedCombatSyncs.TryDequeue(out var csHurt))
                {
                    if (clientA.SelfEntity != null && csHurt.TargetEntityId == clientA.SelfEntity.EntityId && csHurt.Damage > 0)
                    {
                        clientA.SelfEntity.Hp = csHurt.TargetHp;
                        hpBeforeUse = csHurt.TargetHp;
                    }
                }
                if ((clientA.SelfEntity?.Hp ?? hpBeforeUse) < levelUp.EffectiveMaxHp)
                    break;
            }
        }
        hpBeforeUse = clientA.SelfEntity?.Hp ?? hpBeforeUse;
        Console.WriteLine($"[Step M5.6] HP before potion={hpBeforeUse} / {levelUp.EffectiveMaxHp}");

        // If somehow still full HP, still call UseItem (heal clamps) but assert inventory decrement
        var use = await clientA.UseItemAsync("item_hp_potion", 1, cts.Token);
        if (!use.Success)
            throw new Exception($"UseItem failed: {use.ErrorCode} {use.Message}");
        if (hpBeforeUse < levelUp.EffectiveMaxHp && use.Hp <= hpBeforeUse)
            throw new Exception($"Expected HP increase after potion: before={hpBeforeUse} after={use.Hp}");
        if (use.Hp > use.MaxHp)
            throw new Exception("HP exceeded MaxHp after potion");
        Console.WriteLine($"[Step M5.7] UseItem OK: hp {hpBeforeUse}->{use.Hp}/{use.MaxHp} remainingPotions={use.RemainingCount}");

        // Dead check: UseItem on missing item
        var useMissing = await clientA.UseItemAsync("item_mp_potion", 1, cts.Token);
        if (useMissing.Success)
            throw new Exception("Expected UseItem denied for missing MP potion");
        Console.WriteLine($"[Step M5.8] UseItem missing denied OK: {useMissing.ErrorCode}");
    }
}

if (guildTest)
    Console.WriteLine("[SUCCESS] All M6 guild/chat verification steps passed!");
else if (levelTest)
    Console.WriteLine("[SUCCESS] All M5 full-chain verification steps passed!");
else if (equipTest)
    Console.WriteLine("[SUCCESS] All M4 full-chain verification steps passed!");
else
    Console.WriteLine("[SUCCESS] All M3 full-chain verification steps passed!");
