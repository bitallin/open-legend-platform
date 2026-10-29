using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Legend.Launcher.Desktop.ViewModels;
using Legend.Launcher.Desktop.Views;

namespace Legend.Launcher.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            // Fire-and-forget initial load so list is ready when window shows.
            _ = vm.RefreshRealmsCommand.ExecuteAsync(null);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
