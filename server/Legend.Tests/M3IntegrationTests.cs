using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M3IntegrationTests
{
    private static string GetManifestPath()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string p = Path.Combine(root, "version-packs", "sample-classic", "manifest.json");
        if (!File.Exists(p))
        {
            root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            p = Path.Combine(root, "version-packs", "sample-classic", "manifest.json");
        }
        if (!File.Exists(p))
            p = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "version-packs", "sample-classic", "manifest.json"));
        Assert.True(File.Exists(p), $"manifest not found: {p}");
        return p;
    }

    private static async Task WalkToAsync(LegendTcpClient client, int tx, int ty, CancellationToken ct = default)
    {
        Assert.NotNull(client.SelfEntity);
        int x = client.SelfEntity!.X;
        int y = client.SelfEntity.Y;
        int guard = 0;
        while ((x != tx || y != ty) && guard++ < 80)
        {
            int nx = x, ny = y;
            if (x != tx)
                nx = x + Math.Clamp(tx - x, -2, 2);
            else
                ny = y + Math.Clamp(ty - y, -2, 2);

            var sync = await client.MoveAsync(nx, ny, ct: ct);
            Assert.True(sync.Accepted, $"Move to ({nx},{ny}) failed: {sync.Reason}");
            x = sync.ToX;
            y = sync.ToY;
        }
        Assert.Equal(tx, client.SelfEntity.X);
        Assert.Equal(ty, client.SelfEntity.Y);
    }

    private static void DrainMonsterSightings(LegendTcpClient client, Dictionary<string, EntitySnapshot> seen)
    {
        while (client.ReceivedAoiEnters.TryDequeue(out var en))
        {
            if (en.Entity.Kind == EntityKind.EntityMonster && en.Entity.TemplateId == "slime_green")
                seen[en.Entity.EntityId] = en.Entity;
        }
        while (client.ReceivedEntitySpawns.TryDequeue(out var sp))
        {
            if (sp.Entity.Kind == EntityKind.EntityMonster && sp.Entity.TemplateId == "slime_green")
                seen[sp.Entity.EntityId] = sp.Entity;
        }
    }

    private static async Task KillOneSlimeAsync(
        LegendTcpClient client,
        HashSet<string> killedIds,
        Dictionary<string, EntitySnapshot> seen,
        CancellationToken ct = default)
    {
        EntitySnapshot? monster = null;
        int[][] probes = { new[] { 25, 10 }, new[] { 28, 12 }, new[] { 26, 11 }, new[] { 24, 10 }, new[] { 22, 10 }, new[] { 30, 12 } };
        for (int attempt = 0; attempt < 80 && monster is null; attempt++)
        {
            DrainMonsterSightings(client, seen);
            monster = seen.Values.FirstOrDefault(e => !killedIds.Contains(e.EntityId));
            if (monster is not null)
                break;
            var probe = probes[attempt % probes.Length];
            await WalkToAsync(client, probe[0], probe[1], ct);
            await Task.Delay(50, ct);
        }
        Assert.NotNull(monster);

        await WalkToAsync(client, monster!.X - 1, monster.Y, ct);

        // power_strike CD 2500ms — wait if we already used it on a prior kill
        if (killedIds.Count > 0)
            await Task.Delay(2600, ct);

        bool died = false;
        for (int hit = 0; hit < 10 && !died; hit++)
        {
            string skill = hit == 0 ? "power_strike" : "basic_slash";
            if (hit > 0)
                await Task.Delay(650, ct);
            var (atk, combat) = await client.AttackAsync(monster.EntityId, skill, ct);
            Assert.True(atk.Success, $"Attack failed: {atk.ErrorCode} {atk.Message}");
            died = combat?.TargetDied == true;
        }
        Assert.True(died, "Expected slime to die");
        killedIds.Add(monster.EntityId);
        seen.Remove(monster.EntityId);
    }

    [Fact]
    public async Task Shop_Buy_And_Quest_Persist_AfterRestart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m3-{Guid.NewGuid():N}.db");
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await client.HandshakeAsync("1.4.0")).Accepted);
        await client.RegisterAndLoginAsync("m3_trader", "secret1");
        var create = await client.CreateCharAsync("商旅甲", "warrior");
        Assert.True(create.Success, create.Message);
        string charId = create.Character.CharacterId;
        await client.SelectCharAsync(charId);
        var enter = await client.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter.Success);
        Assert.Equal(100, enter.Gold);

        // NPC should be nearby at (12,12) — spawn is in safe zone near default spawn
        var npc = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityNpc);
        if (npc is null)
        {
            await WalkToAsync(client, 12, 12);
            for (int i = 0; i < 20 && npc is null; i++)
            {
                while (client.ReceivedEntitySpawns.TryDequeue(out var sp))
                {
                    if (sp.Entity.Kind == EntityKind.EntityNpc)
                    {
                        npc = sp.Entity;
                        break;
                    }
                }
                await Task.Delay(30);
            }
        }
        Assert.NotNull(npc);
        await WalkToAsync(client, npc!.X, npc.Y);

        var talk = await client.TalkAsync(npc.EntityId);
        Assert.True(talk.Success, talk.Message);
        Assert.Equal("npc_village_merchant", talk.NpcId);
        Assert.True(talk.ShopOffers.Count >= 1);
        Assert.Contains(talk.Quests, q => q.QuestId == "quest_slime_hunt");

        var buy = await client.ShopBuyAsync(npc.EntityId, "item_hp_potion", 1);
        Assert.True(buy.Success, buy.Message);
        Assert.Equal(90, buy.Gold);
        Assert.Contains(buy.Inventory, i => i.ItemId == "item_hp_potion" && i.Count >= 1);

        var accept = await client.QuestAcceptAsync(npc.EntityId, "quest_slime_hunt");
        Assert.True(accept.Success, accept.Message);
        Assert.Equal(QuestStatus.QuestActive, accept.Quest.Status);

        // Leave safe zone and kill 2 green slimes (3 spawn on novice_plain)
        var killed = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, EntitySnapshot>(StringComparer.Ordinal);
        foreach (var e in enter.NearbyEntities.Where(e => e.Kind == EntityKind.EntityMonster && e.TemplateId == "slime_green"))
            seen[e.EntityId] = e;
        await WalkToAsync(client, 22, 10);
        DrainMonsterSightings(client, seen);
        await KillOneSlimeAsync(client, killed, seen);
        await KillOneSlimeAsync(client, killed, seen);

        // Wait for quest ready
        QuestProgress? progress = null;
        for (int i = 0; i < 40 && progress is null; i++)
        {
            while (client.ReceivedQuestProgress.TryDequeue(out var qp))
            {
                if (qp.Quest.QuestId == "quest_slime_hunt" &&
                    (qp.Quest.Status == QuestStatus.QuestReady || qp.Quest.Progress >= 2))
                {
                    progress = qp.Quest;
                    break;
                }
            }
            if (progress is null)
                await Task.Delay(50);
        }
        Assert.NotNull(progress);

        await WalkToAsync(client, npc.X, npc.Y);
        var turnIn = await client.QuestTurnInAsync(npc.EntityId, "quest_slime_hunt");
        Assert.True(turnIn.Success, turnIn.Message);
        Assert.Equal(QuestStatus.QuestCompleted, turnIn.Quest.Status);
        // 90 + 50 reward
        Assert.Equal(140, turnIn.Gold);
        Assert.Contains(turnIn.Inventory, i => i.ItemId == "item_hp_potion" && i.Count >= 4); // 1 bought + 3 reward

        await client.DisposeAsync();
        await engine.DisposeAsync();

        var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        await using var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
        await engine2.StartAsync();
        await using var client2 = new LegendTcpClient();
        await client2.ConnectAsync("127.0.0.1", engine2.BoundPort);
        await client2.HandshakeAsync("1.4.0");
        await client2.LoginAsync("m3_trader", "secret1");
        await client2.SelectCharAsync(charId);
        var enter2 = await client2.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter2.Success);
        Assert.Equal(140, enter2.Gold);
        Assert.Contains(enter2.Inventory, i => i.ItemId == "item_hp_potion" && i.Count >= 4);
        Assert.Contains(enter2.Quests, q => q.QuestId == "quest_slime_hunt" && q.Status == QuestStatus.QuestCompleted);
    }

    [Fact]
    public void VersionPack_Loads_Npcs_Shops_Quests_13()
    {
        var loader = new Legend.Core.VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.4.0", pack.Manifest.ProtocolVersion);
        Assert.True(pack.Npcs.ContainsKey("npc_village_merchant"));
        Assert.True(pack.Shops.ContainsKey("shop_novice_general"));
        Assert.True(pack.Quests.ContainsKey("quest_slime_hunt"));
        Assert.Equal(2, pack.Quests["quest_slime_hunt"].TargetCount);
    }

    [Fact]
    public async Task Shop_Rejects_Insufficient_Gold_And_Far_Npc()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.4.0");
        await client.RegisterAndLoginAsync("m3_poor", "secret1");
        var create = await client.CreateCharAsync("穷鬼", "warrior");
        await client.SelectCharAsync(create.Character.CharacterId);
        var enter = await client.EnterWorldAsync("realm-classic-01", create.Character.CharacterId);
        Assert.True(enter.Success);

        var npc = enter.NearbyEntities.First(e => e.Kind == EntityKind.EntityNpc);
        await WalkToAsync(client, npc.X, npc.Y);

        // Drain gold by buying dried meat (5g) until can't buy expensive stack
        // Buy 11 hp potions at 10g = 110 > 100
        var fail = await client.ShopBuyAsync(npc.EntityId, "item_hp_potion", 11);
        Assert.False(fail.Success);
        Assert.Equal(ProtocolErrorCode.ErrNotEnoughGold, fail.ErrorCode);

        await WalkToAsync(client, 25, 10);
        var far = await client.TalkAsync(npc.EntityId);
        Assert.False(far.Success);
        Assert.Equal(ProtocolErrorCode.ErrNpcTooFar, far.ErrorCode);
    }
}
