using Legend.Launcher.Core;

string baseDir = FindLauncherRoot(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
Directory.SetCurrentDirectory(baseDir);

var cli = Args.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
string settingsPath = cli.SettingsPath
    ?? Path.Combine(baseDir, "config", "launcher.json");

var settings = LauncherSettingsLoader.Load(settingsPath, baseDir);
if (!string.IsNullOrWhiteSpace(cli.RealmsPath))
    settings.RealmsPath = Path.GetFullPath(cli.RealmsPath);
if (!string.IsNullOrWhiteSpace(cli.RealmListUrl))
    settings.RealmListUrl = cli.RealmListUrl;
if (!string.IsNullOrWhiteSpace(cli.ClientExe))
    settings.ClientExecutable = Path.GetFullPath(cli.ClientExe);
if (!string.IsNullOrWhiteSpace(cli.UpdateManifest))
{
    if (cli.UpdateManifest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || cli.UpdateManifest.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || cli.UpdateManifest.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        settings.UpdateManifestUrl = cli.UpdateManifest;
    else
        settings.UpdateManifestPath = Path.GetFullPath(cli.UpdateManifest);
}

var facade = new LauncherFacade(settings);

if (cli.Headless)
{
    int code = await RunHeadlessAsync(facade, cli);
    Environment.ExitCode = code;
    return;
}

await RunInteractiveAsync(facade);

static async Task<int> RunHeadlessAsync(LauncherFacade facade, Args args)
{
    Console.WriteLine("[Launcher] headless mode");
    var realms = await facade.LoadRealmsAsync();
    Console.WriteLine($"[Launcher] realms={realms.Count}");
    foreach (var r in realms)
        Console.WriteLine($"  - {r.RealmId} | {r.RealmName} | {r.Host}:{r.Port} | {r.Status}");

    if (args.Probe)
    {
        await facade.ProbeAsync(realms);
        foreach (var r in realms)
            Console.WriteLine($"  ping {r.RealmId}: {(r.LatencyMs is int ms ? ms + "ms" : "unreachable")}");
    }

    UpdateCheckResult? update = null;
    if (args.CheckUpdate)
    {
        update = await facade.CheckUpdateAsync();
        Console.WriteLine($"[Launcher] update: {update.Status} — {update.Message}");
        if (args.Download && update.Remote is not null
            && update.Status is UpdateStatus.UpdateAvailable or UpdateStatus.MandatoryUpdate)
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.TotalBytes is > 0)
                    Console.WriteLine($"  download {p.Percent:0.0}% ({p.BytesReceived}/{p.TotalBytes})");
            });
            var done = await facade.DownloadUpdateAsync(update.Remote, progress);
            Console.WriteLine($"[Launcher] downloaded: {done.LocalPath} completed={done.Completed}");
        }
    }

    if (args.Launch)
    {
        string? id = args.RealmId;
        RealmEntry? realm = null;
        if (!string.IsNullOrWhiteSpace(id))
            realm = realms.FirstOrDefault(r => string.Equals(r.RealmId, id, StringComparison.OrdinalIgnoreCase));
        realm ??= realms.FirstOrDefault();
        if (realm is null)
        {
            Console.Error.WriteLine("[Launcher] no realm to launch");
            return 2;
        }

        Console.WriteLine($"[Launcher] launching {realm.RealmId} via {facade.Settings.ClientExecutable}");
        Console.WriteLine($"[Launcher] args: {facade.BuildArgs(realm)}");
        var result = await facade.LaunchAsync(realm, waitForExit: true);
        Console.WriteLine($"[Launcher] launch ok={result.Ok} exit={result.ExitCode}");
        Console.WriteLine(result.Message);
        return result.Ok ? 0 : 1;
    }

    return 0;
}

static async Task RunInteractiveAsync(LauncherFacade facade)
{
    List<RealmEntry> realms = (await facade.LoadRealmsAsync()).ToList();
    UpdateCheckResult? lastUpdate = null;

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine("║     Legend 登录器骨架 (Phase H)      ║");
        Console.WriteLine("╚══════════════════════════════════════╝");
        Console.WriteLine("  1) 刷新区服列表");
        Console.WriteLine("  2) 探测延迟 (TCP RTT)");
        Console.WriteLine("  3) 检查更新");
        Console.WriteLine("  4) 下载更新（骨架/假进度）");
        Console.WriteLine("  5) 选区并启动客户端");
        Console.WriteLine("  l) 列出当前区服");
        Console.WriteLine("  q) 退出");
        Console.Write("选择> ");
        string? line = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(line))
            continue;
        if (line is "q" or "Q" or "quit" or "exit")
            break;

        try
        {
            switch (line)
            {
                case "1":
                    realms = (await facade.LoadRealmsAsync()).ToList();
                    PrintRealms(realms);
                    break;
                case "2":
                    if (realms.Count == 0)
                        realms = (await facade.LoadRealmsAsync()).ToList();
                    Console.WriteLine("探测中…");
                    await facade.ProbeAsync(realms);
                    PrintRealms(realms);
                    break;
                case "3":
                    lastUpdate = await facade.CheckUpdateAsync();
                    Console.WriteLine(lastUpdate.Message);
                    if (lastUpdate.Remote is not null)
                        Console.WriteLine($"  远端={lastUpdate.Remote.LatestVersion} 说明={lastUpdate.Remote.ReleaseNotes}");
                    break;
                case "4":
                    lastUpdate ??= await facade.CheckUpdateAsync();
                    if (lastUpdate.Remote is null)
                    {
                        Console.WriteLine("无远端清单");
                        break;
                    }
                    if (lastUpdate.Status == UpdateStatus.UpToDate)
                    {
                        Console.WriteLine("已是最新，仍演示下载骨架…");
                    }
                    var prog = new Progress<DownloadProgress>(p =>
                    {
                        Console.Write($"\r  进度 {p.Percent,5:0.0}%  ");
                    });
                    var done = await facade.DownloadUpdateAsync(lastUpdate.Remote, prog);
                    Console.WriteLine();
                    Console.WriteLine($"完成: {done.LocalPath}");
                    break;
                case "5":
                    if (realms.Count == 0)
                        realms = (await facade.LoadRealmsAsync()).ToList();
                    PrintRealms(realms);
                    Console.Write("输入区服序号或 realmId> ");
                    string? pick = Console.ReadLine()?.Trim();
                    var realm = ResolvePick(realms, pick);
                    if (realm is null)
                    {
                        Console.WriteLine("无效选择");
                        break;
                    }
                    Console.WriteLine($"启动 {realm.RealmName} ({realm.Host}:{realm.Port}) …");
                    var result = await facade.LaunchAsync(realm, waitForExit: true);
                    Console.WriteLine(result.Message);
                    Console.WriteLine($"ok={result.Ok} exit={result.ExitCode}");
                    break;
                case "l":
                case "L":
                    PrintRealms(realms);
                    break;
                default:
                    Console.WriteLine("未知命令");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
        }
    }
}

static void PrintRealms(IReadOnlyList<RealmEntry> realms)
{
    if (realms.Count == 0)
    {
        Console.WriteLine("(空列表)");
        return;
    }
    Console.WriteLine($"{"#",-3} {"区服ID",-22} {"名称",-16} {"地址",-22} {"状态",-8} {"延迟",-8}");
    for (int i = 0; i < realms.Count; i++)
    {
        var r = realms[i];
        string lat = r.LatencyMs is int ms ? $"{ms}ms" : "-";
        Console.WriteLine($"{i + 1,-3} {r.RealmId,-22} {Truncate(r.RealmName, 16),-16} {$"{r.Host}:{r.Port}",-22} {r.Status,-8} {lat,-8}");
    }
}

static RealmEntry? ResolvePick(IReadOnlyList<RealmEntry> realms, string? pick)
{
    if (string.IsNullOrWhiteSpace(pick))
        return null;
    if (int.TryParse(pick, out int idx) && idx >= 1 && idx <= realms.Count)
        return realms[idx - 1];
    return realms.FirstOrDefault(r => string.Equals(r.RealmId, pick, StringComparison.OrdinalIgnoreCase));
}

static string Truncate(string s, int max)
    => s.Length <= max ? s : s[..(max - 1)] + "…";

static string? FindLauncherRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "config", "launcher.json"))
            || File.Exists(Path.Combine(dir.FullName, "config", "realms.json")))
            return dir.FullName;
        // also accept repo launcher/ folder when running from bin/
        if (dir.Name == "launcher" && Directory.Exists(Path.Combine(dir.FullName, "config")))
            return dir.FullName;
        dir = dir.Parent;
    }
    // walk up from cwd-relative publish
    string candidate = Path.GetFullPath(Path.Combine(start, "..", "..", "..", ".."));
    if (File.Exists(Path.Combine(candidate, "config", "launcher.json")))
        return candidate;
    return null;
}

sealed class Args
{
    public bool Headless { get; init; }
    public bool Probe { get; init; }
    public bool CheckUpdate { get; init; }
    public bool Download { get; init; }
    public bool Launch { get; init; }
    public string? RealmId { get; init; }
    public string? SettingsPath { get; init; }
    public string? RealmsPath { get; init; }
    public string? RealmListUrl { get; init; }
    public string? ClientExe { get; init; }
    public string? UpdateManifest { get; init; }

    public static Args Parse(string[] argv)
    {
        bool headless = false, probe = false, check = false, download = false, launch = false;
        string? realmId = null, settings = null, realms = null, url = null, client = null, manifest = null;
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            switch (a)
            {
                case "--headless": headless = true; break;
                case "--probe": probe = true; break;
                case "--check-update": check = true; break;
                case "--download": download = true; break;
                case "--launch": launch = true; break;
                case "--realm-id" when i + 1 < argv.Length: realmId = argv[++i]; break;
                case "--settings" when i + 1 < argv.Length: settings = argv[++i]; break;
                case "--realms" when i + 1 < argv.Length: realms = argv[++i]; break;
                case "--realm-list-url" when i + 1 < argv.Length: url = argv[++i]; break;
                case "--client" when i + 1 < argv.Length: client = argv[++i]; break;
                case "--update-manifest" when i + 1 < argv.Length: manifest = argv[++i]; break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }
        return new Args
        {
            Headless = headless || launch || check || probe,
            Probe = probe,
            CheckUpdate = check,
            Download = download,
            Launch = launch,
            RealmId = realmId,
            SettingsPath = settings,
            RealmsPath = realms,
            RealmListUrl = url,
            ClientExe = client,
            UpdateManifest = manifest
        };
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
Legend.Launcher — Phase H 登录器骨架
  (interactive)                 中文菜单
  --headless --probe            加载区服并探测延迟
  --check-update [--download]   检查/下载更新骨架
  --launch --realm-id <id>      启动客户端/stub
  --settings <path>             launcher.json
  --realms <path>               realms.json
  --realm-list-url <url>        可选远端区服列表
  --client <path>               客户端可执行文件 / stub dll / .sh
  --update-manifest <path|url>  更新清单
""");
    }
}
