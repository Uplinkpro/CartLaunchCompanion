using Avalonia;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Host;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        using var instance = CartHostInstanceLock.TryAcquire();
        if (instance is null) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
