using Avalonia;
using CartLaunchCompanion.Core.PhysicalCarts;
using System.Diagnostics;

namespace CartLaunchCompanion.Host;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        if (!WaitForPriorProcess(args)) return;
        using var instance = CartHostInstanceLock.TryAcquire();
        if (instance is null) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool WaitForPriorProcess(string[] args)
    {
        var index = Array.IndexOf(args, "--wait-for-process");
        if (index < 0) return true;
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var processId) || processId <= 0)
            return false;
        if (processId == Environment.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit(30_000);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
#if DEBUG
            .LogToTrace()
#endif
            ;
}
