using System.Runtime.InteropServices;
using Avalonia;
using Legend.Playable.Ui;

namespace Legend.Playable;

public static class AppHost
{
    public static LaunchOptions Options { get; set; } = new();
}

public static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        LaunchOptions options;
        try
        {
            options = LaunchOptions.Parse(args);
        }
        catch (Exception ex)
        {
            AttachParentConsole();
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(LaunchOptions.HelpText);
            return 2;
        }

        AppHost.Options = options;
        if (options.ShowHelp)
        {
            AttachParentConsole();
            Console.WriteLine(LaunchOptions.HelpText);
            return 0;
        }
        if (options.Smoke)
        {
            AttachParentConsole();
            return SmokeCheck.Run();
        }
        if (options.Probe)
        {
            AttachParentConsole();
            return ProbeHost.Run(options);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void AttachParentConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            AttachConsole(AttachParentProcess);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch
        {
            /* already attached or no console */
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
}
