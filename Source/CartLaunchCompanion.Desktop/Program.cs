using Avalonia;
using System;
using System.Diagnostics;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Core.Storage;

namespace CartLaunchCompanion.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigurePortableDiagnostics();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void ConfigurePortableDiagnostics()
    {
        try
        {
            var paths = new PortablePathService().Discover(AppContext.BaseDirectory);
            var maintenance = new StorageMaintenanceService();
            maintenance.EnsureDirectories(paths.Logs, paths.Cache);
            maintenance.TrimLogs(paths.Logs);
            maintenance.TrimCache(paths.Cache);

            var logPath = Path.Combine(
                paths.Logs,
                $"CartLaunchCompanion-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            Trace.Listeners.Add(new TextWriterTraceListener(logPath));
            Trace.AutoFlush = true;
            Trace.WriteLine($"[{DateTimeOffset.Now:O}] Cart Launch Companion starting.");

            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                Trace.WriteLine($"Unhandled exception: {eventArgs.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                Trace.WriteLine($"Unobserved task exception: {eventArgs.Exception}");
                eventArgs.SetObserved();
            };
        }
        catch
        {
            // Diagnostics must never prevent the launcher from starting.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
#if DEBUG
            .LogToTrace()
#endif
            ;
}
