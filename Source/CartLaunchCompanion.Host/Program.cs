using Avalonia;

namespace CartLaunchCompanion.Host;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static void Main(string[] args) { Arguments = args; BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
