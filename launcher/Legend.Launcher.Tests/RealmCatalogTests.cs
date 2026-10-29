using Legend.Launcher.Core;

namespace Legend.Launcher.Tests;

public class RealmCatalogTests
{
    [Fact]
    public void ParseDocument_ObjectArray_Works()
    {
        const string json = """
        {
          "realms": [
            { "realmId": "a", "realmName": "区A", "host": "1.1.1.1", "port": 7100, "status": "OPEN" },
            { "realmId": "b", "host": "2.2.2.2", "port": 7200 }
          ]
        }
        """;
        var list = RealmCatalogService.ParseDocument(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("区A", list[0].RealmName);
        Assert.Equal("b", list[1].RealmName); // defaulted to id
        Assert.Equal("OPEN", list[1].Status);
    }

    [Fact]
    public void ParseDocument_BareArray_Works()
    {
        const string json = """
        [
          { "realmId": "x", "realmName": "X", "host": "127.0.0.1", "port": 1 }
        ]
        """;
        var list = RealmCatalogService.ParseDocument(json);
        Assert.Single(list);
        Assert.Equal("x", list[0].RealmId);
    }

    [Fact]
    public void ParseDocument_SingleObject_Works()
    {
        const string json = """{ "realmId": "solo", "realmName": "单区", "host": "0.0.0.0", "port": 9 }""";
        var list = RealmCatalogService.ParseDocument(json);
        Assert.Single(list);
        Assert.Equal("solo", list[0].RealmId);
    }

    [Fact]
    public async Task LoadAsync_LocalFile_ThenRemoteOverride()
    {
        string dir = Path.Combine(Path.GetTempPath(), "legend-launcher-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string local = Path.Combine(dir, "local.json");
            await File.WriteAllTextAsync(local, """
            { "realms": [ { "realmId": "a", "realmName": "本地A", "host": "127.0.0.1", "port": 7100 } ] }
            """);
            string remote = Path.Combine(dir, "remote.json");
            await File.WriteAllTextAsync(remote, """
            { "realms": [ { "realmId": "a", "realmName": "远端A", "host": "10.0.0.1", "port": 7100 },
                          { "realmId": "b", "realmName": "远端B", "host": "10.0.0.2", "port": 7200 } ] }
            """);

            var svc = new RealmCatalogService();
            var list = await svc.LoadAsync(local, remote);
            Assert.Equal(2, list.Count);
            var a = list.First(r => r.RealmId == "a");
            Assert.Equal("远端A", a.RealmName);
            Assert.Equal("10.0.0.1", a.Host);
            Assert.Contains(list, r => r.RealmId == "b");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ParseDocument_IgnoresStringFileRefs()
    {
        const string json = """
        { "realms": [ "realm-classic-01.json", { "realmId": "keep", "realmName": "Keep", "host": "h", "port": 1 } ] }
        """;
        var list = RealmCatalogService.ParseDocument(json);
        Assert.Single(list);
        Assert.Equal("keep", list[0].RealmId);
    }
}
