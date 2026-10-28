using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M7IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.11.0", $"pc-{user}", ct)).Accepted);
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

    private static async Task WalkAsync(LegendTcpClient c, int tx, int ty)
    {
        while (c.SelfEntity!.X != tx || c.SelfEntity.Y != ty)
        {
            int nx = c.SelfEntity.X + Math.Sign(tx - c.SelfEntity.X);
            int ny = c.SelfEntity.Y + Math.Sign(ty - c.SelfEntity.Y);
            var m = await c.MoveAsync(nx, ny);
            Assert.True(m.Accepted);
        }
    }

    [Fact]
    public async Task Stall_SafeZone_List_Cancel_Buy_And_Persist_Across_Restart()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m7-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string persistedListingId;
        int sellerGoldBeforeRestart;

        // Phase 1: list, safe-zone rejection, cancel, buy, leave one active listing for restart
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (seller, sellerId, enterS) = await EnterAsAsync(engine, "m7_seller", "摊主甲", "warrior");
                var (buyer, buyerId, enterB) = await EnterAsAsync(engine, "m7_buyer", "买家乙", "mage");
                try
                {
                    // Seller buys 3 potions from NPC in safe zone (12,12)
                    await WalkAsync(seller, 12, 12);
                    var npc = enterS.NearbyEntities.First(e => e.Kind == EntityKind.EntityNpc);
                    var shopBuy = await seller.ShopBuyAsync(npc.EntityId, "item_hp_potion", 3);
                    Assert.True(shopBuy.Success, shopBuy.Message);
                    int potionsInitial = shopBuy.Inventory.First(i => i.ItemId == "item_hp_potion").Count;
                    Assert.Equal(3, potionsInitial);

                    // Walk outside safe zone (20,10) -> StallList must be rejected with ErrSafeZone
                    await WalkAsync(seller, 20, 10);
                    Assert.False(seller.SelfEntity!.InSafeZone);
                    var listOutside = await seller.StallListAsync("item_hp_potion", 1, 15);
                    Assert.False(listOutside.Success);
                    Assert.Equal(ProtocolErrorCode.ErrSafeZone, listOutside.ErrorCode);

                    // Walk back into safe zone (10,10) and list 3 items
                    await WalkAsync(seller, 10, 10);
                    Assert.True(seller.SelfEntity.InSafeZone);

                    var list1 = await seller.StallListAsync("item_hp_potion", 1, 15);
                    Assert.True(list1.Success, list1.Message);
                    Assert.Equal(2, list1.Inventory.First(i => i.ItemId == "item_hp_potion").Count);

                    var list2 = await seller.StallListAsync("item_hp_potion", 1, 20);
                    Assert.True(list2.Success, list2.Message);
                    Assert.Equal(1, list2.Inventory.First(i => i.ItemId == "item_hp_potion").Count);

                    var list3 = await seller.StallListAsync("item_hp_potion", 1, 25);
                    Assert.True(list3.Success, list3.Message);
                    Assert.DoesNotContain(list3.Inventory, i => i.ItemId == "item_hp_potion");
                    persistedListingId = list3.Listing.ListingId;

                    // Cancel list2 -> 1 potion returned to seller bag
                    var cancel2 = await seller.StallCancelAsync(list2.Listing.ListingId);
                    Assert.True(cancel2.Success, cancel2.Message);
                    Assert.Equal(1, cancel2.Inventory.First(i => i.ItemId == "item_hp_potion").Count);

                    // Self-buy rejected
                    var selfBuy = await seller.StallBuyAsync(list1.Listing.ListingId);
                    Assert.False(selfBuy.Success);
                    Assert.Equal(ProtocolErrorCode.ErrSelfBuyDenied, selfBuy.ErrorCode);

                    // Buyer browses active listings (should see list1 & list3, not cancelled list2)
                    var browse = await buyer.StallBrowseAsync("item_hp_potion");
                    Assert.True(browse.Success, browse.Message);
                    Assert.Equal(2, browse.Listings.Count);
                    Assert.Contains(browse.Listings, l => l.ListingId == list1.Listing.ListingId);
                    Assert.Contains(browse.Listings, l => l.ListingId == list3.Listing.ListingId);

                    int sellerGoldBefore = seller.Gold;
                    int buyerGoldBefore = buyer.Gold;

                    // Buyer buys list1
                    var buy1 = await buyer.StallBuyAsync(list1.Listing.ListingId);
                    Assert.True(buy1.Success, buy1.Message);
                    Assert.Equal(buyerGoldBefore - 15, buy1.Gold);
                    Assert.Equal(1, buy1.Inventory.First(i => i.ItemId == "item_hp_potion").Count);

                    // Seller receives StallSoldNotify and gold increases by 15
                    var soldNotify = await seller.WaitStallSoldAsync(n => n.ListingId == list1.Listing.ListingId);
                    Assert.Equal(sellerGoldBefore + 15, soldNotify.NewGold);
                    Assert.Equal(sellerGoldBefore + 15, seller.Gold);
                    sellerGoldBeforeRestart = seller.Gold;

                    // Duplicate buy on already-sold list1 rejected
                    var dupBuy = await buyer.StallBuyAsync(list1.Listing.ListingId);
                    Assert.False(dupBuy.Success);
                    Assert.Equal(ProtocolErrorCode.ErrListingNotFound, dupBuy.ErrorCode);
                }
                finally
                {
                    await seller.DisposeAsync();
                    await buyer.DisposeAsync();
                }
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        // Phase 2: restart Host on same SQLite DB -> active listing list3 survives and can be bought while seller is offline; seller claims gold via mail (M11)
        {
            var storage2 = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine2 = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage2);
            await engine2.StartAsync();
            try
            {
                var (buyer2, _, enterB2) = await EnterAsAsync(engine2, "m7_buyer", "买家乙", "mage");
                try
                {
                    var browse2 = await buyer2.StallBrowseAsync("item_hp_potion");
                    Assert.True(browse2.Success);
                    var remaining = Assert.Single(browse2.Listings);
                    Assert.Equal(persistedListingId, remaining.ListingId);
                    Assert.Equal(25, remaining.TotalPriceGold);

                    int buyerGoldBefore2 = enterB2.Gold;
                    var buy3 = await buyer2.StallBuyAsync(persistedListingId);
                    Assert.True(buy3.Success, buy3.Message);
                    Assert.Equal(buyerGoldBefore2 - 25, buy3.Gold);
                    Assert.Equal(2, buy3.Inventory.First(i => i.ItemId == "item_hp_potion").Count);
                }
                finally
                {
                    await buyer2.DisposeAsync();
                }

                // Offline seller logs in: gold unchanged until mail claim (M11 offline payout via mail attachment).
                var (seller2, _, enterS2) = await EnterAsAsync(engine2, "m7_seller", "摊主甲", "warrior");
                try
                {
                    Assert.Equal(sellerGoldBeforeRestart, enterS2.Gold);
                    var mails = await seller2.MailListAsync();
                    Assert.True(mails.Success);
                    var stallMail = Assert.Single(mails.Mails.Where(m => m.HasUnclaimedAttachment && m.Attachments.Gold > 0));
                    Assert.Equal(25, stallMail.Attachments.Gold);
                    var claim = await seller2.MailClaimAsync(stallMail.MailId);
                    Assert.True(claim.Success, claim.Message);
                    Assert.Equal(sellerGoldBeforeRestart + 25, seller2.Gold);
                }
                finally
                {
                    await seller2.DisposeAsync();
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
    public async Task InMemoryTradeStore_Create_Claim_And_Cancel_Roundtrip()
    {
        var store = new InMemoryStorage();
        await store.InitializeAsync();

        var rec = await store.Trades.CreateAsync(new TradeListingRecord
        {
            SellerCharacterId = "c-seller",
            SellerName = "Seller",
            ItemId = "item_hp_potion",
            Count = 2,
            UnitPriceGold = 10,
            TotalPriceGold = 20
        });
        Assert.False(string.IsNullOrEmpty(rec.ListingId));
        Assert.Equal(1, await store.Trades.CountActiveBySellerAsync("c-seller"));

        // Self-claim denied
        var selfClaim = await store.Trades.TryClaimSoldAsync(rec.ListingId, "c-seller", "Seller");
        Assert.False(selfClaim.Ok);

        // Buyer claims
        var claim = await store.Trades.TryClaimSoldAsync(rec.ListingId, "c-buyer", "Buyer");
        Assert.True(claim.Ok);
        Assert.Equal("sold", claim.Listing!.Status);

        // Second claim denied
        var dup = await store.Trades.TryClaimSoldAsync(rec.ListingId, "c-buyer2", "Buyer2");
        Assert.False(dup.Ok);

        // Create second and cancel
        var rec2 = await store.Trades.CreateAsync(new TradeListingRecord
        {
            SellerCharacterId = "c-seller",
            SellerName = "Seller",
            ItemId = "item_mp_potion",
            Count = 1,
            UnitPriceGold = 12,
            TotalPriceGold = 12
        });
        var wrongCancel = await store.Trades.TryCancelAsync(rec2.ListingId, "c-other");
        Assert.False(wrongCancel.Ok);
        var okCancel = await store.Trades.TryCancelAsync(rec2.ListingId, "c-seller");
        Assert.True(okCancel.Ok);
        Assert.Empty(await store.Trades.ListActiveAsync());
    }
}
