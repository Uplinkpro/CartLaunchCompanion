using System.Diagnostics;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record CartHostStatus(bool IsRunning, bool IsInstalled, CartHostInstallationPlan? InstalledPlan)
{
    public bool IsAvailable => IsRunning || IsInstalled;
}

public sealed class CartHostStatusService
{
    public CartHostStatus Check()
    {
        var running = Process.GetProcessesByName("CLC-CartMonitor").Length > 0;
        var current = CartHostInstallationPlan.ForCurrentUser();
        if (File.Exists(current.ExecutablePath)) return new(running, true, current);
        if (OperatingSystem.IsWindows())
        {
            var all = CartHostInstallationPlan.ForAllUsers();
            if (File.Exists(all.ExecutablePath)) return new(running, true, all);
        }
        return new(running, false, null);
    }
}
