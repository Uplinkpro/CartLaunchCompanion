using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PpssppPortablePathsTests
{
    [Theory]
    [InlineData(PlatformKind.Windows, "PPSSPPWindows64.exe")]
    [InlineData(PlatformKind.Linux, "PPSSPP.AppImage")]
    public void ManagedLaunchUsesSharedMemstickAndPlatformLocalConfiguration(
        PlatformKind platform, string executableName)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CLC Cart"));
        var executable = Path.Combine(root, "Emulators", platform.ToString(), "PPSSPP", executableName);

        var arguments = PpssppPortablePaths.AddLaunchArguments(executable, platform,
            ["--fullscreen", "game.iso"]);

        Assert.Contains("--memstick=" + Path.Combine(root, "Emulators", "Shared", "MemorySticks", "PPSSPP"), arguments);
        Assert.Contains("--config=" + PpssppPortablePaths.ConfigurationPath(executable, platform), arguments);
        Assert.Contains("--controlconfig=" + PpssppPortablePaths.ControlsPath(executable, platform), arguments);
        Assert.Equal("game.iso", arguments[^4]);
    }

    [Fact]
    public void ExistingExplicitPathsArePreserved()
    {
        var executable = Path.Combine(Path.GetTempPath(), "Cart", "Emulators", "Windows", "PPSSPP", "PPSSPPWindows64.exe");
        var arguments = PpssppPortablePaths.AddLaunchArguments(executable, PlatformKind.Windows,
            ["--memstick=custom", "--config=custom.ini", "--controlconfig=controls.ini"]);

        Assert.Equal(3, arguments.Count);
        Assert.Contains("--memstick=custom", arguments);
    }
}
