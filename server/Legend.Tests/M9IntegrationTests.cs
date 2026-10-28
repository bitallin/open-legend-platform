using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M9IntegrationTests
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
        int guard = 0;
        while ((c.SelfEntity!.X != tx || c.SelfEntity.Y != ty) && guard++ < 80)
        {
            int nx = c.SelfEntity.X + Math.Sign(tx - c.SelfEntity.X);
            int ny = c.SelfEntity.Y + Math.Sign(ty - c.SelfEntity.Y);
            var m = await c.MoveAsync(nx, ny, ct: ct);
            Assert.True(m.Accepted, m.Reason);
        }
    }

    private static async Task<EntitySnapshot> FindMonsterAsync(LegendTcpClient c, EnterWorldResp enter, CancellationToken ct = default)
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
                await WalkAsync(c, 25 + (i % 3), 10 + (i % 2), ct);
                await Task.Delay(40, ct);
            }
        }
        Assert.NotNull(m);
        return m!;
    }

    [Fact]
    public async Task SkillBook_Learn_LevelUp_Buff_And_Persist_Across_Restart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m9-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string user = "m9_u_" + Guid.NewGuid().ToString("N")[..6];
        string charName = "技" + Guid.NewGuid().ToString("N")[..4];
        string charId;
        int learnedLevel;

        // Phase 1: learn, level, buff
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (client, id, enter) = await EnterAsAsync(engine, user, charName, "warrior");
                charId = id;
                Assert.DoesNotContain(enter.Skills, s => s.SkillId == "fatal_strike");
                Assert.Contains(enter.Skills, s => s.SkillId == "power_strike");
                int baseAtk = enter.EffectiveAtk;

                await WalkAsync(client, 12, 12);
                var buyBook = await client.ShopBuyAsync("npc_village_merchant", "item_skillbook_fatal_strike", 1);
                Assert.True(buyBook.Success, buyBook.Message);
                var buyScroll = await client.ShopBuyAsync("npc_village_merchant", "item_war_cry_scroll", 1);
                Assert.True(buyScroll.Success, buyScroll.Message);

                var mon0 = await FindMonsterAsync(client, enter);
                await WalkAsync(client, Math.Max(0, mon0.X - 1), mon0.Y);
                var (deny, _) = await client.AttackAsync(mon0.EntityId, "fatal_strike");
                Assert.False(deny.Success);
                Assert.Equal(ProtocolErrorCode.ErrSkillNotLearned, deny.ErrorCode);

                var learn = await client.LearnSkillAsync("item_skillbook_fatal_strike");
                Assert.True(learn.Success, learn.Message);
                Assert.Equal("fatal_strike", learn.Skill.SkillId);
                Assert.Equal(1, learn.Skill.Level);

                // Already learned: LearnSkill again should fail without walking back for another book
                var learn2 = await client.LearnSkillAsync("item_skillbook_fatal_strike");
                Assert.False(learn2.Success);

                int dmgLv1 = 0;
                for (int i = 0; i < 10 && dmgLv1 == 0; i++)
                {
                    var mon = await FindMonsterAsync(client, enter);
                    await WalkAsync(client, Math.Max(0, mon.X - 1), mon.Y);
                    await Task.Delay(900);
                    var (atk, cs) = await client.AttackAsync(mon.EntityId, "fatal_strike");
                    if (atk.Success && cs != null && cs.Damage > 0)
                        dmgLv1 = cs.Damage;
                    else
                        await Task.Delay(400);
                }
                Assert.True(dmgLv1 > 0, "lv1 damage");

                int level = 1;
                for (int i = 0; i < 50 && level < 2; i++)
                {
                    var mon = await FindMonsterAsync(client, enter);
                    await WalkAsync(client, Math.Max(0, mon.X - 1), mon.Y);
                    await Task.Delay(900);
                    var (atk, _) = await client.AttackAsync(mon.EntityId, "fatal_strike");
                    if (!atk.Success) { await Task.Delay(400); continue; }
                    while (client.ReceivedSkillLevelUpNotifies.TryDequeue(out var up))
                        if (up.SkillId == "fatal_strike")
                            level = Math.Max(level, up.NewLevel);
                    var sl = await client.SkillListAsync();
                    var st = sl.Skills.FirstOrDefault(s => s.SkillId == "fatal_strike");
                    if (st != null) level = Math.Max(level, st.Level);
                }
                Assert.True(level >= 2, $"expected skill level >=2, got {level}");
                learnedLevel = level;

                int dmgLv2 = 0;
                for (int i = 0; i < 10 && dmgLv2 == 0; i++)
                {
                    var mon = await FindMonsterAsync(client, enter);
                    await WalkAsync(client, Math.Max(0, mon.X - 1), mon.Y);
                    await Task.Delay(900);
                    var (atk, cs) = await client.AttackAsync(mon.EntityId, "fatal_strike");
                    if (atk.Success && cs != null && cs.Damage > 0)
                        dmgLv2 = cs.Damage;
                    else
                        await Task.Delay(400);
                }
                Assert.True(dmgLv2 > dmgLv1, $"lv2 dmg {dmgLv2} should exceed lv1 {dmgLv1}");

                while (client.ReceivedBuffApplyNotifies.TryDequeue(out _)) { }
                var useScroll = await client.UseItemAsync("item_war_cry_scroll");
                Assert.True(useScroll.Success, useScroll.Message);
                var buff = await client.WaitBuffApplyAsync("buff_war_cry");
                Assert.Equal("buff_war_cry", buff.Buff.BuffId);
                Assert.True(buff.EffectiveAtk > baseAtk, $"buff atk {buff.EffectiveAtk} vs base {baseAtk}");

                await client.DisposeAsync();
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        // Phase 2: restart — skills persist
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var client = new LegendTcpClient();
                await client.ConnectAsync("127.0.0.1", engine.BoundPort);
                Assert.True((await client.HandshakeAsync("1.12.0", "pc-m9-re")).Accepted);
                var login = await client.LoginAsync(user, "secret12345", "PC");
                Assert.True(login.Success, login.Message);
                await client.SelectCharAsync(charId);
                var realms = await client.GetRealmListAsync();
                var enter = await client.EnterWorldAsync(realms.Realms[0].RealmId, charId);
                Assert.True(enter.Success, enter.Message);
                var fatal = enter.Skills.FirstOrDefault(s => s.SkillId == "fatal_strike");
                Assert.NotNull(fatal);
                Assert.True(fatal!.Level >= learnedLevel, $"persisted lv {fatal.Level} < {learnedLevel}");

                var list = await client.SkillListAsync();
                Assert.Contains(list.Skills, s => s.SkillId == "fatal_strike" && s.Level >= learnedLevel);
                await client.DisposeAsync();
            }
            finally
            {
                await engine.DisposeAsync();
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public void VersionPack_Loads_Skills_Buffs_And_Skillbook()
    {
        var pack = new VersionPackLoader().LoadFromManifest(GetManifestPath());
        Assert.Equal("1.12.0", pack.Manifest.ProtocolVersion);
        Assert.True(pack.Skills.ContainsKey("fatal_strike"));
        Assert.True(pack.Skills["fatal_strike"].RequiresLearn);
        Assert.True(pack.Buffs.ContainsKey("buff_war_cry"));
        Assert.True(pack.Buffs.ContainsKey("debuff_poison"));
        Assert.True(pack.Items.ContainsKey("item_skillbook_fatal_strike"));
        Assert.Equal("fatal_strike", pack.Items["item_skillbook_fatal_strike"].TeachesSkillId);
        Assert.Equal("buff_war_cry", pack.Items["item_war_cry_scroll"].AppliesBuffId);
    }
}
