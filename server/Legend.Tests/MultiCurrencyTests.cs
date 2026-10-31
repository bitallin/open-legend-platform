using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Game;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class MultiCurrencyTests
{
    [Fact]
    public void Currencies_Schema_And_SamplePack_LoadGoldOnly()
    {
        var validator = new ConfigSchemaValidator();
        validator.Validate(ConfigSchemaIds.Currencies, """
            [{"currencyId":"gold","displayName":"金币","tradable":true,"stallAllowed":true,"auctionAllowed":true,"deductGroup":"gold"}]
            """, "currencies.json");
        validator.Validate(ConfigSchemaIds.Recycle, """
            [{"itemId":"item_slime_gel","currencyId":"gold","amount":5,"dailyLimit":10,"destroyItem":true}]
            """, "recycle.json");

        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.True(pack.Currencies.ContainsKey("gold"));
        Assert.Single(pack.Currencies);
        Assert.True(pack.Recycle.ContainsKey("item_slime_gel"));
        Assert.Equal(5, pack.Recycle["item_slime_gel"].Amount);
        Assert.Equal(10, pack.Recycle["item_slime_gel"].DailyLimit);
    }

    [Fact]
    public void Currencies_Loader_RejectsUnknownRecycleCurrency()
    {
        string source = Path.GetDirectoryName(RepoFile("version-packs", "sample-classic", "manifest.json"))!;
        string dest = Path.Combine(Path.GetTempPath(), "legend-cur-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, dest);
        try
        {
            File.WriteAllText(Path.Combine(dest, "configs", "recycle.json"), """
                [{"itemId":"item_slime_gel","currencyId":"ingot","amount":1,"dailyLimit":0,"destroyItem":true}]
                """);
            var ex = Assert.ThrowsAny<Exception>(() =>
                new VersionPackLoader().LoadFromManifest(Path.Combine(dest, "manifest.json")));
            Assert.Contains("ingot", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dest, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CurrencyWallet_DeductGroup_PrefersBound()
    {
        var currencies = new Dictionary<string, CurrencyConfigItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["gold"] = new() { CurrencyId = "gold", DisplayName = "金币", DeductGroup = "gold" },
            ["gold_bound"] = new() { CurrencyId = "gold_bound", DisplayName = "绑定金币", DeductGroup = "gold" }
        };
        var wallet = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["gold"] = 30,
            ["gold_bound"] = 40
        };
        Assert.True(CurrencyWallet.TryDebit(wallet, "gold", 50, currencies));
        Assert.Equal(0, wallet["gold_bound"]);
        Assert.Equal(20, wallet["gold"]);
    }

    [Fact]
    public async Task Recycle_DailyLimit_And_GoldCredit()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-recycle-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        await using var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        await using var engine = new LegendServerEngine(
            RepoFile("version-packs", "sample-classic", "manifest.json"),
            port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();

        Assert.True(engine.VersionPack.Recycle.ContainsKey("item_slime_gel"));

        await using var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort);
        Assert.True((await client.HandshakeAsync("1.21.0")).Accepted);
        string user = "recycle_" + Guid.NewGuid().ToString("N")[..8];
        await client.RegisterAndLoginAsync(user, "secret-recycle-1");
        var create = await client.CreateCharAsync("Rc" + Guid.NewGuid().ToString("N")[..6], "warrior");
        Assert.True(create.Success, create.Message);
        string charId = create.Character.CharacterId;
        await client.SelectCharAsync(charId);

        // Seed slime gel before first enter so world loads it.
        var ch = await storage.Characters.GetByIdAsync(charId);
        Assert.NotNull(ch);
        var inv = InventoryHelper.Parse(ch!.InventoryJson);
        InventoryHelper.AddItem(inv, "item_slime_gel", 15);
        await storage.Characters.SaveInventoryAsync(charId, InventoryHelper.Serialize(inv));

        var enter = await client.EnterWorldAsync("realm-classic-01", charId);
        Assert.True(enter.Success, enter.Message);
        int goldBefore = enter.Gold;

        var npc = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityNpc && e.TemplateId == "npc_village_merchant");
        if (npc is null)
        {
            await WalkToAsync(client, 12, 12);
            for (int i = 0; i < 20 && npc is null; i++)
            {
                while (client.ReceivedEntitySpawns.TryDequeue(out var sp))
                {
                    if (sp.Entity.Kind == EntityKind.EntityNpc && sp.Entity.TemplateId == "npc_village_merchant")
                    {
                        npc = sp.Entity;
                        break;
                    }
                }
                await Task.Delay(30);
            }
        }
        Assert.NotNull(npc);
        await WalkToAsync(client, npc!.X, npc.Y);

        var sell1 = await client.ShopSellAsync(npc.EntityId, "item_slime_gel", 10);
        Assert.True(sell1.Success, sell1.Message);
        Assert.Equal(50, sell1.GoldGained);
        Assert.Equal(goldBefore + 50, sell1.Gold);

        var sellOver = await client.ShopSellAsync(npc.EntityId, "item_slime_gel", 1);
        Assert.False(sellOver.Success);
        Assert.Contains("limit", sellOver.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WalkToAsync(LegendTcpClient client, int tx, int ty, CancellationToken ct = default)
    {
        Assert.NotNull(client.SelfEntity);
        int x = client.SelfEntity!.X;
        int y = client.SelfEntity.Y;
        int guard = 0;
        while ((x != tx || y != ty) && guard++ < 80)
        {
            int nx = x, ny = y;
            if (x != tx)
                nx = x + Math.Clamp(tx - x, -2, 2);
            else
                ny = y + Math.Clamp(ty - y, -2, 2);
            var sync = await client.MoveAsync(nx, ny, ct: ct);
            Assert.True(sync.Accepted, $"Move to ({nx},{ny}) failed: {sync.Reason}");
            x = sync.ToX;
            y = sync.ToY;
        }
        Assert.Equal(tx, client.SelfEntity.X);
        Assert.Equal(ty, client.SelfEntity.Y);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(dest, Path.GetFileName(directory)));
    }
}
