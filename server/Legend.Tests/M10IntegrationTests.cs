using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M10IntegrationTests
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

    [Fact]
    public async Task Siege_Declare_PvP_Capture_LordBuff_And_Persist_Across_Restart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m10-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string suffix = Guid.NewGuid().ToString("N")[..4];
        string userLA = $"m10_la_{suffix}";
        string userMA = $"m10_ma_{suffix}";
        string userLB = $"m10_lb_{suffix}";
        string charLA = $"城主{suffix}";
        string charMA = $"护卫{suffix}";
        string charLB = $"敌首{suffix}";
        string guildNameA = $"沙巴克之巅{suffix}";
        string guildNameB = $"挑战者联盟{suffix}";
        string guildIdA;
        string guildIdB;
        int baseAtkBeforeLord;

        // Phase 1: Declare, start siege, cross-guild PvP & same-guild immunity, contested & uncontested capture, end siege
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (leaderA, _, enterLA) = await EnterAsAsync(engine, userLA, charLA, "warrior");
                var (memberA, _, _) = await EnterAsAsync(engine, userMA, charMA, "warrior");
                var (leaderB, _, _) = await EnterAsAsync(engine, userLB, charLB, "mage");
                await using var _a = leaderA;
                await using var _ma = memberA;
                await using var _b = leaderB;

                baseAtkBeforeLord = enterLA.EffectiveAtk;

                // 1. Guildless player cannot declare siege
                var denyNoGuild = await leaderA.SiegeDeclareAsync();
                Assert.False(denyNoGuild.Success);
                Assert.Equal(ProtocolErrorCode.ErrSiegeDenied, denyNoGuild.ErrorCode);

                // 2. Create Guild A and Guild B; memberA joins Guild A
                var createGA = await leaderA.GuildCreateAsync(guildNameA);
                Assert.True(createGA.Success, createGA.Message);
                guildIdA = createGA.GuildId;

                var joinGA = await memberA.GuildJoinAsync(guildNameA);
                Assert.True(joinGA.Success, joinGA.Message);

                var createGB = await leaderB.GuildCreateAsync(guildNameB);
                Assert.True(createGB.Success, createGB.Message);
                guildIdB = createGB.GuildId;

                // 3. Non-leader member cannot declare siege
                var denyMember = await memberA.SiegeDeclareAsync();
                Assert.False(denyMember.Success);
                Assert.Equal(ProtocolErrorCode.ErrSiegeDenied, denyMember.ErrorCode);

                // 4. Both guild leaders declare siege (costs 50 gold each: 50 -> 0)
                var declA = await leaderA.SiegeDeclareAsync();
                Assert.True(declA.Success, declA.Message);
                Assert.Equal(0, declA.NewGold);
                Assert.Contains(guildIdA, declA.DeclaredGuildIds);

                // Duplicate declare rejected
                var declADup = await leaderA.SiegeDeclareAsync();
                Assert.False(declADup.Success);
                Assert.Equal(ProtocolErrorCode.ErrSiegeAlreadyDeclared, declADup.ErrorCode);

                var declB = await leaderB.SiegeDeclareAsync();
                Assert.True(declB.Success, declB.Message);
                Assert.Equal(0, declB.NewGold);
                Assert.Contains(guildIdA, declB.DeclaredGuildIds);
                Assert.Contains(guildIdB, declB.DeclaredGuildIds);

                // 5. Enter Sabuk Palace captureZone via portal at (10, 8) -> dark_forest (20, 20)
                await leaderA.MoveAsync(10, 8);
                await memberA.MoveAsync(10, 8);
                await leaderB.MoveAsync(10, 8);
                for (int w = 0; w < 60 && (leaderA.SelfEntity?.MapId != "dark_forest" || memberA.SelfEntity?.MapId != "dark_forest" || leaderB.SelfEntity?.MapId != "dark_forest"); w++)
                    await Task.Delay(25);
                Assert.Equal("dark_forest", leaderA.SelfEntity!.MapId);
                Assert.Equal("dark_forest", memberA.SelfEntity!.MapId);
                Assert.Equal("dark_forest", leaderB.SelfEntity!.MapId);

                // 6. Capture before siege active is rejected with ErrSiegeNotActive
                var capBeforeStart = await leaderA.SiegeCaptureAsync();
                Assert.False(capBeforeStart.Success);
                Assert.Equal(ProtocolErrorCode.ErrSiegeNotActive, capBeforeStart.ErrorCode);

                // PvP before siege active is rejected (M2 default non-PvP compatibility)
                var (pvpBeforeStart, _) = await leaderA.AttackAsync(leaderB.SelfEntity.EntityId, "basic_slash");
                Assert.False(pvpBeforeStart.Success);
                Assert.Equal(ProtocolErrorCode.ErrInvalidTarget, pvpBeforeStart.ErrorCode);

                // 7. Start Siege War
                var startResp = await leaderA.SiegeControlAsync("start");
                Assert.True(startResp.Success, startResp.Message);
                Assert.True(startResp.IsActive);

                // 8. Same-guild PvP is immune (ErrSiegeDenied)
                var (sameGuildAtk, _) = await leaderA.AttackAsync(memberA.SelfEntity.EntityId, "basic_slash");
                Assert.False(sameGuildAtk.Success);
                Assert.Equal(ProtocolErrorCode.ErrSiegeDenied, sameGuildAtk.ErrorCode);

                // 9. Contested capture rejected while enemy leaderB is alive in captureZone
                var contestedCap = await leaderA.SiegeCaptureAsync();
                Assert.False(contestedCap.Success);
                Assert.Equal(ProtocolErrorCode.ErrSiegeContested, contestedCap.ErrorCode);

                // 10. Cross-guild PvP: leaderA + memberA kill leaderB, sending leaderB back to spawn (5, 5)
                bool bKilled = false;
                for (int i = 0; i < 20 && !bKilled; i++)
                {
                    var (atk1, cs1) = await leaderA.AttackAsync(leaderB.SelfEntity.EntityId, "power_strike");
                    if (atk1.Success && cs1?.TargetDied == true) { bKilled = true; break; }
                    var (atk2, cs2) = await memberA.AttackAsync(leaderB.SelfEntity.EntityId, "basic_slash");
                    if (atk2.Success && cs2?.TargetDied == true) { bKilled = true; break; }
                    await Task.Delay(550);
                }
                Assert.True(bKilled, "Expected enemy leaderB to be killed in cross-guild PvP");
                await Task.Delay(80);
                Assert.Equal(5, leaderB.SelfEntity.X);
                Assert.Equal(5, leaderB.SelfEntity.Y);

                // 11. Uncontested capture now succeeds and grants buff_castle_lord (+20% ATK/DEF)
                while (leaderA.ReceivedBuffApplyNotifies.TryDequeue(out _)) { }
                var cap = await leaderA.SiegeCaptureAsync();
                Assert.True(cap.Success, cap.Message);
                Assert.Equal(guildIdA, cap.OwnerGuildId);
                Assert.Equal(guildNameA, cap.OwnerGuildName);

                var lordBuff = await leaderA.WaitBuffApplyAsync("buff_castle_lord", 3000);
                Assert.True(lordBuff.EffectiveAtk > baseAtkBeforeLord,
                    $"Expected castle lord ATK bonus: {lordBuff.EffectiveAtk} > {baseAtkBeforeLord}");

                // 12. Stop Siege War -> winner guild members receive winnerRewardGold (100)
                var stopResp = await leaderA.SiegeControlAsync("stop");
                Assert.True(stopResp.Success, stopResp.Message);
                Assert.False(stopResp.IsActive);
                Assert.Equal(guildIdA, stopResp.OwnerGuildId);
                await Task.Delay(80);
                Assert.Equal(100, leaderA.Gold);
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        // Phase 2: Restart Host on same SQLite DB and verify Sabuk ownership & declared guilds persist
        {
            var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
            await engine2.StartAsync();
            try
            {
                var (leaderA2, _, enterLA2) = await EnterAsAsync(engine2, userLA, charLA, "warrior");
                await using var _a2 = leaderA2;

                Assert.Equal(guildIdA, enterLA2.SiegeOwnerGuildId);
                Assert.Equal(guildNameA, enterLA2.SiegeOwnerGuildName);
                Assert.True(enterLA2.IsCastleLordGuild);
                Assert.Contains(enterLA2.Buffs, b => b.BuffId == "buff_castle_lord");
                Assert.True(enterLA2.EffectiveAtk > baseAtkBeforeLord);

                var status = await leaderA2.SiegeStatusAsync();
                Assert.True(status.Success);
                Assert.False(status.IsActive);
                Assert.Equal(guildIdA, status.OwnerGuildId);
                Assert.Equal(guildNameA, status.OwnerGuildName);
                Assert.Contains(guildIdA, status.DeclaredGuildIds);
                Assert.Contains(guildIdB, status.DeclaredGuildIds);
            }
            finally
            {
                await engine2.DisposeAsync();
            }
        }

        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public void VersionPack_Loads_Siege_Config_And_CastleLord_Buff()
    {
        var pack = new VersionPackLoader().LoadFromManifest(GetManifestPath());
        Assert.Equal("1.20.0", pack.Manifest.ProtocolVersion);
        Assert.Equal("sabuk_castle", pack.Siege.CastleId);
        Assert.Equal(50, pack.Siege.DeclareCostGold);
        Assert.True(pack.Siege.LeaderOnlyDeclare);
        Assert.Equal("dark_forest", pack.Siege.CaptureZone.MapId);
        Assert.True(pack.Siege.CaptureZone.Contains("dark_forest", 20, 20));
        Assert.False(pack.Siege.CaptureZone.Contains("dark_forest", 5, 5));
        Assert.True(pack.Buffs.ContainsKey("buff_castle_lord"));
    }
}
