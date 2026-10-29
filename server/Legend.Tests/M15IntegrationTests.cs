using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M15IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.17.0", $"pc-{user}", ct)).Accepted);
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
    public void MountFashion_VersionPack_LoadsConfigs()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.17.0", pack.Manifest.ProtocolVersion);
        Assert.NotEmpty(pack.Mounts.Mounts);
        Assert.Contains(pack.Mounts.Mounts, m => m.MountId == "mount_white_horse");
        Assert.True(pack.Mounts.DefaultMoveIntervalMs >= 100);
        Assert.NotEmpty(pack.Fashions.Fashions);
        Assert.Contains(pack.Fashions.Fashions, f => f.FashionId == "fashion_crimson_robe" && f.AtkBonus >= 1);
        Assert.True(pack.Items.ContainsKey("item_mount_white_horse"));
        Assert.True(pack.Items.ContainsKey("item_fashion_crimson_robe"));
        Assert.Equal("mount_unlock", pack.Items["item_mount_white_horse"].Kind);
        Assert.Equal("fashion_unlock", pack.Items["item_fashion_crimson_robe"].Kind);
    }

    [Fact]
    public async Task Mount_Unlock_Summon_SpeedsMove_AndAoiSync()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (rider, _, enterR) = await EnterAsAsync(engine, $"m15r_{suffix}", $"Rdr{suffix}", "warrior");
            var (watcher, _, _) = await EnterAsAsync(engine, $"m15w_{suffix}", $"Wch{suffix}", "mage");
            try
            {
                int baseInterval = 300;
                var stEmpty = await rider.MountStatusAsync();
                Assert.True(stEmpty.Success);
                baseInterval = stEmpty.BaseMoveIntervalMs > 0 ? stEmpty.BaseMoveIntervalMs : 300;

                var buy = await rider.ShopBuyAsync("npc_village_merchant", "item_mount_white_horse", 1);
                Assert.True(buy.Success, buy.Message);
                var use = await rider.UseItemAsync("item_mount_white_horse", 1);
                Assert.True(use.Success, use.Message);

                var st = await rider.MountStatusAsync();
                Assert.Contains(st.Mounts, m => m.MountId == "mount_white_horse" && m.Owned);

                while (watcher.ReceivedAppearanceSyncs.TryDequeue(out _)) { }
                var summon = await rider.MountSummonAsync("mount_white_horse");
                Assert.True(summon.Success, summon.Message);
                Assert.Equal("mount_white_horse", summon.ActiveMountId);
                Assert.True(summon.MoveIntervalMs < baseInterval, $"interval {summon.MoveIntervalMs} vs {baseInterval}");

                var sync = await watcher.WaitAppearanceSyncAsync(
                    n => n.EntityId == enterR.SelfEntity!.EntityId && n.MountId == "mount_white_horse", 4000);
                Assert.Equal("mount_white_horse", sync.MountId);

                Assert.NotNull(rider.SelfEntity);
                var move = await rider.MoveAsync(rider.SelfEntity!.X + 1, rider.SelfEntity.Y);
                Assert.True(move.Accepted, move.Reason);

                var deny = await rider.MountSummonAsync("no_such_mount");
                Assert.False(deny.Success);
                Assert.Equal(ProtocolErrorCode.ErrMountDenied, deny.ErrorCode);

                var dismiss = await rider.MountDismissAsync();
                Assert.True(dismiss.Success);
                Assert.Equal(baseInterval, dismiss.MoveIntervalMs);
            }
            finally
            {
                await rider.DisposeAsync();
                await watcher.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Fashion_Unlock_Equip_AtkBonus_AndAoiSync()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (client, _, enter) = await EnterAsAsync(engine, $"m15f_{suffix}", $"Fash{suffix}", "mage");
            var (watcher, _, _) = await EnterAsAsync(engine, $"m15fw_{suffix}", $"Fw{suffix}", "taoist");
            try
            {
                int atk0 = enter.EffectiveAtk;
                var buy = await client.ShopBuyAsync("npc_village_merchant", "item_fashion_crimson_robe", 1);
                Assert.True(buy.Success, buy.Message);
                Assert.True((await client.UseItemAsync("item_fashion_crimson_robe", 1)).Success);

                while (watcher.ReceivedAppearanceSyncs.TryDequeue(out _)) { }
                var eq = await client.FashionEquipAsync("fashion_crimson_robe");
                Assert.True(eq.Success, eq.Message);
                Assert.Equal("fashion_crimson_robe", eq.EquippedFashionId);
                Assert.True(eq.EffectiveAtk >= atk0 + 2, $"atk {eq.EffectiveAtk} vs {atk0}");

                var sync = await watcher.WaitAppearanceSyncAsync(
                    n => n.EntityId == enter.SelfEntity!.EntityId && n.FashionId == "fashion_crimson_robe", 4000);
                Assert.Equal("fashion_crimson_robe", sync.FashionId);

                var st = await client.FashionStatusAsync();
                Assert.True(st.Success);
                Assert.Contains(st.Fashions, f => f.FashionId == "fashion_crimson_robe" && f.Owned && f.Equipped);

                var unequip = await client.FashionEquipAsync("");
                Assert.True(unequip.Success);
                Assert.Equal("", unequip.EquippedFashionId);
                Assert.True(unequip.EffectiveAtk <= eq.EffectiveAtk - 2 || unequip.EffectiveAtk == atk0);
            }
            finally
            {
                await client.DisposeAsync();
                await watcher.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Appearance_Persists_Across_ReEnter()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            string user = $"m15p_{suffix}";
            string charName = $"Pers{suffix}";
            var (client, charId, enter) = await EnterAsAsync(engine, user, charName, "warrior");
            int atk0 = enter.EffectiveAtk;
            try
            {
                Assert.True((await client.ShopBuyAsync("npc_village_merchant", "item_mount_white_horse", 1)).Success);
                Assert.True((await client.UseItemAsync("item_mount_white_horse", 1)).Success);
                Assert.True((await client.ShopBuyAsync("npc_village_merchant", "item_fashion_crimson_robe", 1)).Success);
                Assert.True((await client.UseItemAsync("item_fashion_crimson_robe", 1)).Success);
                Assert.True((await client.MountSummonAsync("mount_white_horse")).Success);
                Assert.True((await client.FashionEquipAsync("fashion_crimson_robe")).Success);
            }
            finally
            {
                await client.DisposeAsync();
            }

            var client2 = new LegendTcpClient();
            await client2.ConnectAsync("127.0.0.1", engine.BoundPort);
            Assert.True((await client2.HandshakeAsync("1.17.0", $"pc-{user}-2")).Accepted);
            Assert.True((await client2.LoginAsync(user, "secret12345", "PC")).Success);
            await client2.SelectCharAsync(charId);
            var realms = await client2.GetRealmListAsync();
            var enter2 = await client2.EnterWorldAsync(realms.Realms[0].RealmId, charId);
            Assert.True(enter2.Success, enter2.Message);
            Assert.Equal("mount_white_horse", enter2.SelfEntity!.MountId);
            Assert.Equal("fashion_crimson_robe", enter2.SelfEntity.FashionId);
            Assert.True(enter2.SelfEntity.MoveIntervalMs < 300);
            Assert.True(enter2.EffectiveAtk >= atk0 + 2);
            await client2.DisposeAsync();
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }
}
