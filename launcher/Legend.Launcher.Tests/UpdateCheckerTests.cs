using Legend.Launcher.Core;

namespace Legend.Launcher.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.2.0", "1.1.9", 1)]
    [InlineData("0.1.0", "0.2.0", -1)]
    [InlineData("1.21.0", "1.21.0", 0)]
    [InlineData("1.9.0", "1.10.0", -1)]
    public void CompareVersions(string a, string b, int sign)
    {
        int c = UpdateChecker.CompareVersions(a, b);
        Assert.Equal(sign, Math.Sign(c));
    }

    [Fact]
    public void Check_UpToDate()
    {
        var checker = new UpdateChecker();
        var result = checker.Check(
            new LocalVersionInfo { ClientVersion = "0.2.0", Channel = "stable" },
            new UpdateManifest { LatestVersion = "0.2.0", Channel = "stable" });
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Check_UpdateAvailable()
    {
        var checker = new UpdateChecker();
        var result = checker.Check(
            new LocalVersionInfo { ClientVersion = "0.1.0", Channel = "stable" },
            new UpdateManifest
            {
                LatestVersion = "0.2.0",
                Channel = "stable",
                MinSupportedVersion = "0.1.0",
                Mandatory = false
            });
        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public void Check_Mandatory_WhenBelowMin()
    {
        var checker = new UpdateChecker();
        var result = checker.Check(
            new LocalVersionInfo { ClientVersion = "0.0.5", Channel = "stable" },
            new UpdateManifest
            {
                LatestVersion = "0.2.0",
                Channel = "stable",
                MinSupportedVersion = "0.1.0",
                Mandatory = false
            });
        Assert.Equal(UpdateStatus.MandatoryUpdate, result.Status);
    }

    [Fact]
    public async Task FileChannel_FetchAndFakeDownload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "legend-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string manifestPath = Path.Combine(dir, "m.json");
            await File.WriteAllTextAsync(manifestPath, """
            {
              "latestVersion": "9.9.9",
              "channel": "stable",
              "minSupportedVersion": "0.0.1",
              "packageUrl": "",
              "packageSizeBytes": 1000,
              "sha256": "abc",
              "releaseNotes": "t",
              "mandatory": false
            }
            """);
            string verPath = Path.Combine(dir, "version.json");
            await File.WriteAllTextAsync(verPath, """{ "clientVersion": "1.0.0", "channel": "stable" }""");

            var channel = new FileUpdateChannel(manifestPath, simulateDownload: true);
            var checker = new UpdateChecker();
            var check = await checker.CheckAsync(verPath, channel);
            Assert.Equal(UpdateStatus.UpdateAvailable, check.Status);

            string cache = Path.Combine(dir, "cache");
            var reports = new List<DownloadProgress>();
            var prog = new Progress<DownloadProgress>(p => reports.Add(p));
            var done = await channel.DownloadAsync(check.Remote!, cache, prog);
            Assert.True(done.Completed);
            Assert.True(File.Exists(done.LocalPath));
            Assert.Contains(reports, r => r.Percent > 0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
