using Microsoft.UI.Xaml;

namespace CartLaunchCompanion;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        StartupLog("App constructor entered.");
        UnhandledException += (_, e) =>
        {
            StartupLog("Unhandled WinUI exception: " + e.Exception);
        };
        InitializeComponent();
        StartupLog("App XAML initialized.");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupLog("OnLaunched entered.");
            _window = new MainWindow();
            StartupLog("MainWindow constructed.");
            _window.Activate();
            StartupLog("MainWindow activated.");
        }
        catch (Exception ex)
        {
            StartupLog("Startup failed: " + ex);
            throw;
        }
    }

    internal static void StartupLog(string message)
    {
        try
        {
            Directory.CreateDirectory(PortablePaths.DataDirectory);
            File.AppendAllText(
                Path.Combine(PortablePaths.DataDirectory, "Startup.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
