using System.Net;
using System.Text.Json;
using Legend.CliClient;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Host;
using Legend.Storage;

namespace Legend.Tests;

public class MapFlagsTests
{
    [Fact]
    public void MapFlags_SchemaAcceptsNoPk_AndTopLevelNoHangStillRejected()
    {
        var validator = new ConfigSchemaValidator();
        string withFlags = """
            {"mapId":"tiny","mapName":"Tiny","width":4,"height":4,"spawnX":1,"spawnY":1,
             "mapFlags":{"noPk":true,"noHang":false}}
            """;
        validator.Validate(ConfigSchemaIds.Map, withFlags, "flags.json");

        string topLevel = """
            {"mapId":"tiny","mapName":"Tiny","width":4,"height":4,"spawnX":1,"spawnY":1,"noHang":true}
            """;
        var ex = Assert.Throws<ConfigSchemaException>(() =>
            validator.Validate(ConfigSchemaIds.Map, topLevel, "top.json"));
        Assert.Contains("noHang", ex.Message);
    }

    [Fact]
    public void MapFlags_Loader_NoPkTrue_AndSampleMapsDefaultFalse()
    {
        string root = Path.Combine(Path.GetTempPath(), "legend-mapflags-" + Guid.NewGuid().ToString("N"));
        string maps = Path.Combine(root, "maps");
        Directory.CreateDirectory(maps);
        File.WriteAllText(Path.Combine(maps, "arena.json"), """
            {
              "mapId": "arena",
              "mapName": "Arena",
              "width": 16,
              "height": 16,
              "aoiCellSize": 8,
              "aoiRadiusCells": 1,
              "spawnX": 4,
              "spawnY": 4,
              "blockedCells": [],
              "safeZones": [],
              "portals": [],
              "mapFlags": { "noPk": true }
            }
            """);
        // Minimal pack: reuse sample classic configs via relative copy would be heavy;
        // deserialize MapConfig directly for the flag, and load sample-classic for defaults.
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var cfg = JsonSerializer.Deserialize<MapConfig>(File.ReadAllText(Path.Combine(maps, "arena.json")), opts)!;
        Assert.True(cfg.IsNoPk);
        Assert.True(cfg.MapFlags.NoPk);

        var pack = new VersionPackLoader().LoadFromManifest(RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.All(pack.Maps.Values, m => Assert.False(m.IsNoPk));
    }

    [Fact]
    public async Task MapFlags_NoPk_RejectsOpenPvp_DefaultMapUnchanged()
    {
        string source = Path.GetDirectoryName(RepoFile("version-packs", "sample-classic", "manifest.json"))!;
        string dest = Path.Combine(Path.GetTempPath(), "legend-nopk-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, dest);

        string mapPath = Path.Combine(dest, "maps", "novice_plain.json");
        using (var doc = JsonDocument.Parse(File.ReadAllText(mapPath)))
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("mapFlags"))
                        continue;
                    prop.WriteTo(writer);
                }
                writer.WritePropertyName("mapFlags");
                writer.WriteStartObject();
                writer.WriteBoolean("noPk", true);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            File.WriteAllBytes(mapPath, stream.ToArray());
        }

        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-nopk-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(Path.Combine(dest, "manifest.json"), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            Assert.True(engine.VersionPack.Maps["novice_plain"].IsNoPk);

            var (a, _, _) = await EnterAsAsync(engine, "nopk_a_" + Guid.NewGuid().ToString("N")[..6], "甲" + Guid.NewGuid().ToString("N")[..4], "warrior");
            var (b, _, _) = await EnterAsAsync(engine, "nopk_b_" + Guid.NewGuid().ToString("N")[..6], "乙" + Guid.NewGuid().ToString("N")[..4], "mage");

            // Leave safe zone (5..18) so gate is not ERR_SAFE_ZONE.
            await WalkAsync(a, 22, 10);
            await WalkAsync(a, 25, 10);
            await WalkAsync(b, 22, 10);
            await WalkAsync(b, 26, 10);

            var mode = await a.SetAttackModeAsync("all");
            Assert.True(mode.Success, mode.Message);

            var (deny, _) = await a.AttackAsync(b.SelfEntity!.EntityId, "basic_slash", force: true);
            Assert.False(deny.Success);
            Assert.Equal(ProtocolErrorCode.ErrInvalidTarget, deny.ErrorCode);
            Assert.Contains("禁止PK", deny.Message);
        }
        finally
        {
            await engine.DisposeAsync();
            try { Directory.Delete(dest, true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task MapFlags_DefaultSample_OpenPvpStillAllowedOutsideSafeZone()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"legend-pkok-{Guid.NewGuid():N}.db");
        try { File.Delete(dbPath); } catch { }

        var storage = await StorageFactory.CreateAsync(new StorageOptions { Backend = "Sqlite", SqlitePath = dbPath });
        var engine = new LegendServerEngine(RepoFile("version-packs", "sample-classic", "manifest.json"), port: 0, IPAddress.Loopback, storage);
        await engine.StartAsync();
        try
        {
            Assert.False(engine.VersionPack.Maps["novice_plain"].IsNoPk);

            var (a, _, _) = await EnterAsAsync(engine, "pkok_a_" + Guid.NewGuid().ToString("N")[..6], "丙" + Guid.NewGuid().ToString("N")[..4], "warrior");
            var (b, _, _) = await EnterAsAsync(engine, "pkok_b_" + Guid.NewGuid().ToString("N")[..6], "丁" + Guid.NewGuid().ToString("N")[..4], "mage");

            await WalkAsync(a, 22, 10);
            await WalkAsync(a, 25, 10);
            await WalkAsync(b, 22, 10);
            await WalkAsync(b, 26, 10);

            var mode = await a.SetAttackModeAsync("all");
            Assert.True(mode.Success, mode.Message);

            var (atk, combat) = await a.AttackAsync(b.SelfEntity!.EntityId, "basic_slash", force: true);
            Assert.True(atk.Success, atk.Message);
            Assert.NotNull(combat);
            Assert.True(combat!.Damage > 0);
        }
        finally
        {
            await engine.DisposeAsync();
            try { File.Delete(dbPath); } catch { }
        }
    }

    private static async Task<(LegendTcpClient Client, string CharId, EnterWorldResp Enter)> EnterAsAsync(
        LegendServerEngine engine, string user, string charName, string cls, CancellationToken ct = default)
    {
        var client = new LegendTcpClient();
        await client.ConnectAsync("127.0.0.1", engine.BoundPort, ct);
        Assert.True((await client.HandshakeAsync("1.21.0", $"pc-{user}", ct)).Accepted);
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
            Assert.True(m.Accepted, m.Reason);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(dest, Path.GetFileName(directory)));
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
}
