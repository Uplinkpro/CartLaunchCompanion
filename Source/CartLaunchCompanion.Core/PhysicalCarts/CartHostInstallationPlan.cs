namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record CartHostInstallationPlan(
    string InstallDirectory, string DataDirectory, string ExecutablePath, string StartupRegistration,
    string SettingsPath, string TrustDatabasePath, string LogsDirectory)
{
    public static CartHostInstallationPlan ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "CartLaunchCompanion", "Host");
        var runtime = Path.Combine(root, "Runtime");
        var data = Path.Combine(root, "Data");
        var executable = OperatingSystem.IsWindows() ? "CartLaunchCompanion.Host.exe" : "CartLaunchCompanion.Host";
        var startup = OperatingSystem.IsWindows()
            ? @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CartLaunchCompanionHost"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", "cart-launch-host.desktop");
        return new CartHostInstallationPlan(runtime, data, Path.Combine(runtime, executable), startup,
            Path.Combine(data, "settings.json"), Path.Combine(data, "trusted-carts.json"), Path.Combine(data, "Logs"));
    }
}
