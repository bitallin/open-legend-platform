using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M6IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.10.0", $"pc-{user}", ct)).Accepted);
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
    public async Task Guild_Create_Join_Chat_Channels_And_Persist_Across_Restart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m6-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string guildName = "测试行会";
        string guildId;
        int goldBefore;

        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (a, _, enterA) = await EnterAsAsync(engine, "m6_leader", "会长甲", "warrior");
                var (b, _, _) = await EnterAsAsync(engine, "m6_member", "成员乙", "mage");
                try
                {
                    goldBefore = enterA.Gold;
                    var create = await a.GuildCreateAsync(guildName);
                    Assert.True(create.Success, create.Message);
                    Assert.Equal(ProtocolErrorCode.ErrNone, create.ErrorCode);
                    Assert.Equal("leader", create.GuildRank);
                    Assert.Equal(goldBefore - 50, create.Gold);
                    Assert.False(string.IsNullOrEmpty(create.GuildId));
                    guildId = create.GuildId;
                    Assert.Equal(guildName, a.GuildName);

                    var dup = await a.GuildCreateAsync("另一行会");
                    Assert.False(dup.Success);
                    Assert.Equal(ProtocolErrorCode.ErrAlreadyInGuild, dup.ErrorCode);

                    var join = await b.GuildJoinAsync(guildName);
                    Assert.True(join.Success, join.Message);
                    Assert.Equal(2, join.MemberCount);
                    Assert.Equal("member", join.GuildRank);
                    Assert.Equal(guildId, b.GuildId);

                    var nameTaken = await EnterAsAsync(engine, "m6_third", "路人丙", "taoist");
                    try
                    {
                        var fail = await nameTaken.Client.GuildCreateAsync(guildName);
                        Assert.False(fail.Success);
                        Assert.Equal(ProtocolErrorCode.ErrGuildNameTaken, fail.ErrorCode);
                    }
                    finally { await nameTaken.Client.DisposeAsync(); }

                    var info = await a.GuildInfoAsync();
                    Assert.True(info.Success, info.Message);
                    Assert.Equal(2, info.Members.Count);
                    Assert.Contains(info.Members, m => m.Rank == "leader");

                    // World
                    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
                    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
                    await a.ChatAsync(ChatChannel.ChatWorld, "world-hi");
                    await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatWorld && n.Content == "world-hi");
                    await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatWorld && n.Content == "world-hi");
                    await Task.Delay(30);

                    // Nearby (same map, close spawn)
                    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
                    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
                    await a.ChatAsync(ChatChannel.ChatNearby, "near-hi");
                    await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatNearby && n.Content == "near-hi");
                    await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatNearby && n.Content == "near-hi");
                    await Task.Delay(30);

                    // Guild
                    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
                    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
                    await a.ChatAsync(ChatChannel.ChatGuild, "guild-hi");
                    await a.WaitChatAsync(n => n.Channel == ChatChannel.ChatGuild && n.Content == "guild-hi");
                    await b.WaitChatAsync(n => n.Channel == ChatChannel.ChatGuild && n.Content == "guild-hi");

                    // Leave denies guild chat
                    var leave = await b.GuildLeaveAsync();
                    Assert.True(leave.Success, leave.Message);
                    while (b.ReceivedServerNotices.TryDequeue(out _)) { }
                    await b.ChatAsync(ChatChannel.ChatGuild, "nope");
                    bool denied = false;
                    for (int i = 0; i < 40 && !denied; i++)
                    {
                        while (b.ReceivedServerNotices.TryDequeue(out var sn))
                        {
                            if (sn.NoticeType == "chat_denied") { denied = true; break; }
                        }
                        if (!denied) await Task.Delay(50);
                    }
                    Assert.True(denied, "expected chat_denied");

                    // Rejoin so persistence has 2 members
                    var rejoin = await b.GuildJoinAsync(guildName);
                    Assert.True(rejoin.Success, rejoin.Message);
                }
                finally
                {
                    await a.DisposeAsync();
                    await b.DisposeAsync();
                }
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        // Restart Host with same Sqlite — guild membership must survive
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (a, _, enterA) = await EnterAsAsync(engine, "m6_leader", "会长甲", "warrior");
                var (b, _, enterB) = await EnterAsAsync(engine, "m6_member", "成员乙", "mage");
                try
                {
                    Assert.Equal(guildId, enterA.GuildId);
                    Assert.Equal(guildName, enterA.GuildName);
                    Assert.Equal("leader", enterA.GuildRank);
                    Assert.Equal(guildId, enterB.GuildId);
                    Assert.Equal(guildName, enterB.GuildName);
                    Assert.Equal(goldBefore - 50, a.Gold);

                    var info = await a.GuildInfoAsync();
                    Assert.True(info.Success);
                    Assert.Equal(2, info.Members.Count);

                    while (a.ReceivedChatNotifies.TryDequeue(out _)) { }
                    while (b.ReceivedChatNotifies.TryDequeue(out _)) { }
                    await a.ChatAsync(ChatChannel.ChatGuild, "after-restart");
                    await a.WaitChatAsync(n => n.Content == "after-restart" && n.Channel == ChatChannel.ChatGuild);
                    await b.WaitChatAsync(n => n.Content == "after-restart" && n.Channel == ChatChannel.ChatGuild);
                }
                finally
                {
                    await a.DisposeAsync();
                    await b.DisposeAsync();
                }
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public async Task Guild_Store_InMemory_Create_Join_Leave_Roundtrip()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var (ok, err, g) = await storage.Guilds.CreateAsync("内存行会", "char-leader");
        Assert.True(ok, err);
        Assert.NotNull(g);
        Assert.Single(g!.MemberCharacterIds);

        var (ok2, err2, g2) = await storage.Guilds.AddMemberAsync(g.GuildId, "char-member");
        Assert.True(ok2, err2);
        Assert.Equal(2, g2!.MemberCharacterIds.Count);

        var byName = await storage.Guilds.GetByNameAsync("内存行会");
        Assert.NotNull(byName);
        Assert.Equal(g.GuildId, byName!.GuildId);

        var (ok3, err3) = await storage.Guilds.RemoveMemberAsync(g.GuildId, "char-member");
        Assert.True(ok3, err3);
        var after = await storage.Guilds.GetByIdAsync(g.GuildId);
        Assert.Single(after!.MemberCharacterIds);

        // empty guild dissolves on last leave
        var (ok4, err4) = await storage.Guilds.RemoveMemberAsync(g.GuildId, "char-leader");
        Assert.True(ok4, err4);
        Assert.Null(await storage.Guilds.GetByIdAsync(g.GuildId));
    }
}
