using Avalonia;
using System;
using System.Diagnostics;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Core.Storage;

namespace CartLaunchCompanion.Desktop;

sealed class Program
{
    public static string? TrustedCartRoot { get; private set; }
    public static bool CheckForUpdatesRequested { get; private set; }
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ParseOptions(args);
        ConfigurePortableDiagnostics();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void ParseOptions(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--check-for-updates":
                    CheckForUpdatesRequested = true;
                    break;
                case "--cart-root" when index + 1 < args.Length:
                    var value = args[++index];
                    if (!Path.IsPathFullyQualified(value))
                        throw new ArgumentException("The cart root must be absolute.");
                    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
                    if (!Directory.Exists(root) || Path.GetFileName(root) != "Cart" || !Directory.Exists(Path.Combine(root, "Games")))
                        throw new ArgumentException("The trusted cart root is invalid.");
                    TrustedCartRoot = root;
                    break;
                default:
                    throw new ArgumentException($"Unsupported launcher argument: {args[index]}");
            }
        }
    }

    private static void ConfigurePortableDiagnostics()
    {
        try
        {
            var paths = TrustedCartRoot is null ? new PortablePathService().Discover(AppContext.BaseDirectory) : PortablePaths.FromRoot(TrustedCartRoot);
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
