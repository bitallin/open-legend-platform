using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M4IntegrationTests
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

    private static async Task<EntitySnapshot> FindMonsterAsync(LegendTcpClient client, EnterWorldResp enter, string? excludeId = null)
    {
        EntitySnapshot? monster = enter.NearbyEntities.FirstOrDefault(
            e => e.Kind == EntityKind.EntityMonster && e.EntityId != excludeId);
        int[][] probes = { new[] { 25, 10 }, new[] { 28, 12 }, new[] { 24, 10 }, new[] { 22, 10 } };
        for (int i = 0; i < 40 && monster is null; i++)
        {
            while (client.ReceivedAoiEnters.TryDequeue(out var en))
            {
                if (en.Entity.Kind == EntityKind.EntityMonster && en.Entity.EntityId != excludeId)
                {
                    monster = en.Entity;
                    break;
                }
            }
            while (monster is null && client.ReceivedEntitySpawns.TryDequeue(out var sp))
            {
                if (sp.Entity.Kind == EntityKind.EntityMonster && sp.Entity.EntityId != excludeId)
                {
                    monster = sp.Entity;
                    break;
                }
            }
            if (monster is null)
            {
                var p = probes[i % probes.Length];
                await WalkToAsync(client, p[0], p[1]);
                await Task.Delay(40);
            }
        }
        Assert.NotNull(monster);
        return monster!;
    }

    [Fact]
    public async Task Equip_CombatDamageIncrease_Enhance_And_Persist_AfterRestart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m4-{Guid.NewGuid():N}.db");
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await client.HandshakeAsync("1.10.0")).Accepted);
        await client.RegisterAndLoginAsync("m4_hero", "secret123");
        var create = await client.CreateCharAsync("铁匠武者", "warrior");
        Assert.True(create.Success, create.Message);
        string charId = create.Character.CharacterId;
        await client.SelectCharAsync(charId);

        var enter = await client.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter.Success);
        Assert.Equal(25, enter.EffectiveAtk);
        Assert.Equal(0, enter.EffectiveDef);
        Assert.Equal(180, enter.EffectiveMaxHp);
        Assert.Empty(enter.Equipment);

        // 1. Measure unequipped damage on monster
        var monster = await FindMonsterAsync(client, enter);
        await WalkToAsync(client, monster.X - 1, monster.Y);
        var (atk1, cs1) = await client.AttackAsync(monster.EntityId, "basic_slash");
        Assert.True(atk1.Success, atk1.Message);
        Assert.NotNull(cs1);
        int unequippedDamage = cs1!.Damage;
        Assert.Equal(24, unequippedDamage); // Warrior 25 Atk - Slime 1 Def = 24

        // 2. Walk to NPC merchant (12,12), buy wooden sword (20g) & cloth armor (15g)
        await WalkToAsync(client, 12, 12);
        var buySword = await client.ShopBuyAsync("npc_village_merchant", "item_wooden_sword", 1);
        Assert.True(buySword.Success, buySword.Message);
        Assert.Equal(80, buySword.Gold);

        var buyArmor = await client.ShopBuyAsync("npc_village_merchant", "item_cloth_armor", 1);
        Assert.True(buyArmor.Success, buyArmor.Message);
        Assert.Equal(65, buyArmor.Gold);

        // 3. Equip sword (+10 Atk) and armor (+5 Def, +30 MaxHp)
        var eqSword = await client.EquipAsync("item_wooden_sword", "weapon");
        Assert.True(eqSword.Success, eqSword.Message);
        Assert.Equal(35, eqSword.EffectiveAtk);
        Assert.DoesNotContain(eqSword.Inventory, i => i.ItemId == "item_wooden_sword");
        Assert.Contains(eqSword.Equipment, e => e.Slot == "weapon" && e.ItemId == "item_wooden_sword" && e.EnhanceLevel == 0);

        var eqArmor = await client.EquipAsync("item_cloth_armor", "armor");
        Assert.True(eqArmor.Success, eqArmor.Message);
        Assert.Equal(35, eqArmor.EffectiveAtk);
        Assert.Equal(5, eqArmor.EffectiveDef);
        Assert.Equal(210, eqArmor.EffectiveMaxHp);

        // 4. Attack monster with equipped weapon — verify damage increased
        var monsterAfterEquip = cs1.TargetDied ? await FindMonsterAsync(client, enter, monster.EntityId) : monster;
        await WalkToAsync(client, monsterAfterEquip.X - 1, monsterAfterEquip.Y);
        await Task.Delay(650);
        var (atk2, cs2) = await client.AttackAsync(monsterAfterEquip.EntityId, "basic_slash");
        Assert.True(atk2.Success, atk2.Message);
        Assert.NotNull(cs2);
        int equippedDamage = cs2!.Damage;
        Assert.True(equippedDamage > unequippedDamage, $"Expected equippedDamage ({equippedDamage}) > unequippedDamage ({unequippedDamage})");
        Assert.Equal(34, equippedDamage); // 35 Atk - 1 Def = 34

        // 5. Enhance weapon to +1 (cost 20g, +4 Atk -> 39) and +2 (cost 30g, +4 Atk -> 43)
        var enh1 = await client.EnhanceEquipAsync("weapon");
        Assert.True(enh1.Success && enh1.EnhanceSucceeded, enh1.Message);
        Assert.Equal(1, enh1.UpdatedSlot.EnhanceLevel);
        Assert.Equal(14, enh1.UpdatedSlot.AtkBonus); // 10 base + 4 enhance
        Assert.Equal(39, enh1.EffectiveAtk);
        Assert.Equal(45, enh1.Gold);

        var enh2 = await client.EnhanceEquipAsync("weapon");
        Assert.True(enh2.Success && enh2.EnhanceSucceeded, enh2.Message);
        Assert.Equal(2, enh2.UpdatedSlot.EnhanceLevel);
        Assert.Equal(18, enh2.UpdatedSlot.AtkBonus); // 10 base + 8 enhance
        Assert.Equal(43, enh2.EffectiveAtk);
        Assert.Equal(15, enh2.Gold);

        // 6. Attack monster after enhancement — verify damage increased again
        var monsterAfterEnhance = cs2.TargetDied ? await FindMonsterAsync(client, enter, monsterAfterEquip.EntityId) : monsterAfterEquip;
        await WalkToAsync(client, monsterAfterEnhance.X - 1, monsterAfterEnhance.Y);
        await Task.Delay(650);
        var (atk3, cs3) = await client.AttackAsync(monsterAfterEnhance.EntityId, "basic_slash");
        Assert.True(atk3.Success, atk3.Message);
        Assert.NotNull(cs3);
        Assert.True(cs3!.Damage > equippedDamage);
        Assert.Equal(42, cs3.Damage); // 43 Atk - 1 Def = 42

        await client.DisposeAsync();
        await engine.DisposeAsync();

        // 7. Restart Host on same Sqlite DB — verify equipment, enhance level, stats, and gold survive
        var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
        await engine2.StartAsync();

        var client2 = new LegendTcpClient();
        await client2.ConnectAsync("127.0.0.1", engine2.BoundPort);
        await client2.HandshakeAsync("1.10.0");
        var login2 = await client2.LoginAsync("m4_hero", "secret123");
        Assert.True(login2.Success);
        await client2.SelectCharAsync(charId);
        var enter2 = await client2.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter2.Success);
        Assert.Equal(15, enter2.Gold);
        Assert.Equal(43, enter2.EffectiveAtk);
        Assert.Equal(5, enter2.EffectiveDef);
        Assert.Equal(210, enter2.EffectiveMaxHp);
        Assert.Contains(enter2.Equipment, e => e.Slot == "weapon" && e.ItemId == "item_wooden_sword" && e.EnhanceLevel == 2 && e.AtkBonus == 18);
        Assert.Contains(enter2.Equipment, e => e.Slot == "armor" && e.ItemId == "item_cloth_armor" && e.EnhanceLevel == 0 && e.DefBonus == 5);

        // 8. Unequip armor and verify stats recalculate and item returns to bag
        var unequip = await client2.UnequipAsync("armor");
        Assert.True(unequip.Success, unequip.Message);
        Assert.Equal(43, unequip.EffectiveAtk);
        Assert.Equal(0, unequip.EffectiveDef);
        Assert.Equal(180, unequip.EffectiveMaxHp);
        Assert.Contains(unequip.Inventory, i => i.ItemId == "item_cloth_armor" && i.Count == 1);

        await client2.DisposeAsync();
        await engine2.DisposeAsync();
        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public async Task Equip_And_Enhance_Validation_Guards_Work()
    {
        var storage = new InMemoryStorage();
        await storage.InitializeAsync();
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.10.0");
        await client.RegisterAndLoginAsync("m4_guard", "secret123");
        var create = await client.CreateCharAsync("校验测试", "warrior");
        await client.SelectCharAsync(create.Character.CharacterId);
        await client.EnterWorldAsync("realm-classic-01", create.Character.CharacterId);

        // Equip item not in inventory -> ErrEquipDenied
        var eqMissing = await client.EquipAsync("item_wooden_sword", "weapon");
        Assert.False(eqMissing.Success);
        Assert.Equal(ProtocolErrorCode.ErrEquipDenied, eqMissing.ErrorCode);

        // Enhance empty slot -> ErrEnhanceDenied
        var enhEmpty = await client.EnhanceEquipAsync("weapon");
        Assert.False(enhEmpty.Success);
        Assert.Equal(ProtocolErrorCode.ErrEnhanceDenied, enhEmpty.ErrorCode);

        // Unequip empty slot -> ErrEquipDenied
        var uneqEmpty = await client.UnequipAsync("weapon");
        Assert.False(uneqEmpty.Success);
        Assert.Equal(ProtocolErrorCode.ErrEquipDenied, uneqEmpty.ErrorCode);

        // Buy sword (20g) and potions (8 * 10g = 80g) so 0 gold left, then equip in wrong slot, then equip properly and try enhance with 0 gold
        await WalkToAsync(client, 12, 12);
        await client.ShopBuyAsync("npc_village_merchant", "item_wooden_sword", 1);
        await client.ShopBuyAsync("npc_village_merchant", "item_hp_potion", 8);

        var eqWrongSlot = await client.EquipAsync("item_wooden_sword", "armor");
        Assert.False(eqWrongSlot.Success);
        Assert.Equal(ProtocolErrorCode.ErrEquipDenied, eqWrongSlot.ErrorCode);

        var eqOk = await client.EquipAsync("item_wooden_sword", "weapon");
        Assert.True(eqOk.Success);

        var enhNoGold = await client.EnhanceEquipAsync("weapon");
        Assert.False(enhNoGold.Success);
        Assert.Equal(ProtocolErrorCode.ErrNotEnoughGold, enhNoGold.ErrorCode);

        await client.DisposeAsync();
        await engine.DisposeAsync();
    }

    [Fact]
    public void VersionPack_1_4_0_Loads_Items_And_EnhanceRules()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.10.0", pack.Manifest.ProtocolVersion);
        Assert.True(pack.Items.ContainsKey("item_wooden_sword"));
        Assert.True(pack.Items.ContainsKey("item_cloth_armor"));
        Assert.True(pack.Items.ContainsKey("item_bronze_ring"));
        Assert.True(pack.EnhanceRules.ContainsKey(1));
        Assert.Equal(1.0, pack.EnhanceRules[1].SuccessRate);
        Assert.Contains("scripts/equip.lua", pack.LuaScripts.Keys);
    }
}
