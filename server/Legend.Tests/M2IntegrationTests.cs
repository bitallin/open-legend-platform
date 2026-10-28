using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M2IntegrationTests
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

    [Fact]
    public async Task Kill_Drop_Pickup_InventoryPersists_AfterRestart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m2-{Guid.NewGuid():N}.db");
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await client.HandshakeAsync("1.7.0")).Accepted);
        await client.RegisterAndLoginAsync("m2_hunter", "secret1");
        var create = await client.CreateCharAsync("猎手甲", "warrior");
        Assert.True(create.Success, create.Message);
        string charId = create.Character.CharacterId;
        await client.SelectCharAsync(charId);
        var enter = await client.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter.Success);

        // Monsters should appear in nearby (or after walking)
        var monster = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
        if (monster is null)
        {
            // Walk toward known spawn (25,10)
            await WalkToAsync(client, 22, 10);
            for (int i = 0; i < 20 && monster is null; i++)
            {
                while (client.ReceivedEntitySpawns.TryDequeue(out var sp))
                {
                    if (sp.Entity.Kind == EntityKind.EntityMonster)
                    {
                        monster = sp.Entity;
                        break;
                    }
                }
                // re-query by attacking known pattern — re-enter nearby via move
                if (monster is null)
                    await client.MoveAsync(client.SelfEntity!.X + 1, client.SelfEntity.Y);
                await Task.Delay(50);
            }
        }
        Assert.NotNull(monster);

        // Leave safe zone if needed (safe is 5..18)
        if (client.SelfEntity!.InSafeZone || client.SelfEntity.X <= 18)
            await WalkToAsync(client, 22, 10);

        // Get adjacent to monster
        int mx = monster!.X, my = monster.Y;
        int ax = mx - 1, ay = my;
        await WalkToAsync(client, ax, ay);

        // Kill with power_strike (one or two hits)
        string? lootId = null;
        int lootX = mx, lootY = my;
        for (int hit = 0; hit < 6 && lootId is null; hit++)
        {
            string skill = hit == 0 ? "power_strike" : "basic_slash";
            if (hit > 0)
                await Task.Delay(650);
            var (atk, combat) = await client.AttackAsync(monster.EntityId, skill);
            Assert.True(atk.Success, $"Attack failed: {atk.ErrorCode} {atk.Message}");
            Assert.NotNull(combat);

            for (int i = 0; i < 30 && lootId is null; i++)
            {
                while (client.ReceivedLootDrops.TryDequeue(out var drop))
                {
                    lootId = drop.Loot.EntityId;
                    lootX = drop.Loot.X;
                    lootY = drop.Loot.Y;
                }
                if (lootId is null)
                    await Task.Delay(30);
            }

            if (combat!.TargetDied)
                break;
        }
        Assert.False(string.IsNullOrEmpty(lootId), "Expected loot drop after kill");

        // PickupRange=2; stand on loot cell
        await WalkToAsync(client, lootX, lootY);
        var pickup = await client.PickupAsync(lootId!);
        Assert.True(pickup.Success, pickup.Message);
        Assert.True(pickup.Inventory.Count > 0);
        int invCount = pickup.Inventory.Sum(i => i.Count);

        await client.DisposeAsync();
        await engine.DisposeAsync(); // stop first host before reopen same sqlite path

        // Restart host — inventory must persist
        var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        await using var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
        await engine2.StartAsync();
        await using var client2 = new LegendTcpClient();
        await client2.ConnectAsync("127.0.0.1", engine2.BoundPort);
        await client2.HandshakeAsync("1.7.0");
        await client2.LoginAsync("m2_hunter", "secret1");
        await client2.SelectCharAsync(charId);
        var enter2 = await client2.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter2.Success);
        Assert.True(enter2.Inventory.Count > 0, "Inventory empty after restart");
        Assert.True(enter2.Inventory.Sum(i => i.Count) >= invCount);
        try { File.Delete(dbPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task SafeZone_Blocks_All_Attacks()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();
        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.7.0");
        await client.RegisterAndLoginAsync("safe_user", "secret1");
        var create = await client.CreateCharAsync("安全员", "warrior");
        await client.SelectCharAsync(create.Character.CharacterId);
        var enter = await client.EnterWorldAsync("realm-classic-01", create.Character.CharacterId);
        Assert.True(enter.InSafeZone);

        var (resp, _) = await client.AttackAsync("mob_fake", "basic_slash");
        Assert.False(resp.Success);
        Assert.Equal(ProtocolErrorCode.ErrSafeZone, resp.ErrorCode);
    }

    [Fact]
    public async Task InvalidSkill_And_Cooldown_Rejected()
    {
        await using var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, new InMemoryStorage());
        await engine.StartAsync();
        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        await client.HandshakeAsync("1.7.0");
        await client.RegisterAndLoginAsync("cd_user", "secret1");
        var create = await client.CreateCharAsync("冷却侠", "warrior");
        await client.SelectCharAsync(create.Character.CharacterId);
        var enter = await client.EnterWorldAsync("realm-classic-01", create.Character.CharacterId);
        Assert.True(enter.Success);

        // Leave safe zone and approach a monster
        await WalkToAsync(client, 24, 10);
        var monster = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
        for (int i = 0; i < 30 && monster is null; i++)
        {
            while (client.ReceivedEntitySpawns.TryDequeue(out var sp))
            {
                if (sp.Entity.Kind == EntityKind.EntityMonster) { monster = sp.Entity; break; }
            }
            if (monster is null)
            {
                // nudge to reveal
                int x = client.SelfEntity!.X;
                await client.MoveAsync(Math.Min(x + 1, 28), 10);
                await Task.Delay(30);
            }
        }
        Assert.NotNull(monster);
        await WalkToAsync(client, monster!.X - 1, monster.Y);

        var (bad, _) = await client.AttackAsync(monster.EntityId, "no_such_skill");
        Assert.False(bad.Success);
        Assert.Equal(ProtocolErrorCode.ErrInvalidSkill, bad.ErrorCode);

        var (ok1, _) = await client.AttackAsync(monster.EntityId, "power_strike");
        Assert.True(ok1.Success, ok1.Message);
        var (cd, _) = await client.AttackAsync(monster.EntityId, "power_strike");
        Assert.False(cd.Success);
        Assert.Equal(ProtocolErrorCode.ErrSkillCooldown, cd.ErrorCode);
    }

    [Fact]
    public void VersionPack_Loads_Skills_Monsters_Loot()
    {
        var pack = new Legend.Core.VersionPackLoader().LoadFromManifest(GetManifestPath());
        Assert.True(pack.Skills.ContainsKey("basic_slash"));
        Assert.True(pack.Monsters.ContainsKey("slime_green"));
        Assert.True(pack.LootTables.ContainsKey("slime_basic"));
        Assert.NotEmpty(pack.MonsterSpawns);
        Assert.Equal("1.7.0", pack.Manifest.ProtocolVersion);
    }

    [Fact]
    public void CombatMath_Applies_Def_And_Floor()
    {
        Assert.Equal(24, Legend.Game.CombatMath.CalcDamage(25, 1, 1.0));
        Assert.Equal(1, Legend.Game.CombatMath.CalcDamage(5, 100, 1.0));
        Assert.Equal(42, Legend.Game.CombatMath.CalcDamage(25, 1, 1.75));
    }
}
