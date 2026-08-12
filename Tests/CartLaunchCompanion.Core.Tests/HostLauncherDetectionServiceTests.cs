using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class HostLauncherDetectionServiceTests
{
    [Theory]
    [InlineData(LauncherKind.Local)]
    [InlineData(LauncherKind.Custom)]
    [InlineData(LauncherKind.Wine)]
    [InlineData(LauncherKind.Proton)]
    public void CartManagedLaunchersDoNotScanHost(LauncherKind launcher)
    {
        var result = new HostLauncherDetectionService().Detect(launcher, PlatformKind.Windows);

        Assert.True(result.Found);
        Assert.Equal("Cart-managed", result.Location);
    }
}
