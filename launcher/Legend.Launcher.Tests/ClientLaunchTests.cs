using Legend.Launcher.Core;

namespace Legend.Launcher.Tests;

public class ClientLaunchTests
{
    [Fact]
    public void BuildArguments_SubstitutesTokens()
    {
        var launcher = new ClientProcessLauncher();
        var realm = new RealmEntry
        {
            RealmId = "realm-classic-01",
            RealmName = "一区",
            Host = "10.1.2.3",
            Port = 7100
        };
        string args = launcher.BuildArguments("--host {host} --port {port} --realm-id {realmId}", realm);
        Assert.Equal("--host 10.1.2.3 --port 7100 --realm-id realm-classic-01", args);
    }

    [Fact]
    public async Task Launch_ClientStub_ExitsZero()
    {
        // Build output of ClientStub should exist after solution build; locate relative to test assembly
        string stubDll = FindStubDll();
        Assert.True(File.Exists(stubDll), $"ClientStub dll missing: {stubDll}");

        var launcher = new ClientProcessLauncher();
        var realm = new RealmEntry
        {
            RealmId = "realm-classic-01",
            Host = "127.0.0.1",
            Port = 7100
        };
        string args = launcher.BuildArguments("--host {host} --port {port} --realm-id {realmId}", realm);
        var result = await launcher.LaunchAsync(stubDll, args, waitForExit: true);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("host=127.0.0.1", result.Message);
        Assert.Contains("realm-id=realm-classic-01", result.Message);
    }

    static string FindStubDll()
    {
        // Prefer freshly built path next to repo
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Legend.ClientStub", "bin", "Release", "net8.0", "Legend.ClientStub.dll")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Legend.ClientStub", "bin", "Debug", "net8.0", "Legend.ClientStub.dll")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Legend.ClientStub.dll")),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }
        return candidates[0];
    }
}
