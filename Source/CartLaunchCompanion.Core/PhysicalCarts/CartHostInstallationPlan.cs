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

    public static CartHostInstallationPlan ForAllUsers()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("All-users Host installation is available only on Windows.");
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cart Launch Companion", "Host");
        // Installation is machine-wide, but trust decisions and logs remain isolated per signed-in user.
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CartLaunchCompanion", "Host", "Data");
        return new CartHostInstallationPlan(runtime, data, Path.Combine(runtime, "CartLaunchCompanion.Host.exe"),
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\CartLaunchCompanionHost",
            Path.Combine(data, "settings.json"), Path.Combine(data, "trusted-carts.json"), Path.Combine(data, "Logs"))
        { Scope = CartHostInstallScope.AllUsers };
    }
}
