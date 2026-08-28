namespace CartLaunchCompanion.Core.PhysicalCarts;

public enum CartHostInstallScope { CurrentUser, AllUsers }

public sealed record CartHostInstallationPlan(
    string InstallDirectory, string DataDirectory, string ExecutablePath, string StartupRegistration,
    string SettingsPath, string TrustDatabasePath, string LogsDirectory)
{
    public CartHostInstallScope Scope { get; init; } = CartHostInstallScope.CurrentUser;

    public static CartHostInstallationPlan ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "CartLaunchCompanion", "CartMonitor");
        var runtime = Path.Combine(root, "Runtime");
        var data = Path.Combine(root, "Data");
        var executable = OperatingSystem.IsWindows() ? "CLC-CartMonitor.exe" : "CLC-CartMonitor";
        var startup = OperatingSystem.IsWindows()
            ? @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CLCCartMonitor"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", "clc-cart-monitor.desktop");
        return new CartHostInstallationPlan(runtime, data, Path.Combine(runtime, executable), startup,
            Path.Combine(data, "settings.json"), Path.Combine(data, "trusted-carts.json"), Path.Combine(data, "Logs"));
    }

    public static CartHostInstallationPlan ForAllUsers()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("All-users CLC-Cart Monitor installation is available only on Windows.");
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cart Launch Companion", "Cart Monitor");
        // Installation is machine-wide, but trust decisions and logs remain isolated per signed-in user.
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CartLaunchCompanion", "CartMonitor", "Data");
        return new CartHostInstallationPlan(runtime, data, Path.Combine(runtime, "CLC-CartMonitor.exe"),
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\CLCCartMonitor",
            Path.Combine(data, "settings.json"), Path.Combine(data, "trusted-carts.json"), Path.Combine(data, "Logs"))
        { Scope = CartHostInstallScope.AllUsers };
    }
}
