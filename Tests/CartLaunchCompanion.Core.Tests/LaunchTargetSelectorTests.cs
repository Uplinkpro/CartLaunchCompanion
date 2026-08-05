using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class LaunchTargetSelectorTests
{
    [Fact]
    public void Select_UsesCurrentPlatformWhenAutomatic()
    {
        var configuration = new GameConfiguration
        {
            Launch =
            {
                PreferredPlatform = PreferredPlatform.Automatic,
                Windows =
                {
                    Launcher = LauncherKind.Xbox,
                    XboxAppId = "Example!App"
                },
                Linux =
                {
                    Launcher = LauncherKind.Steam,
                    SteamId = "620"
                }
            }
        };

        var selector = new LaunchTargetSelector();

        var target = selector.Select(
            configuration,
            PlatformKind.Linux);

        Assert.NotNull(target);
        Assert.Equal(PlatformKind.Linux, target.Platform);
        Assert.Equal(LauncherKind.Steam, target.Launcher);
        Assert.Equal("620", target.SteamId);
    }

    [Fact]
    public void Select_RespectsExplicitPreferredPlatform()
    {
        var configuration = new GameConfiguration
        {
            Launch =
            {
                PreferredPlatform = PreferredPlatform.Windows,
                Windows =
                {
                    Launcher = LauncherKind.Xbox,
                    XboxAppId = "Example!App"
                }
            }
        };

        var selector = new LaunchTargetSelector();

        var target = selector.Select(
            configuration,
            PlatformKind.Linux);

        Assert.NotNull(target);
        Assert.Equal(PlatformKind.Windows, target.Platform);
        Assert.Equal("Example!App", target.ApplicationId);
    }
}
