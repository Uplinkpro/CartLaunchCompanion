using Avalonia;

namespace CartLaunchCompanion.EmulatorCompanion;

internal static class Program
{
    public static string? TrustedCartRoot { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        ParseOptions(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void ParseOptions(string[] args)
    {
        if (args.Length == 0) return;
        if (args.Length != 2 || args[0] != "--cart-root" || !Path.IsPathFullyQualified(args[1]))
            throw new ArgumentException("Use --cart-root followed by an absolute Cart folder path.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
        if (!Directory.Exists(root) ||
            !string.Equals(Path.GetFileName(root), "Cart", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(Path.Combine(root, "Games")) ||
            !Directory.Exists(Path.Combine(root, "System")))
            throw new ArgumentException("The cart root is invalid.");
        TrustedCartRoot = root;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
#if DEBUG
            .LogToTrace()
#endif
            ;
}
