using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Legend.Launcher.Core;

namespace Legend.Launcher.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly LauncherFacade _facade;
    private UpdateCheckResult? _lastUpdate;

    public MainViewModel() : this(CreateDefaultFacade())
    {
    }

    public MainViewModel(LauncherFacade facade)
    {
        _facade = facade;
        StatusText = "就绪。请刷新区服列表。";
        ClientPath = facade.Settings.ClientExecutable;
        if (string.IsNullOrWhiteSpace(ClientPath))
            ClientPath = "(未配置 — 请把 clientExecutable 指到 dist/windows-player/LegendClient.exe)";
    }

    public ObservableCollection<RealmItemViewModel> Realms { get; } = new();

    [ObservableProperty]
    private RealmItemViewModel? _selectedRealm;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _updateSummary = "尚未检查更新";

    [ObservableProperty]
    private double _downloadPercent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _clientPath = "";

    [ObservableProperty]
    private string _title = "Legend 登录器 (Phase I)";

    private static LauncherFacade CreateDefaultFacade()
    {
        string baseDir = LauncherPaths.FindLauncherRoot(AppContext.BaseDirectory)
                         ?? Directory.GetCurrentDirectory();
        try { Directory.SetCurrentDirectory(baseDir); } catch { /* ignore */ }
        string settingsPath = Path.Combine(baseDir, "config", "launcher.json");
        var settings = LauncherSettingsLoader.Load(settingsPath, baseDir);
        if (string.IsNullOrWhiteSpace(settings.ClientExecutable))
        {
            string stub = Path.GetFullPath(Path.Combine(baseDir, "Legend.ClientStub", "bin", "Release", "net8.0", "Legend.ClientStub.dll"));
            if (!File.Exists(stub))
                stub = Path.GetFullPath(Path.Combine(baseDir, "Legend.ClientStub", "bin", "Debug", "net8.0", "Legend.ClientStub.dll"));
            if (File.Exists(stub))
                settings.ClientExecutable = stub;
            else
            {
                string fake = Path.GetFullPath(Path.Combine(baseDir, "..", "scripts", "fake-client.sh"));
                if (File.Exists(fake))
                    settings.ClientExecutable = fake;
            }
        }
        return new LauncherFacade(settings);
    }

    [RelayCommand]
    private async Task RefreshRealmsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在加载区服…";
        try
        {
            var list = await _facade.LoadRealmsAsync();
            Realms.Clear();
            foreach (var r in list)
                Realms.Add(new RealmItemViewModel(r));
            SelectedRealm = Realms.FirstOrDefault();
            StatusText = $"已加载 {Realms.Count} 个区服";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ProbeLatencyAsync()
    {
        if (IsBusy) return;
        if (Realms.Count == 0)
            await RefreshRealmsAsync();
        if (Realms.Count == 0) return;

        IsBusy = true;
        StatusText = "正在探测延迟…";
        try
        {
            var entries = Realms.Select(r => r.Entry).ToList();
            await _facade.ProbeAsync(entries);
            foreach (var item in Realms)
                item.RefreshLatency();
            StatusText = "延迟探测完成";
        }
        catch (Exception ex)
        {
            StatusText = $"探测失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在检查更新…";
        DownloadPercent = 0;
        try
        {
            _lastUpdate = await _facade.CheckUpdateAsync();
            UpdateSummary = _lastUpdate.Message;
            if (_lastUpdate.Remote is not null)
                UpdateSummary += $"（远端 {_lastUpdate.Remote.LatestVersion}）";
            StatusText = "更新检查完成";
        }
        catch (Exception ex)
        {
            UpdateSummary = $"检查失败：{ex.Message}";
            StatusText = UpdateSummary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在下载更新骨架…";
        try
        {
            _lastUpdate ??= await _facade.CheckUpdateAsync();
            if (_lastUpdate.Remote is null)
            {
                StatusText = "无远端更新清单";
                return;
            }

            var progress = new Progress<DownloadProgress>(p =>
            {
                DownloadPercent = p.Percent;
                StatusText = $"下载 {p.Percent:0.0}%";
            });
            var done = await _facade.DownloadUpdateAsync(_lastUpdate.Remote, progress);
            DownloadPercent = done.Completed ? 100 : DownloadPercent;
            StatusText = done.Completed
                ? $"下载完成：{done.LocalPath}"
                : $"下载未完成：{done.Error ?? "未知"}";
        }
        catch (Exception ex)
        {
            StatusText = $"下载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsBusy) return;
        if (SelectedRealm is null)
        {
            StatusText = "请先选择一个区服";
            return;
        }

        IsBusy = true;
        var realm = SelectedRealm.Entry;
        StatusText = $"正在启动 {realm.RealmName} ({realm.Host}:{realm.Port})…";
        try
        {
            // Phase K: fire-and-forget so Avalonia stays usable while Unity player runs.
            var result = await _facade.LaunchAsync(realm, waitForExit: false);
            StatusText = result.Ok
                ? $"已启动游戏。{result.Message}"
                : $"启动失败：{result.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"启动异常：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Resolve launcher/ folder that contains config/launcher.json.</summary>
public static class LauncherPaths
{
    public static string? FindLauncherRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config", "launcher.json"))
                || File.Exists(Path.Combine(dir.FullName, "config", "realms.json")))
                return dir.FullName;
            if (dir.Name == "launcher" && Directory.Exists(Path.Combine(dir.FullName, "config")))
                return dir.FullName;
            dir = dir.Parent;
        }

        string candidate = Path.GetFullPath(Path.Combine(start, "..", "..", "..", ".."));
        if (File.Exists(Path.Combine(candidate, "config", "launcher.json")))
            return candidate;
        return null;
    }
}
