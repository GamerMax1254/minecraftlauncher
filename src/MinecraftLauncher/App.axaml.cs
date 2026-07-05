using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MinecraftLauncher.Core.Logging;
using MinecraftLauncher.Views;

namespace MinecraftLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Logger.Info("Framework initialized");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownRequested += (_, _) =>
            {
                Logger.Info("Application shutting down");
                Logger.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}