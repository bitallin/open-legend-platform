using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M12IntegrationTests
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

    private static async Task<(LegendTcpClient Client, string CharId, EnterWorldResp Enter)> EnterAsAsync(
        LegendServerEngine engine, string user, string charName, string cls, CancellationToken ct = default)
    {
        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort, ct);
        Assert.True((await client.HandshakeAsync("1.12.0", $"pc-{user}", ct)).Accepted);
        await client.RegisterAsync(user, "secret12345", "PC", ct);
        var login = await client.LoginAsync(user, "secret12345", "PC", ct);
        Assert.True(login.Success, login.Message);
        var create = await client.CreateCharAsync(charName, cls, ct);
        string charId;
        if (create.Success)
            charId = create.Character.CharacterId;
        else
        {
            var list = await client.CharListAsync(ct);
            charId = list.Characters.First(c => c.Name == charName).CharacterId;
        }
        await client.SelectCharAsync(charId, ct);
        var realms = await client.GetRealmListAsync(ct);
        var enter = await client.EnterWorldAsync(realms.Realms[0].RealmId, charId, ct);
        Assert.True(enter.Success, enter.Message);
        return (client, charId, enter);
    }

    private static async Task WalkAsync(LegendTcpClient c, int tx, int ty, CancellationToken ct = default)
    {
        while (c.SelfEntity!.X != tx || c.SelfEntity.Y != ty)
        {
            int nx = c.SelfEntity.X + Math.Sign(tx - c.SelfEntity.X);
            int ny = c.SelfEntity.Y + Math.Sign(ty - c.SelfEntity.Y);
            var m = await c.MoveAsync(nx, ny, ct: ct);
            Assert.True(m.Accepted, $"move to ({nx},{ny}) rejected");
        }
    }

    private static async Task KillAsync(LegendTcpClient c, string entityId, CancellationToken ct = default)
    {
        for (int i = 0; i < 40; i++)
        {
            var (atk, cs) = await c.AttackAsync(entityId, "basic_slash", ct);
            if (atk.Success && cs != null && (cs.TargetDied || cs.TargetHp <= 0))
                return;
            await Task.Delay(40, ct);
        }
        throw new Exception($"Failed to kill {entityId}");
    }

    [Fact]
    public async Task Dungeon_LevelGate_Enter_Isolate_Clear_Reward_Leave()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (hero, heroId, enterH) = await EnterAsAsync(engine, $"m12a_{suffix}", $"洞{suffix}", "warrior");
            var (other, _, enterO) = await EnterAsAsync(engine, $"m12b_{suffix}", $"路{suffix}", "mage");
            try
            {
                var gate = await hero.DungeonEnterAsync("dungeon_level_gate");
                Assert.False(gate.Success);
                Assert.Equal(ProtocolErrorCode.ErrDungeonLevel, gate.ErrorCode);

                int goldBefore = hero.Gold;
                var enter = await hero.DungeonEnterAsync("dungeon_slime_cave");
                Assert.True(enter.Success, enter.Message);
                Assert.StartsWith("inst_", enter.MapId);
                Assert.Equal(goldBefore - 10, enter.Gold);
                Assert.Equal(2, enter.TargetKills);

                // Isolation: other remains on novice_plain and must not receive hero AOI enter for dungeon map
                Assert.Equal("novice_plain", other.SelfEntity!.MapId);
                await WalkAsync(other, 10, 10);
                await Task.Delay(150);
                bool saw = false;
                while (other.ReceivedAoiEnters.TryDequeue(out var ae))
                {
                    if (ae.Entity.EntityId == hero.SelfEntity!.EntityId)
                        saw = true;
                }
                Assert.False(saw);

                var mobs = new List<EntitySnapshot>();
                while (hero.ReceivedMapChanges.TryDequeue(out var mc))
                {
                    foreach (var e in mc.NearbyEntities)
                        if (e.Kind == EntityKind.EntityMonster) mobs.Add(e);
                }
                for (int i = 0; i < 40 && mobs.Count < enter.TargetKills; i++)
                {
                    while (hero.ReceivedEntitySpawns.TryDequeue(out var sp))
                        if (sp.Entity.Kind == EntityKind.EntityMonster) mobs.Add(sp.Entity);
                    await Task.Delay(40);
                }
                mobs = mobs.GroupBy(m => m.EntityId).Select(g => g.First()).ToList();
                Assert.True(mobs.Count >= enter.TargetKills, $"mobs={mobs.Count}");

                foreach (var mob in mobs.Take(enter.TargetKills))
                {
                    await WalkAsync(hero, mob.X, mob.Y);
                    await KillAsync(hero, mob.EntityId);
                }

                var complete = await hero.WaitDungeonCompleteAsync(15000);
                Assert.Equal(50, complete.RewardGold);
                Assert.Equal("novice_plain", complete.ExitMapId);
                Assert.Equal("novice_plain", hero.SelfEntity!.MapId);
                Assert.True(hero.Gold >= goldBefore - 10 + 50);

                var enter2 = await hero.DungeonEnterAsync("dungeon_slime_cave");
                Assert.True(enter2.Success, enter2.Message);
                var leave = await hero.DungeonLeaveAsync();
                Assert.True(leave.Success, leave.Message);
                Assert.Equal("novice_plain", leave.MapId);
            }
            finally
            {
                await hero.DisposeAsync();
                await other.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dungeon_HostRestart_SanitizesStaleInstanceToTown()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m12-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }
        string suffix = Guid.NewGuid().ToString("N")[..4];
        string user = $"m12r_{suffix}";
        string name = $"安{suffix}";
        string charId;
        string staleMap;

        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (client, cid, _) = await EnterAsAsync(engine, user, name, "warrior");
                charId = cid;
                try
                {
                    var enter = await client.DungeonEnterAsync("dungeon_slime_cave");
                    Assert.True(enter.Success, enter.Message);
                    staleMap = enter.MapId;
                    Assert.StartsWith("inst_", staleMap);
                }
                finally
                {
                    await client.DisposeAsync(); // normal disconnect persists town exit
                }
            }
            finally
            {
                await engine.DisposeAsync();
            }

            // Simulate crash that persisted inst_ without disconnect cleanup
            var storageCorrupt = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            await storageCorrupt.Characters.SavePositionAsync(charId, staleMap, 5, 5, 100, 50);
            await storageCorrupt.DisposeAsync();
        }

        {
            var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var rec = await storage2.Characters.GetByIdAsync(charId);
            Assert.NotNull(rec);
            Assert.StartsWith("inst_", rec!.MapId); // persisted stale

            var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
            await engine2.StartAsync();
            try
            {
                var (client2, _, enter2) = await EnterAsAsync(engine2, user, name, "warrior");
                try
                {
                    Assert.False(enter2.SelfEntity.MapId.StartsWith("inst_", StringComparison.OrdinalIgnoreCase));
                    Assert.Equal("novice_plain", enter2.SelfEntity.MapId);
                }
                finally
                {
                    await client2.DisposeAsync();
                }
            }
            finally
            {
                await engine2.DisposeAsync();
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public void VersionPack_Loads_DungeonConfig_And_Map()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.12.0", pack.Manifest.ProtocolVersion);
        Assert.Contains(pack.Dungeons.Dungeons, d => d.DungeonId == "dungeon_slime_cave");
        Assert.True(pack.Maps.ContainsKey("dungeon_slime_cave"));
        Assert.True(pack.Monsters.ContainsKey("slime_cave"));
        Assert.Contains(pack.LuaScripts.Keys, k => k.Contains("dungeon", StringComparison.OrdinalIgnoreCase));
    }
}
