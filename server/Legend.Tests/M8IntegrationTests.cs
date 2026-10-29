using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M8IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.18.0", $"pc-{user}", ct)).Accepted);
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
            Assert.True(m.Accepted);
        }
    }

    [Fact]
    public async Task Party_Invite_Accept_SharedExp_Leave_And_Friend_Whisper_Persist()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m8-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string friendCharId;
        string ownerUser = "m8_owner_" + Guid.NewGuid().ToString("N")[..6];
        string friendUser = "m8_friend_" + Guid.NewGuid().ToString("N")[..6];
        string ownerName = "甲" + Guid.NewGuid().ToString("N")[..4];
        string friendName = "乙" + Guid.NewGuid().ToString("N")[..4];

        // Phase 1: party + friend + whisper
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (a, aId, enterA) = await EnterAsAsync(engine, ownerUser, ownerName, "warrior");
                var (b, bId, _) = await EnterAsAsync(engine, friendUser, friendName, "mage");
                friendCharId = bId;

                // Cannot add self
                var self = await a.FriendAddAsync(ownerName);
                Assert.False(self.Success);
                Assert.Equal(ProtocolErrorCode.ErrFriendDenied, self.ErrorCode);

                // Unidirectional friend
                var add = await a.FriendAddAsync(friendName);
                Assert.True(add.Success, add.Message);
                Assert.Equal(friendName, add.Friend.Name);

                var fl = await a.FriendListAsync();
                Assert.True(fl.Success);
                Assert.Contains(fl.Friends, f => f.CharacterId == bId);

                // Whisper requires friend
                await a.WhisperAsync(friendName, "m8-hi");
                await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatWhisper && n.Content == "m8-hi");
                await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatWhisper && n.Content == "m8-hi" && n.SenderName == ownerName);

                // B is not friend of A => whisper denied
                while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
                await b.WhisperAsync(ownerName, "nope");
                await Task.Delay(200);
                Assert.DoesNotContain(a.ReceivedChatNotifies, n => n.Content == "nope");

                // Party: invite -> accept
                var inv = await a.PartyInviteAsync(friendName);
                Assert.True(inv.Success, inv.Message);
                var pin = await b.WaitPartyInviteAsync();
                Assert.Equal(inv.PartyId, pin.PartyId);
                var acc = await b.PartyAcceptAsync(pin.PartyId, true);
                Assert.True(acc.Success, acc.Message);
                var info = await a.WaitPartyInfoAsync(n => n.Members.Count == 2);
                Assert.Equal(2, info.Members.Count);
                Assert.Contains(info.Members, m => m.IsLeader && m.CharacterId == aId);

                // Shared exp: walk near slime spawn, both members same map, kill until both get ExpGain
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
                            await WalkAsync(c, 25 + (i % 3), 10 + (i % 2));
                            await Task.Delay(50);
                        }
                    }
                    Assert.NotNull(m);
                    return m!;
                }

                ExpGainNotify? expA = null, expB = null;
                for (int attempt = 0; attempt < 5 && (expA is null || expB is null); attempt++)
                {
                    var monster = await FindMonsterAsync(a, enterA);
                    await WalkAsync(a, Math.Max(0, monster.X - 1), monster.Y);
                    await WalkAsync(b, monster.X - 1, Math.Min(monster.Y + 1, 40));
                    for (int hit = 0; hit < 30 && (expA is null || expB is null); hit++)
                    {
                        await Task.Delay(650);
                        var (atk, cs) = await a.AttackAsync(monster.EntityId, "basic_slash");
                        if (!atk.Success)
                        {
                            await Task.Delay(400);
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
                Assert.NotNull(expA);
                Assert.NotNull(expB);
                Assert.True(expA!.GainedExp > 0 && expB!.GainedExp > 0);

                // Leave
                var leave = await b.PartyLeaveAsync();
                Assert.True(leave.Success);

                // Already in party denial: form again then second invite of same person fails after accept... 
                // Kick path: invite again, accept, kick
                var inv2 = await a.PartyInviteAsync(friendName);
                Assert.True(inv2.Success, inv2.Message);
                var pin2 = await b.WaitPartyInviteAsync();
                Assert.True((await b.PartyAcceptAsync(pin2.PartyId, true)).Success);
                await a.WaitPartyInfoAsync(n => n.Members.Count == 2);
                var kick = await a.PartyKickAsync(bId);
                Assert.True(kick.Success, kick.Message);

                await a.DisposeAsync();
                await b.DisposeAsync();
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        // Phase 2: friend list persists after restart
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (a, _, _) = await EnterAsAsync(engine, ownerUser, ownerName, "warrior");
                var fl = await a.FriendListAsync();
                Assert.True(fl.Success);
                Assert.Contains(fl.Friends, f => f.CharacterId == friendCharId && f.Name == friendName);

                var rm = await a.FriendRemoveAsync(friendCharId);
                Assert.True(rm.Success);
                var fl2 = await a.FriendListAsync();
                Assert.DoesNotContain(fl2.Friends, f => f.CharacterId == friendCharId);

                await a.DisposeAsync();
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public void VersionPack_Loads_PartyConfig_1_8_0()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.18.0", pack.Manifest.ProtocolVersion);
        Assert.Equal(5, pack.Party.MaxMembers);
        Assert.True(pack.Party.ExpShareSameMapOnly);
        Assert.Equal(10, pack.Party.ExpBonusPercent);
        Assert.Equal(50, pack.Party.MaxFriends);
        Assert.True(pack.Party.WhisperRequiresFriend);
        Assert.Contains("scripts/party.lua", pack.LuaScripts.Keys);
        Assert.Contains("scripts/social.lua", pack.LuaScripts.Keys);
    }
}
