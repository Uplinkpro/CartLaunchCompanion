namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record CartHostInstallationPlan(
    string InstallDirectory, string ExecutablePath, string StartupRegistration,
    string SettingsPath, string TrustDatabasePath, string LogsDirectory)
{
    public static CartHostInstallationPlan ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "CartLaunchCompanion", "Host");
        var executable = OperatingSystem.IsWindows() ? "CartLaunchCompanion.Host.exe" : "CartLaunchCompanion.Host";
        var startup = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Cart Launch Host.lnk")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", "cart-launch-host.desktop");
        return new CartHostInstallationPlan(root, Path.Combine(root, executable), startup,
            Path.Combine(root, "settings.json"), Path.Combine(root, "trusted-carts.json"), Path.Combine(root, "Logs"));
    }
}
