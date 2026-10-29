using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M18IntegrationTests
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
    public void GuildRanks_VersionPack_LoadsConfig()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.20.0", pack.Manifest.ProtocolVersion);
        Assert.Equal("1.20.0", ProtocolConstants.CurrentVersion);
        Assert.True(pack.Guild.LeaderCanClear);
        Assert.True(pack.Guild.ViceLeaderCanClear);
        Assert.True(pack.Guild.WarehouseAdminCanClear);
        Assert.False(pack.Guild.MemberCanClear);
        Assert.True(pack.Guild.LeaderCanSetRank);
        Assert.Equal(2, pack.Guild.MaxViceLeaders);
        Assert.Equal(3, pack.Guild.MaxWarehouseAdmins);
        Assert.Equal(100, pack.Guild.AuditLogLimit);
    }

    [Fact]
    public async Task GuildRanks_SetRank_WarehouseAdmin_CanClear_Member_Denied()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m18-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            var (leader, leadId, _) = await EnterAsAsync(engine, "m18a_lead", "职长甲", "warrior");
            var (member, memId, _) = await EnterAsAsync(engine, "m18a_mem", "职员乙", "mage");
            var (whAdmin, whId, _) = await EnterAsAsync(engine, "m18a_wh", "职仓丙", "taoist");
            try
            {
                string gName = "职级行会";
                Assert.True((await leader.GuildCreateAsync(gName)).Success);
                Assert.True((await member.GuildJoinAsync(gName)).Success);
                Assert.True((await whAdmin.GuildJoinAsync(gName)).Success);

                var deny = await member.GuildSetRankAsync(whId, "warehouse_admin");
                Assert.False(deny.Success);
                Assert.Equal(ProtocolErrorCode.ErrGuildRankDenied, deny.ErrorCode);

                var set = await leader.GuildSetRankAsync(whId, "warehouse_admin");
                Assert.True(set.Success, set.Message);
                Assert.Equal("warehouse_admin", set.NewRank);
                Assert.Equal("member", set.OldRank);

                var invalid = await leader.GuildSetRankAsync(whId, "leader");
                Assert.False(invalid.Success);
                Assert.Equal(ProtocolErrorCode.ErrGuildInvalidRank, invalid.ErrorCode);

                var info = await leader.GuildInfoAsync();
                Assert.True(info.Success);
                Assert.Contains(info.Members, m => m.CharacterId == whId && m.Rank == "warehouse_admin");
                Assert.Contains(info.Members, m => m.CharacterId == leadId && m.Rank == "leader");

                var buy = await leader.ShopBuyAsync("npc_village_merchant", "item_hp_potion", 3);
                Assert.True(buy.Success, buy.Message);
                Assert.True((await leader.GuildWarehouseDepositAsync("item_hp_potion", 3)).Success);

                var listMem = await member.GuildWarehouseListAsync();
                Assert.True(listMem.Success);
                Assert.False(listMem.CanClear);

                var clearDeny = await member.GuildWarehouseClearAsync();
                Assert.False(clearDeny.Success);
                Assert.Equal(ProtocolErrorCode.ErrGuildWarehouseDenied, clearDeny.ErrorCode);

                var listWh = await whAdmin.GuildWarehouseListAsync();
                Assert.True(listWh.Success);
                Assert.True(listWh.CanClear);

                var clear = await whAdmin.GuildWarehouseClearAsync();
                Assert.True(clear.Success, clear.Message);
                Assert.True(clear.ClearedSlots >= 1);
            }
            finally
            {
                await leader.DisposeAsync();
                await member.DisposeAsync();
                await whAdmin.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
            await storage.DisposeAsync();
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task GuildWarehouse_Audit_Persists_Across_Restart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m18p-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string guildId;
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (leader, _, _) = await EnterAsAsync(engine, "m18p_lead", "审存甲", "warrior");
                try
                {
                    var create = await leader.GuildCreateAsync("审计持久会");
                    Assert.True(create.Success, create.Message);
                    guildId = create.GuildId;

                    var buy = await leader.ShopBuyAsync("npc_village_merchant", "item_hp_potion", 2);
                    Assert.True(buy.Success, buy.Message);
                    Assert.True((await leader.GuildWarehouseDepositAsync("item_hp_potion", 2)).Success);
                    Assert.True((await leader.GuildWarehouseWithdrawAsync("item_hp_potion", 1)).Success);
                    Assert.True((await leader.GuildWarehouseClearAsync()).Success);

                    var audit = await leader.GuildWarehouseListAuditAsync(10);
                    Assert.True(audit.Success, audit.Message);
                    Assert.True(audit.Entries.Count >= 3);
                    Assert.Contains(audit.Entries, e => e.Action == "deposit");
                    Assert.Contains(audit.Entries, e => e.Action == "withdraw");
                    Assert.Contains(audit.Entries, e => e.Action == "clear");
                }
                finally { await leader.DisposeAsync(); }
            }
            finally
            {
                await engine.DisposeAsync();
                await storage.DisposeAsync();
            }
        }

        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var rows = await storage.Guilds.ListWarehouseAuditAsync(guildId, 20);
                Assert.True(rows.Count >= 3);
                Assert.Contains(rows, r => r.Action == "deposit");
                Assert.Contains(rows, r => r.Action == "clear");

                var (leader, _, _) = await EnterAsAsync(engine, "m18p_lead", "审存甲", "warrior");
                try
                {
                    var audit = await leader.GuildWarehouseListAuditAsync(20);
                    Assert.True(audit.Success, audit.Message);
                    Assert.True(audit.Entries.Count >= 3);
                    Assert.All(audit.Entries, e => Assert.True(e.CreatedUnixMs > 0));
                }
                finally { await leader.DisposeAsync(); }
            }
            finally
            {
                await engine.DisposeAsync();
                await storage.DisposeAsync();
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
