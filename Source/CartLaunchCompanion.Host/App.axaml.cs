using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CartLaunchCompanion.Core.PhysicalCarts;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace CartLaunchCompanion.Host;

public sealed partial class App : Application
{
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
            desktop.MainWindow = window;
            var reviewIndex = Array.IndexOf(Program.Arguments, "--review-cart");
            if (reviewIndex >= 0 && reviewIndex + 1 < Program.Arguments.Length)
                window.Opened += async (_, _) => await window.ReviewPreparedCartAsync(Program.Arguments[reviewIndex + 1]);
            if (Program.Arguments.Contains("--background", StringComparer.Ordinal))
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                window.Opened += async (_, _) =>
                {
                    window.Hide();
                    await window.StartBackgroundMonitoringAsync();
                };
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
