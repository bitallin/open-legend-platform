using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M14IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.20.0", $"pc-{user}", ct)).Accepted);
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
        // Prefer Y then X to avoid novice_plain portal at (30,10) on the spawn row.
        async Task StepToward(int gx, int gy)
        {
            for (int guard = 0; guard < 160; guard++)
            {
                Assert.NotNull(c.SelfEntity);
                if (c.SelfEntity.X == gx && c.SelfEntity.Y == gy) return;
                int nx = c.SelfEntity.X + Math.Sign(gx - c.SelfEntity.X);
                int ny = c.SelfEntity.Y + Math.Sign(gy - c.SelfEntity.Y);
                if (nx != c.SelfEntity.X && ny != c.SelfEntity.Y)
                {
                    var tryY = await c.MoveAsync(c.SelfEntity.X, ny, ct: ct);
                    if (tryY.Accepted) continue;
                    var tryX = await c.MoveAsync(nx, c.SelfEntity.Y, ct: ct);
                    if (tryX.Accepted) continue;
                }
                var m = await c.MoveAsync(nx, ny, ct: ct);
                if (!m.Accepted)
                {
                    bool moved = false;
                    foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1) })
                    {
                        int ax = c.SelfEntity.X + dx, ay = c.SelfEntity.Y + dy;
                        int before = Math.Abs(c.SelfEntity.X - gx) + Math.Abs(c.SelfEntity.Y - gy);
                        int after = Math.Abs(ax - gx) + Math.Abs(ay - gy);
                        if (after > before + 1) continue;
                        var alt = await c.MoveAsync(ax, ay, ct: ct);
                        if (alt.Accepted) { moved = true; break; }
                    }
                    if (!moved)
                        throw new Exception($"move stuck ({c.SelfEntity.X},{c.SelfEntity.Y})->({gx},{gy}): {m.Reason}");
                }
            }
            throw new Exception($"walk timeout to ({gx},{gy})");
        }

        Assert.NotNull(c.SelfEntity);
        await StepToward(c.SelfEntity.X, ty);
        await StepToward(tx, ty);
    }



    [Fact]
    public async Task WorldBoss_ForceSpawn_StatusAlive()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (client, _, _) = await EnterAsAsync(engine, $"m14u1_{suffix}", $"Wb{suffix}", "warrior");
            try
            {
                while (client.ReceivedWorldBossSpawns.TryDequeue(out _)) { }
                var ctrl = await client.WorldBossControlAsync("world_green_dragon", "force_spawn");
                Assert.True(ctrl.Success, ctrl.Message);
                Assert.NotNull(ctrl.Boss);
                Assert.True(ctrl.Boss.Alive);
                Assert.False(string.IsNullOrEmpty(ctrl.Boss.EntityId));

                var st = await client.WorldBossStatusAsync("world_green_dragon");
                Assert.True(st.Success);
                Assert.Single(st.Bosses);
                Assert.True(st.Bosses[0].Alive);
                Assert.Equal(ctrl.Boss.EntityId, st.Bosses[0].EntityId);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task WorldBoss_TopHate_GetsRareLoot_AndWorldBroadcast()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (a, _, _) = await EnterAsAsync(engine, $"m14a_{suffix}", $"HateL{suffix}", "warrior");
            var (b, _, _) = await EnterAsAsync(engine, $"m14b_{suffix}", $"HateH{suffix}", "mage");
            var (s, _, _) = await EnterAsAsync(engine, $"m14s_{suffix}", $"Watch{suffix}", "taoist");
            try
            {
                while (a.ReceivedWorldBossSpawns.TryDequeue(out _)) { }
                while (s.ReceivedLootBroadcasts.TryDequeue(out _)) { }
                while (s.ReceivedServerNotices.TryDequeue(out _)) { }

                var force = await a.WorldBossControlAsync("world_green_dragon", "force_spawn");
                Assert.True(force.Success, force.Message);
                var spawn = await s.WaitWorldBossSpawnAsync(5000);
                Assert.Equal("world_green_dragon", spawn.BossId);

                string entityId = force.Boss.EntityId;
                int bx = force.Boss.X;
                int by = force.Boss.Y;
                await WalkAsync(a, bx - 1, by);
                await WalkAsync(b, bx + 1, by);

                var (atkA, csA) = await a.AttackAsync(entityId, "basic_slash");
                Assert.True(atkA.Success, atkA.Message);
                Assert.NotNull(csA);
                Assert.True(csA.Damage > 0);

                bool dead = false;
                for (int i = 0; i < 50; i++)
                {
                    var (atkB, csB) = await b.AttackAsync(entityId, "basic_slash");
                    if (atkB.Success && csB != null && (csB.TargetDied || csB.TargetHp <= 0))
                    {
                        dead = true;
                        break;
                    }
                    await Task.Delay(40);
                }
                Assert.True(dead, "boss should die");

                var loot = await s.WaitLootBroadcastAsync(8000);
                Assert.Equal($"HateH{suffix}", loot.KillerName);
                Assert.Equal("item_dragon_scale", loot.ItemId);
                Assert.Equal("rare_loot", loot.NoticeType);

                bool noticed = false;
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline && !noticed)
                {
                    while (s.ReceivedServerNotices.TryDequeue(out var n))
                    {
                        if (n.NoticeType == "rare_loot")
                        {
                            noticed = true;
                            break;
                        }
                    }
                    if (!noticed) await Task.Delay(40);
                }
                Assert.True(noticed, "spectator should get ServerNotice rare_loot");

                var st = await a.WorldBossStatusAsync("world_green_dragon");
                Assert.True(st.Success);
                Assert.False(st.Bosses[0].Alive);
                var top = st.Bosses[0].HateTop.OrderBy(h => h.Rank).ToList();
                Assert.True(top.Count >= 2);
                Assert.Equal($"HateH{suffix}", top[0].Name);
                Assert.True(top[0].Damage > top[1].Damage);
            }
            finally
            {
                await a.DisposeAsync();
                await b.DisposeAsync();
                await s.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task WorldBoss_UnknownBoss_Denied()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (client, _, _) = await EnterAsAsync(engine, $"m14x_{suffix}", $"Deny{suffix}", "warrior");
            try
            {
                var st = await client.WorldBossStatusAsync("no_such_boss");
                Assert.False(st.Success);
                Assert.Equal(ProtocolErrorCode.ErrWorldBossDenied, st.ErrorCode);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public void WorldBoss_VersionPack_LoadsBossConfig()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.20.0", pack.Manifest.ProtocolVersion);
        Assert.NotEmpty(pack.WorldBoss.Bosses);
        Assert.Contains(pack.WorldBoss.Bosses, b => b.BossId == "world_green_dragon");
        Assert.True(pack.Monsters.ContainsKey("world_green_dragon"));
        Assert.Contains(pack.WorldBoss.Bosses[0].RareDrops, d => d.ItemId == "item_dragon_scale" && d.Broadcast);
    }
}
