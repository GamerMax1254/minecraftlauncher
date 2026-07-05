using Avalonia;
using MinecraftLauncher.Core.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MinecraftLauncher;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Логи рядом с exe
        var exeDir = AppContext.BaseDirectory;
        var logDir = Path.Combine(exeDir, "logs");
        Logger.Initialize(logDir);

        // Ловим все необработанные исключения
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Logger.Fatal("Unhandled exception", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.Fatal("Application crashed", ex);
            throw;
        }
        finally
        {
            Logger.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}