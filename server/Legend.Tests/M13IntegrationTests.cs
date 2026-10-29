using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M13IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.14.0", $"pc-{user}", ct)).Accepted);
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

    private static async Task FormPartyAsync(LegendTcpClient leader, LegendTcpClient mate, string mateName)
    {
        var invite = await leader.PartyInviteAsync(mateName);
        Assert.True(invite.Success, invite.Message);
        var invN = await mate.WaitPartyInviteAsync(5000);
        var accept = await mate.PartyAcceptAsync(invN.PartyId, true);
        Assert.True(accept.Success, accept.Message);
    }

    private static async Task<List<EntitySnapshot>> CollectDungeonMobsAsync(LegendTcpClient c, int need, int timeoutMs = 3000)
    {
        var mobs = new List<EntitySnapshot>();
        while (c.ReceivedMapChanges.TryDequeue(out var mc))
        {
            foreach (var e in mc.NearbyEntities)
                if (e.Kind == EntityKind.EntityMonster) mobs.Add(e);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs && mobs.Count < need)
        {
            while (c.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityMonster) mobs.Add(sp.Entity);
            await Task.Delay(40);
        }
        return mobs.GroupBy(m => m.EntityId).Select(g => g.First()).ToList();
    }

    [Fact]
    public async Task PartyEnter_SameInstance_SharedClear_PerMemberReward()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (leader, _, _) = await EnterAsAsync(engine, $"m13a_{suffix}", $"队{suffix}", "warrior");
            var (mate, _, _) = await EnterAsAsync(engine, $"m13b_{suffix}", $"员{suffix}", "mage");
            try
            {
                await FormPartyAsync(leader, mate, $"员{suffix}");
                await WalkAsync(leader, 10, 10);
                await WalkAsync(mate, 10, 10);

                int goldL = leader.Gold;
                int goldM = mate.Gold;
                var enter = await leader.DungeonEnterAsync("dungeon_slime_cave", partyEnter: true);
                Assert.True(enter.Success, enter.Message);
                Assert.StartsWith("inst_", enter.MapId);

                for (int i = 0; i < 50 && !mate.SelfEntity!.MapId.StartsWith("inst_"); i++)
                    await Task.Delay(40);
                Assert.Equal(enter.MapId, mate.SelfEntity!.MapId);

                var mobs = await CollectDungeonMobsAsync(leader, enter.TargetKills);
                Assert.True(mobs.Count >= enter.TargetKills, $"mobs={mobs.Count}");
                foreach (var mob in mobs.Take(enter.TargetKills))
                {
                    await WalkAsync(leader, mob.X, mob.Y);
                    await KillAsync(leader, mob.EntityId);
                }

                var cL = await leader.WaitDungeonCompleteAsync(15000);
                var cM = await mate.WaitDungeonCompleteAsync(15000);
                Assert.Equal(50, cL.RewardGold);
                Assert.Equal(50, cM.RewardGold);
                Assert.True(leader.Gold >= goldL - 10 + 50);
                Assert.True(mate.Gold >= goldM - 10 + 50);
                Assert.Equal("novice_plain", leader.SelfEntity!.MapId);
                Assert.Equal("novice_plain", mate.SelfEntity!.MapId);
            }
            finally
            {
                await leader.DisposeAsync();
                await mate.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task PartyEnter_BusyMemberDenied_LeaderStillEnters()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (leader, _, _) = await EnterAsAsync(engine, $"m13c_{suffix}", $"长{suffix}", "warrior");
            var (mate, _, _) = await EnterAsAsync(engine, $"m13d_{suffix}", $"忙{suffix}", "mage");
            try
            {
                await FormPartyAsync(leader, mate, $"忙{suffix}");
                await WalkAsync(leader, 10, 10);
                await WalkAsync(mate, 10, 10);

                var mateSolo = await mate.DungeonEnterAsync("dungeon_slime_cave");
                Assert.True(mateSolo.Success, mateSolo.Message);

                var enter = await leader.DungeonEnterAsync("dungeon_slime_cave", partyEnter: true);
                Assert.True(enter.Success, enter.Message);
                Assert.NotEqual(mateSolo.InstanceId, enter.InstanceId);
                Assert.Equal(enter.MapId, leader.SelfEntity!.MapId);
                Assert.Equal(mateSolo.MapId, mate.SelfEntity!.MapId);

                await leader.DungeonLeaveAsync();
                await mate.DungeonLeaveAsync();
            }
            finally
            {
                await leader.DisposeAsync();
                await mate.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task LevelGate_And_DailyLimit_Cooldown_PersistAcrossRestart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m13-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }
        string suffix = Guid.NewGuid().ToString("N")[..4];
        string user = $"m13q_{suffix}";
        string name = $"限{suffix}";

        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (client, _, _) = await EnterAsAsync(engine, user, name, "warrior");
                try
                {
                    var gate = await client.DungeonEnterAsync("dungeon_level_gate");
                    Assert.False(gate.Success);
                    Assert.Equal(ProtocolErrorCode.ErrDungeonLevel, gate.ErrorCode);

                    var st0 = await client.DungeonStatusAsync("dungeon_daily_quota");
                    Assert.True(st0.Success);
                    Assert.Equal(1, st0.DailyLimit);

                    var e1 = await client.DungeonEnterAsync("dungeon_daily_quota");
                    Assert.True(e1.Success, e1.Message);
                    await client.DungeonLeaveAsync();

                    var e2 = await client.DungeonEnterAsync("dungeon_daily_quota");
                    Assert.False(e2.Success);
                    Assert.True(
                        e2.ErrorCode is ProtocolErrorCode.ErrDungeonDailyLimit or ProtocolErrorCode.ErrDungeonCooldown,
                        e2.Message);

                    var st1 = await client.DungeonStatusAsync("dungeon_daily_quota");
                    Assert.True(st1.UsedToday >= 1);
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

        {
            var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var rec = await storage2.Characters.GetByNameAsync(name);
            Assert.NotNull(rec);
            var progress = DungeonProgressJson.Parse(rec!.DungeonProgressJson);
            Assert.True(progress.ContainsKey("dungeon_daily_quota"));
            Assert.True(progress["dungeon_daily_quota"].UsedToday >= 1);

            var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
            await engine2.StartAsync();
            try
            {
                var (client2, _, _) = await EnterAsAsync(engine2, user, name, "warrior");
                try
                {
                    var st = await client2.DungeonStatusAsync("dungeon_daily_quota");
                    Assert.True(st.Success);
                    Assert.True(st.UsedToday >= 1);
                    Assert.False(st.CanEnter);
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
    public void VersionPack_Loads_M13_DungeonDailyFields()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.14.0", pack.Manifest.ProtocolVersion);
        var slime = pack.Dungeons.Dungeons.First(d => d.DungeonId == "dungeon_slime_cave");
        Assert.Equal(5, slime.DailyLimit);
        var quota = pack.Dungeons.Dungeons.First(d => d.DungeonId == "dungeon_daily_quota");
        Assert.Equal(1, quota.DailyLimit);
        Assert.Equal(2, quota.CooldownSeconds);
        Assert.Equal(0, quota.ResetHourLocal);
    }
}
