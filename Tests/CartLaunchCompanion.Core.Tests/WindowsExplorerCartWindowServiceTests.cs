using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class WindowsExplorerCartWindowServiceTests
{
    [Fact]
    public void MatchesOnlyExactFileLocationForMediaRoot()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(WindowsExplorerCartWindowService.MatchesRootLocation("file:///H:/", @"H:\"));
        Assert.True(WindowsExplorerCartWindowService.MatchesRootLocation("file:///h:/", @"H:\"));
        Assert.False(WindowsExplorerCartWindowService.MatchesRootLocation("file:///H:/Cart/", @"H:\"));
        Assert.False(WindowsExplorerCartWindowService.MatchesRootLocation("https://example.com/", @"H:\"));
        Assert.False(WindowsExplorerCartWindowService.MatchesRootLocation(null, @"H:\"));
    }
}
