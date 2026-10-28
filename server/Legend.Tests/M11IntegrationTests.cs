using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M11IntegrationTests
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
        while (c.SelfEntity!.X != tx || c.SelfEntity.Y != ty)
        {
            int nx = c.SelfEntity.X + Math.Sign(tx - c.SelfEntity.X);
            int ny = c.SelfEntity.Y + Math.Sign(ty - c.SelfEntity.Y);
            var m = await c.MoveAsync(nx, ny, ct: ct);
            Assert.True(m.Accepted, $"move to ({nx},{ny}) rejected");
        }
    }

    [Fact]
    public async Task Mail_Send_List_Claim_AntiDup_Delete_And_Persist()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m11-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        string suffix = Guid.NewGuid().ToString("N")[..4];
        string userA = $"m11a_{suffix}";
        string userB = $"m11b_{suffix}";
        string nameA = $"甲{suffix}";
        string nameB = $"乙{suffix}";
        string mailId;
        int attachGold = 40;

        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (a, _, _) = await EnterAsAsync(engine, userA, nameA, "warrior");
                var (b, idB, enterB) = await EnterAsAsync(engine, userB, nameB, "mage");
                await using var _a = a;
                await using var _b = b;

                var send = await a.MailSendAsync(nameB, "系统测试", "附赠金币", attachGold, ct: default);
                Assert.True(send.Success, send.Message);
                mailId = send.MailId;

                var list = await b.MailListAsync();
                Assert.True(list.Success);
                Assert.Contains(list.Mails, m => m.MailId == mailId);
                var entry = list.Mails.First(m => m.MailId == mailId);
                Assert.False(entry.IsRead);
                Assert.True(entry.HasUnclaimedAttachment);
                Assert.Equal(attachGold, entry.Attachments.Gold);

                var read = await b.MailReadAsync(mailId);
                Assert.True(read.Success);
                Assert.True(read.Mail.IsRead);

                var delDeny = await b.MailDeleteAsync(mailId);
                Assert.False(delDeny.Success);
                Assert.Equal(ProtocolErrorCode.ErrMailAttachUnclaimed, delDeny.ErrorCode);

                int gold0 = b.Gold;
                var claim = await b.MailClaimAsync(mailId);
                Assert.True(claim.Success, claim.Message);
                Assert.Equal(gold0 + attachGold, b.Gold);

                var claim2 = await b.MailClaimAsync(mailId);
                Assert.False(claim2.Success);
                Assert.Equal(ProtocolErrorCode.ErrMailAlreadyClaimed, claim2.ErrorCode);

                var del = await b.MailDeleteAsync(mailId);
                Assert.True(del.Success, del.Message);

                // Leave a mail for persistence
                var keep = await a.MailSendAsync(nameB, "持久化", "重启保留", 13);
                Assert.True(keep.Success, keep.Message);
                mailId = keep.MailId;
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        // Restart host on same DB
        {
            var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
            var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
            await engine.StartAsync();
            try
            {
                var (b, _, _) = await EnterAsAsync(engine, userB, nameB, "mage");
                await using var _b = b;
                var list = await b.MailListAsync();
                Assert.True(list.Success);
                Assert.Contains(list.Mails, m => m.MailId == mailId && m.HasUnclaimedAttachment);
                int gold0 = b.Gold;
                var claim = await b.MailClaimAsync(mailId);
                Assert.True(claim.Success, claim.Message);
                Assert.Equal(gold0 + 13, b.Gold);
            }
            finally
            {
                await engine.DisposeAsync();
            }
        }

        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public async Task Stall_OfflineSeller_GetsMailGold_NotDirectGold()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m11-stall-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }
        string suffix = Guid.NewGuid().ToString("N")[..4];

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            var (seller, sellerId, enterS) = await EnterAsAsync(engine, $"m11s_{suffix}", $"卖{suffix}", "warrior");
            var (buyer, _, _) = await EnterAsAsync(engine, $"m11u_{suffix}", $"买{suffix}", "mage");
            await using var _buyer = buyer;

            await WalkAsync(seller, 12, 12);
            var npc = enterS.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityNpc);
            if (npc is null)
            {
                for (int i = 0; i < 40 && npc is null; i++)
                {
                    while (seller.ReceivedEntitySpawns.TryDequeue(out var sp))
                        if (sp.Entity.Kind == EntityKind.EntityNpc) { npc = sp.Entity; break; }
                    await Task.Delay(25);
                }
            }
            Assert.NotNull(npc);
            await WalkAsync(seller, npc!.X, npc.Y);
            var shop = await seller.ShopBuyAsync(npc.EntityId, "item_hp_potion", 1);
            Assert.True(shop.Success, shop.Message);
            await WalkAsync(seller, 10, 10);
            var list = await seller.StallListAsync("item_hp_potion", 1, 22);
            Assert.True(list.Success, list.Message);
            string listingId = list.Listing.ListingId;
            int sellerGoldBefore = seller.Gold;

            // Offline seller
            await seller.DisposeAsync();

            await WalkAsync(buyer, 10, 10);
            var buy = await buyer.StallBuyAsync(listingId);
            Assert.True(buy.Success, buy.Message);

            // Seller gold in DB must be unchanged (mail path)
            var sellerChar = await storage.Characters.GetByIdAsync(sellerId);
            Assert.NotNull(sellerChar);
            Assert.Equal(sellerGoldBefore, sellerChar!.Gold);

            // Re-enter and claim mail
            var (seller2, _, enter2) = await EnterAsAsync(engine, $"m11s_{suffix}", $"卖{suffix}", "warrior");
            await using var _s2 = seller2;
            Assert.Equal(sellerGoldBefore, enter2.Gold);
            var mails = await seller2.MailListAsync();
            var stallMail = mails.Mails.FirstOrDefault(m => m.HasUnclaimedAttachment && m.Subject.Contains("摊位"));
            Assert.NotNull(stallMail);
            Assert.Equal(22, stallMail!.Attachments.Gold);
            var claim = await seller2.MailClaimAsync(stallMail.MailId);
            Assert.True(claim.Success, claim.Message);
            Assert.Equal(sellerGoldBefore + 22, seller2.Gold);
        }
        finally
        {
            await engine.DisposeAsync();
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task Siege_OfflineMember_GetsRewardMail()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-m11-siege-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }
        string suffix = Guid.NewGuid().ToString("N")[..4];
        int rewardGold;

        var pack = new VersionPackLoader().LoadFromManifest(GetManifestPath());
        rewardGold = pack.Siege.WinnerRewardGold;
        Assert.True(rewardGold > 0);

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            var (leader, _, _) = await EnterAsAsync(engine, $"m11gl_{suffix}", $"盟主{suffix}", "warrior");
            var (member, memberId, enterM) = await EnterAsAsync(engine, $"m11gm_{suffix}", $"成员{suffix}", "mage");
            await using var _l = leader;

            string gName = $"邮会{suffix}";
            var create = await leader.GuildCreateAsync(gName);
            Assert.True(create.Success, create.Message);
            var join = await member.GuildJoinAsync(gName);
            Assert.True(join.Success, join.Message);

            int memberGoldBefore = enterM.Gold;

            // Member goes offline
            await member.DisposeAsync();

            var decl = await leader.SiegeDeclareAsync();
            Assert.True(decl.Success, decl.Message);

            // Portal at (10,8) -> dark_forest capture zone
            await leader.MoveAsync(10, 8);
            for (int w = 0; w < 60 && leader.SelfEntity?.MapId != "dark_forest"; w++)
                await Task.Delay(25);
            Assert.Equal("dark_forest", leader.SelfEntity!.MapId);

            var start = await leader.SiegeControlAsync("start");
            Assert.True(start.Success, start.Message);

            var cap = await leader.SiegeCaptureAsync();
            Assert.True(cap.Success, cap.Message);

            var stop = await leader.SiegeControlAsync("stop");
            Assert.True(stop.Success, stop.Message);

            // Online leader got instant gold
            // Offline member: gold unchanged in DB, mail pending
            var memberChar = await storage.Characters.GetByIdAsync(memberId);
            Assert.NotNull(memberChar);
            Assert.Equal(memberGoldBefore, memberChar!.Gold);

            var (member2, _, enter2) = await EnterAsAsync(engine, $"m11gm_{suffix}", $"成员{suffix}", "mage");
            await using var _m2 = member2;
            Assert.Equal(memberGoldBefore, enter2.Gold);
            var mails = await member2.MailListAsync();
            var rewardMail = mails.Mails.FirstOrDefault(m => m.HasUnclaimedAttachment && m.Subject.Contains("攻城"));
            Assert.NotNull(rewardMail);
            Assert.Equal(rewardGold, rewardMail!.Attachments.Gold);
            var claim = await member2.MailClaimAsync(rewardMail.MailId);
            Assert.True(claim.Success, claim.Message);
            Assert.Equal(memberGoldBefore + rewardGold, member2.Gold);
        }
        finally
        {
            await engine.DisposeAsync();
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void VersionPack_MailConfig_Loaded()
    {
        var pack = new VersionPackLoader().LoadFromManifest(GetManifestPath());
        Assert.Equal("1.12.0", pack.Manifest.ProtocolVersion);
        Assert.Equal(50, pack.Mail.MaxMailsPerCharacter);
        Assert.Equal(ProtocolConstants.CurrentVersion, "1.12.0");
    }
}
