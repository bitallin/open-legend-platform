using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M17IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.19.0", $"pc-{user}", ct)).Accepted);
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
    public void GuildWarehouse_VersionPack_LoadsConfig()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.19.0", pack.Manifest.ProtocolVersion);
        Assert.Equal("1.19.0", ProtocolConstants.CurrentVersion);
        Assert.Equal(40, pack.Guild.WarehouseCapacitySlots);
        Assert.Equal(999, pack.Guild.WarehouseMaxStackPerItem);
        Assert.True(pack.Guild.MemberCanDeposit);
        Assert.True(pack.Guild.MemberCanWithdraw);
        Assert.True(pack.Guild.LeaderCanClear);
    }

    [Fact]
    public async Task GuildWarehouse_Deposit_Withdraw_Clear_Permissions_And_Reject_Outsider()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m17-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            var (outsider, _, _) = await EnterAsAsync(engine, "m17a_out", "仓外丁", "taoist");
            var (leader, _, _) = await EnterAsAsync(engine, "m17a_lead", "仓长甲", "warrior");
            var (member, _, _) = await EnterAsAsync(engine, "m17a_mem", "仓员乙", "mage");
            try
            {
                var deny = await outsider.GuildWarehouseListAsync();
                Assert.False(deny.Success);
                Assert.Equal(ProtocolErrorCode.ErrNotInGuild, deny.ErrorCode);

                string gName = "测试共享仓";
                var create = await leader.GuildCreateAsync(gName);
                Assert.True(create.Success, create.Message);
                var join = await member.GuildJoinAsync(gName);
                Assert.True(join.Success, join.Message);

                var buy = await leader.ShopBuyAsync("npc_village_merchant", "item_hp_potion", 5);
                Assert.True(buy.Success, buy.Message);

                var depDeny = await outsider.GuildWarehouseDepositAsync("item_hp_potion", 1);
                Assert.False(depDeny.Success);
                Assert.Equal(ProtocolErrorCode.ErrNotInGuild, depDeny.ErrorCode);

                var dep = await leader.GuildWarehouseDepositAsync("item_hp_potion", 3);
                Assert.True(dep.Success, dep.Message);
                Assert.Equal(1, dep.UsedSlots);
                Assert.Contains(dep.WarehouseItems, i => i.ItemId == "item_hp_potion" && i.Count == 3);

                var list = await member.GuildWarehouseListAsync();
                Assert.True(list.Success, list.Message);
                Assert.True(list.CanDeposit);
                Assert.True(list.CanWithdraw);
                Assert.False(list.CanClear);
                Assert.Equal(1, list.UsedSlots);

                var wd = await member.GuildWarehouseWithdrawAsync("item_hp_potion", 2);
                Assert.True(wd.Success, wd.Message);
                Assert.Contains(wd.WarehouseItems, i => i.ItemId == "item_hp_potion" && i.Count == 1);

                var clearDeny = await member.GuildWarehouseClearAsync();
                Assert.False(clearDeny.Success);
                Assert.Equal(ProtocolErrorCode.ErrGuildWarehouseDenied, clearDeny.ErrorCode);

                var clear = await leader.GuildWarehouseClearAsync();
                Assert.True(clear.Success, clear.Message);
                Assert.True(clear.ClearedSlots >= 1);

                var empty = await leader.GuildWarehouseListAsync();
                Assert.True(empty.Success);
                Assert.Equal(0, empty.UsedSlots);
            }
            finally
            {
                await outsider.DisposeAsync();
                await leader.DisposeAsync();
                await member.DisposeAsync();
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
    public async Task GuildWarehouse_Persists_Across_Restart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m17p-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string guildId;
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (leader, _, _) = await EnterAsAsync(engine, "m17p_lead", "仓存甲", "warrior");
                try
                {
                    var create = await leader.GuildCreateAsync("持久仓行会");
                    Assert.True(create.Success, create.Message);
                    guildId = create.GuildId;

                    var buy = await leader.ShopBuyAsync("npc_village_merchant", "item_hp_potion", 4);
                    Assert.True(buy.Success, buy.Message);
                    var dep = await leader.GuildWarehouseDepositAsync("item_hp_potion", 4);
                    Assert.True(dep.Success, dep.Message);
                    Assert.Equal(1, dep.UsedSlots);
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
                var guild = await storage.Guilds.GetByIdAsync(guildId);
                Assert.NotNull(guild);
                Assert.Contains("item_hp_potion", guild!.WarehouseJson);

                var (leader, _, _) = await EnterAsAsync(engine, "m17p_lead", "仓存甲", "warrior");
                try
                {
                    var list = await leader.GuildWarehouseListAsync();
                    Assert.True(list.Success, list.Message);
                    Assert.Equal(1, list.UsedSlots);
                    Assert.Contains(list.Items, i => i.ItemId == "item_hp_potion" && i.Count == 4);

                    var wd = await leader.GuildWarehouseWithdrawAsync("item_hp_potion", 4);
                    Assert.True(wd.Success, wd.Message);
                    Assert.Equal(0, wd.UsedSlots);
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
