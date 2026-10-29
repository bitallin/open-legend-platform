using System.Net;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class M16IntegrationTests
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
        Assert.True((await client.HandshakeAsync("1.16.0", $"pc-{user}", ct)).Accepted);
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
            Assert.True(sync.Accepted, sync.Reason);
            x = sync.ToX;
            y = sync.ToY;
        }
    }

    private static async Task<EntitySnapshot> FindMonsterAsync(LegendTcpClient client, EnterWorldResp enter, CancellationToken ct = default)
    {
        EntitySnapshot? m = enter.NearbyEntities.FirstOrDefault(e => e.Kind == EntityKind.EntityMonster);
        for (int i = 0; i < 50 && m is null; i++)
        {
            while (client.ReceivedAoiEnters.TryDequeue(out var en))
                if (en.Entity.Kind == EntityKind.EntityMonster) { m = en.Entity; break; }
            while (m is null && client.ReceivedEntitySpawns.TryDequeue(out var sp))
                if (sp.Entity.Kind == EntityKind.EntityMonster) { m = sp.Entity; break; }
            if (m is null)
            {
                await WalkToAsync(client, 25 + (i % 3), 10 + (i % 2), ct);
                await Task.Delay(40, ct);
            }
        }
        Assert.NotNull(m);
        return m!;
    }

    [Fact]
    public void Pets_VersionPack_LoadsConfigs()
    {
        var loader = new VersionPackLoader();
        var pack = loader.LoadFromManifest(GetManifestPath());
        Assert.Equal("1.16.0", pack.Manifest.ProtocolVersion);
        Assert.Equal("1.16.0", ProtocolConstants.CurrentVersion);
        Assert.NotEmpty(pack.Pets.Pets);
        Assert.Contains(pack.Pets.Pets, p => p.PetId == "pet_wolf_pup" && p.AssistDamagePercent >= 1 && p.AssistCooldownMs >= 200);
        Assert.True(pack.Items.ContainsKey("item_pet_wolf_pup"));
        Assert.Equal("pet_unlock", pack.Items["item_pet_wolf_pup"].Kind);
        Assert.Equal("pet_wolf_pup", pack.Items["item_pet_wolf_pup"].UnlockPetId);
    }

    [Fact]
    public async Task Pet_Unlock_Summon_Aoi_Assist_Dismiss()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            var (owner, _, enter) = await EnterAsAsync(engine, $"m16o_{suffix}", $"Own{suffix}", "warrior");
            var (watcher, _, _) = await EnterAsAsync(engine, $"m16w_{suffix}", $"Wch{suffix}", "mage");
            try
            {
                var buy = await owner.ShopBuyAsync("npc_village_merchant", "item_pet_wolf_pup", 1);
                Assert.True(buy.Success, buy.Message);
                Assert.True((await owner.UseItemAsync("item_pet_wolf_pup", 1)).Success);

                var st = await owner.PetStatusAsync();
                Assert.True(st.Success);
                Assert.Contains(st.Pets, p => p.PetId == "pet_wolf_pup" && p.Owned);

                while (watcher.ReceivedEntitySpawns.TryDequeue(out _)) { }
                while (owner.ReceivedPetSyncs.TryDequeue(out _)) { }

                var summon = await owner.PetSummonAsync("pet_wolf_pup");
                Assert.True(summon.Success, summon.Message);
                Assert.Equal("pet_wolf_pup", summon.ActivePetId);
                Assert.False(string.IsNullOrEmpty(summon.PetEntityId));

                var spawn = await watcher.WaitEntitySpawnAsync(
                    s => s.Entity != null && s.Entity.EntityId == summon.PetEntityId && s.Entity.Kind == EntityKind.EntityPet, 4000);
                Assert.Equal("pet_wolf_pup", spawn.Entity.PetId);

                var sync = await owner.WaitPetSyncAsync(n => n.PetId == "pet_wolf_pup", 3000);
                Assert.Equal(summon.PetEntityId, sync.PetEntityId);

                // Follow: leave safe zone (safe ~5..18) then walk
                Assert.NotNull(owner.SelfEntity);
                await WalkToAsync(owner, 22, 10);
                await Task.Delay(300);

                var monster = await FindMonsterAsync(owner, enter);
                await WalkToAsync(owner, monster.X - 1, monster.Y);
                await Task.Delay(900); // let pet catch-up snap / follow into assistRange
                while (owner.ReceivedCombatSyncs.TryDequeue(out _)) { }
                var (atk, combat) = await owner.AttackAsync(monster.EntityId, "basic_slash");
                Assert.True(atk.Success, atk.Message);
                Assert.NotNull(combat);

                var assist = await owner.WaitCombatSyncAsync(
                    c => c.SkillId == "pet_assist" && c.AttackerEntityId == summon.PetEntityId && c.TargetEntityId == monster.EntityId,
                    5000);
                Assert.True(assist.Damage >= 1);

                var deny = await owner.PetSummonAsync("no_such_pet");
                Assert.False(deny.Success);
                Assert.Equal(ProtocolErrorCode.ErrPetDenied, deny.ErrorCode);

                var dismiss = await owner.PetDismissAsync();
                Assert.True(dismiss.Success);
                var st2 = await owner.PetStatusAsync();
                Assert.Equal("", st2.ActivePetId);
            }
            finally
            {
                await owner.DisposeAsync();
                await watcher.DisposeAsync();
            }
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Pet_Persists_Active_Across_ReEnter()
    {
        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "InMemory" });
        var engine = new LegendServerEngine(GetManifestPath(), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..4];
            string user = $"m16p_{suffix}";
            string charName = $"Pet{suffix}";
            var (client, charId, _) = await EnterAsAsync(engine, user, charName, "warrior");
            try
            {
                Assert.True((await client.ShopBuyAsync("npc_village_merchant", "item_pet_wolf_pup", 1)).Success);
                Assert.True((await client.UseItemAsync("item_pet_wolf_pup", 1)).Success);
                Assert.True((await client.PetSummonAsync("pet_wolf_pup")).Success);
            }
            finally
            {
                await client.DisposeAsync();
            }

            var client2 = new LegendTcpClient();
            await client2.ConnectAsync("127.0.0.1", engine.BoundPort);
            Assert.True((await client2.HandshakeAsync("1.16.0", $"pc-{user}-2")).Accepted);
            Assert.True((await client2.LoginAsync(user, "secret12345", "PC")).Success);
            await client2.SelectCharAsync(charId);
            var realms = await client2.GetRealmListAsync();
            var enter2 = await client2.EnterWorldAsync(realms.Realms[0].RealmId, charId);
            Assert.True(enter2.Success, enter2.Message);
            var st = await client2.PetStatusAsync();
            Assert.Equal("pet_wolf_pup", st.ActivePetId);
            Assert.False(string.IsNullOrEmpty(st.ActivePetEntityId));
            Assert.Contains(st.Pets, p => p.PetId == "pet_wolf_pup" && p.Owned && p.Active);
            // Self snapshot may carry pet_id on owner? Owner entity PetId field reflects ActivePetId
            Assert.Equal("pet_wolf_pup", enter2.SelfEntity!.PetId);
            await client2.DisposeAsync();
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }
}
