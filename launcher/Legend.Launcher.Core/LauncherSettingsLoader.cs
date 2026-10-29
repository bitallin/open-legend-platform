using System.Text.Json;

namespace Legend.Launcher.Core;

public static class LauncherSettingsLoader
{
    public static LauncherSettings Load(string? path, string? baseDir = null)
    {
        baseDir ??= Directory.GetCurrentDirectory();
        var settings = new LauncherSettings();

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            string json = File.ReadAllText(path);
            settings = JsonSerializer.Deserialize<LauncherSettings>(json, JsonDefaults.Options) ?? new LauncherSettings();
        }

        settings.RealmsPath = Resolve(baseDir, settings.RealmsPath);
        settings.VersionPath = Resolve(baseDir, settings.VersionPath);
        if (!string.IsNullOrWhiteSpace(settings.UpdateManifestPath))
            settings.UpdateManifestPath = Resolve(baseDir, settings.UpdateManifestPath!);
        if (!string.IsNullOrWhiteSpace(settings.ClientExecutable))
            settings.ClientExecutable = Resolve(baseDir, settings.ClientExecutable);
        settings.DownloadCacheDir = Resolve(baseDir, settings.DownloadCacheDir);
        return settings;
    }

    private static string Resolve(string baseDir, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }
}
