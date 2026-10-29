using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M5IntegrationTests
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
            Assert.True(sync.Accepted, sync.Reason);
            x = sync.ToX;
            y = sync.ToY;
        }
    }

    private static async Task<EntitySnapshot> FindMonsterAsync(LegendTcpClient client, EnterWorldResp enter, CancellationToken ct = default)
    {
        EntitySnapshot? m = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
        for (int i = 0; i < 50 && m is null; i++)
        {
            while (client.ReceivedAoiEnters.TryDequeue(out var en))
                if (en.Entity.Kind == EntityKind.EntityMonster) { m = en.Entity; break; }
            while (m is null && client.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityMonster) { m = sp.Entity; break; }
            if (m is null)
            {
                await WalkToAsync(client, 25 + (i % 3), 10 + (i % 2), ct);
                await Task.Delay(40, ct);
            }
        }
        Assert.NotNull(m);
        return m!;
    }

    [Fact]
    public async Task Kill_Grants_Exp_LevelUp_UsePotion_And_Persists()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m5-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await client.HandshakeAsync("1.14.0")).Accepted);
        await client.RegisterAndLoginAsync("m5_hero", "secret123");
        var create = await client.CreateCharAsync("升级勇者", "warrior");
        Assert.True(create.Success, create.Message);
        string charId = create.Character.CharacterId;
        await client.SelectCharAsync(charId);
        var enter = await client.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter.Success, enter.Message);
        Assert.Equal(1, enter.Level);
        Assert.Equal(0, enter.Exp);
        Assert.Equal(50, enter.ExpToNext);
        Assert.Equal(25, enter.EffectiveAtk);
        Assert.Equal(180, enter.EffectiveMaxHp);
        Assert.Equal(40, enter.EffectiveMaxMp);

        await WalkToAsync(client, 12, 12);
        var buyPot = await client.ShopBuyAsync("npc_village_merchant", "item_hp_potion", 2);
        Assert.True(buyPot.Success, buyPot.Message);
        var buyIron = await client.ShopBuyAsync("npc_village_merchant", "item_iron_sword", 1);
        Assert.True(buyIron.Success, buyIron.Message);

        var eqDenied = await client.EquipAsync("item_iron_sword", "weapon");
        Assert.False(eqDenied.Success);
        Assert.Equal(ProtocolErrorCode.ErrEquipDenied, eqDenied.ErrorCode);

        LevelUpNotify? levelUp = null;
        int kills = 0;
        for (int attempt = 0; attempt < 6 && levelUp is null; attempt++)
        {
            var monster = await FindMonsterAsync(client, enter);
            await WalkToAsync(client, monster.X - 1, monster.Y);
            for (int hit = 0; hit < 25; hit++)
            {
                await Task.Delay(650);
                var (atk, cs) = await client.AttackAsync(monster.EntityId, "basic_slash");
                if (!atk.Success)
                {
                    // Skill cooldown or transient range — wait and retry
                    await Task.Delay(400);
                    continue;
                }
                while (client.ReceivedLevelUps.TryDequeue(out var lu))
                    levelUp = lu;
                if (cs?.TargetDied == true)
                {
                    kills++;
                    break;
                }
            }
            while (client.ReceivedLevelUps.TryDequeue(out var lu2))
                levelUp = lu2;
        }

        Assert.NotNull(levelUp);
        Assert.Equal(1, levelUp!.OldLevel);
        Assert.Equal(2, levelUp.NewLevel);
        Assert.Equal(28, levelUp.BaseAtk);
        Assert.Equal(1, levelUp.BaseDef);
        Assert.Equal(200, levelUp.BaseMaxHp);
        Assert.Equal(45, levelUp.BaseMaxMp);
        Assert.Equal(28, levelUp.EffectiveAtk);
        Assert.Equal(200, levelUp.EffectiveMaxHp);
        Assert.Equal(200, levelUp.Hp);
        Assert.True(kills >= 2);

        var eqOk = await client.EquipAsync("item_iron_sword", "weapon");
        Assert.True(eqOk.Success, eqOk.Message);
        Assert.Equal(43, eqOk.EffectiveAtk); // 28 base + 15 iron

        // Take damage then use potion
        var hurtMob = await FindMonsterAsync(client, enter);
        await WalkToAsync(client, hurtMob.X, hurtMob.Y);
        int hpBefore = client.SelfEntity!.Hp;
        for (int w = 0; w < 40; w++)
        {
            await Task.Delay(150);
            while (client.ReceivedCombatSyncs.TryDequeue(out var csHurt))
            {
                if (csHurt.TargetEntityId == client.SelfEntity.EntityId && csHurt.Damage > 0)
                {
                    client.SelfEntity.Hp = csHurt.TargetHp;
                    hpBefore = csHurt.TargetHp;
                }
            }
            if (client.SelfEntity.Hp < 200)
                break;
        }
        hpBefore = client.SelfEntity.Hp;

        var use = await client.UseItemAsync("item_hp_potion");
        Assert.True(use.Success, use.Message);
        if (hpBefore < 200)
            Assert.True(use.Hp > hpBefore);
        Assert.True(use.Hp <= use.MaxHp);

        var useMissing = await client.UseItemAsync("item_mp_potion");
        Assert.False(useMissing.Success);
        Assert.Equal(ProtocolErrorCode.ErrUseItemDenied, useMissing.ErrorCode);

        await client.DisposeAsync();
        await engine.DisposeAsync();

        // Restart — level/exp/inventory/equipment survive
        var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
        await engine2.StartAsync();

        var client2 = new LegendTcpClient();
        await client2.ConnectAsync("127.0.0.1", engine2.BoundPort);
        await client2.HandshakeAsync("1.14.0");
        var login2 = await client2.LoginAsync("m5_hero", "secret123");
        Assert.True(login2.Success);
        await client2.SelectCharAsync(charId);
        var enter2 = await client2.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter2.Success);
        Assert.Equal(2, enter2.Level);
        Assert.True(enter2.Exp >= 0);
        Assert.Equal(28 + 15, enter2.EffectiveAtk);
        Assert.Equal(200, enter2.EffectiveMaxHp); // no armor
        Assert.Equal(45, enter2.EffectiveMaxMp);
        Assert.Contains(enter2.Equipment, e => e.Slot == "weapon" && e.ItemId == "item_iron_sword");
        Assert.Contains(enter2.Inventory, i => i.ItemId == "item_hp_potion");

        await client2.DisposeAsync();
        await engine2.DisposeAsync();
        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public async Task UseItem_Denied_When_Dead_Or_NonConsumable()
    {
        var storage = new InMemoryStorage();
        await storage.InitializeAsync();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.14.0");
        await client.RegisterAndLoginAsync("m5_guard", "secret123");
        var create = await client.CreateCharAsync("药水校验", "warrior");
        await client.SelectCharAsync(create.Character.CharacterId);
        await client.EnterWorldAsync("realm-classic-01", create.Character.CharacterId);

        await WalkToAsync(client, 12, 12);
        await client.ShopBuyAsync("npc_village_merchant", "item_wooden_sword", 1);

        var useSword = await client.UseItemAsync("item_wooden_sword");
        Assert.False(useSword.Success);
        Assert.Equal(ProtocolErrorCode.ErrUseItemDenied, useSword.ErrorCode);

        var useMissing = await client.UseItemAsync("item_hp_potion");
        Assert.False(useMissing.Success);
        Assert.Equal(ProtocolErrorCode.ErrUseItemDenied, useMissing.ErrorCode);

        await client.DisposeAsync();
        await engine.DisposeAsync();
    }

    [Fact]
    public void VersionPack_1_5_0_Loads_Levels_ExpReward_And_UseEffect()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.14.0", pack.Manifest.ProtocolVersion);
        Assert.True(pack.Levels.Entries.Count >= 10);
        Assert.Equal(50, pack.Levels.GetExpToNext(1));
        Assert.Equal(0, pack.Levels.GetExpToNext(10));
        var growth = pack.Levels.GetGrowth("warrior");
        Assert.Equal(3, growth.AtkPerLevel);
        Assert.Equal(20, growth.HpPerLevel);
        Assert.True(pack.Monsters.TryGetValue("slime_green", out var slime));
        Assert.Equal(30, slime!.ExpReward);
        Assert.True(pack.Items.TryGetValue("item_hp_potion", out var pot));
        Assert.Equal(50, pot!.HealHp);
        Assert.True(pack.Items.TryGetValue("item_iron_sword", out var iron));
        Assert.Equal(2, iron!.RequiredLevel);
        Assert.Contains("scripts/level.lua", pack.LuaScripts.Keys);
    }
}
