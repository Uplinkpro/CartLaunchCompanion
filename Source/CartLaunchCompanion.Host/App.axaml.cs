using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CartLaunchCompanion.Core.PhysicalCarts;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace CartLaunchCompanion.Host;

public sealed partial class App : Application
{
    private TrayIcon? _trayIcon;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsWindows() && Program.Arguments.Contains("--install-all-users", StringComparer.Ordinal))
            {
                InstallAllUsersAsync(desktop);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var window = new MainWindow();
            var background = Program.Arguments.Contains("--background", StringComparer.Ordinal);
            // The desktop lifetime automatically shows MainWindow on startup.
            // A sign-in launch must create only the notification-area icon.
            if (!background) desktop.MainWindow = window;
            using (var iconStream = AssetLoader.Open(new Uri("avares://CLC-CartMonitor/Assets/AppIcon.png")))
            {
                _trayIcon = new TrayIcon
                {
                    Icon = new WindowIcon(iconStream),
                    ToolTipText = "CLC-Cart Monitor",
                    IsVisible = true
                };
            }
            void OpenMonitor()
            {
                desktop.MainWindow = window;
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
                _ = window.ScanMountedCartsAsync();
            }
            var open = new NativeMenuItem("Open Monitor");
            open.Click += (_, _) => OpenMonitor();
            var exit = new NativeMenuItem("Exit Monitor");
            exit.Click += async (_, _) =>
            {
                exit.IsEnabled = false;
                try
                {
                    await window.StopMonitoringAsync();
                    desktop.Shutdown();
                }
                catch (Exception ex)
                {
                    window.Status = "Monitor could not exit cleanly: " + ex.Message;
                    exit.IsEnabled = true;
                    OpenMonitor();
                }
            };
            _trayIcon.Menu = new NativeMenu { Items = { open, exit } };
            _trayIcon.Clicked += (_, _) => OpenMonitor();
            desktop.Exit += (_, _) => _trayIcon.Dispose();
            window.Closed += (_, _) => desktop.Shutdown();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.EnableBackgroundMode();
            var reviewIndex = Array.IndexOf(Program.Arguments, "--review-cart");
            if (reviewIndex >= 0 && reviewIndex + 1 < Program.Arguments.Length)
                window.Opened += async (_, _) => await window.ReviewPreparedCartAsync(Program.Arguments[reviewIndex + 1]);
            if (background)
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await window.StartBackgroundMonitoringAsync();
                });
            }
            else
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    window.Show();
                    window.Activate();
                    await window.ScanMountedCartsAsync();
                    window.StartPassiveMonitoring();
                });
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    [SupportedOSPlatform("windows")]
    private static async void InstallAllUsersAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var plan = CartHostInstallationPlan.ForAllUsers();
            await new CartHostInstallationService().InstallFilesAsync(AppContext.BaseDirectory, plan);
            using var key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key.SetValue("CLCCartMonitor", $"\"{plan.ExecutablePath}\" --background", RegistryValueKind.String);
        }
        finally { desktop.Shutdown(); }
    }
}
