using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Core.Tests;

public sealed class LinuxLaunchAutoConfiguratorTests
{
    [Fact]
    public void Apply_CopiesSteamIdToLinuxSteam()
    {
        var configuration = Configuration(LauncherKind.Steam);
        configuration.Launch.Windows.SteamId = "620";

        var result = LinuxLaunchAutoConfigurator.Apply(configuration);

        Assert.True(result.Applied);
        Assert.True(configuration.Launch.Linux.Enabled);
        Assert.Equal(LauncherKind.Steam, configuration.Launch.Linux.Launcher);
        Assert.Equal("620", configuration.Launch.Linux.SteamId);
    }

    [Fact]
    public void Apply_UsesHeroicForEpicAppName()
    {
        var configuration = Configuration(LauncherKind.Epic);
        configuration.Launch.Windows.EpicAppName = "Fortnite";

        LinuxLaunchAutoConfigurator.Apply(configuration);

        Assert.Equal(LauncherKind.Heroic, configuration.Launch.Linux.Launcher);
        Assert.Equal("Fortnite", configuration.Launch.Linux.HeroicGameId);
    }

    [Fact]
    public void Apply_UsesWineForPortableWindowsExecutableWithoutOverwritingArguments()
    {
        var configuration = Configuration(LauncherKind.Local);
        configuration.Launch.Windows.Executable = "../../Games/Test/Test.exe";
        configuration.Launch.Windows.Arguments = "--fullscreen";
        configuration.Launch.Linux.Arguments = "--user-choice";

        LinuxLaunchAutoConfigurator.Apply(configuration);

        Assert.Equal(LauncherKind.Wine, configuration.Launch.Linux.Launcher);
        Assert.Equal(configuration.Launch.Windows.Executable, configuration.Launch.Linux.Executable);
        Assert.Equal("--user-choice", configuration.Launch.Linux.Arguments);
        Assert.Equal("wine", configuration.Launch.Linux.CompatibilityTool);
    }

    [Fact]
    public void Apply_ConvertsPairedRetroArchPathsForLinux()
    {
        var configuration = Configuration(LauncherKind.Custom);
        configuration.Launch.Windows.Executable = "../../Emulators/Windows/RetroArch/retroarch.exe";
        configuration.Launch.Windows.Arguments = "-f -L \"../../Emulators/Windows/RetroArch/cores/mgba_libretro.dll\" \"../../Roms/game.gba\"";

        LinuxLaunchAutoConfigurator.Apply(
            configuration,
            "../../Emulators/Linux/RetroArch/RetroArch.AppImage");

        Assert.Equal(LauncherKind.Custom, configuration.Launch.Linux.Launcher);
        Assert.Contains("Emulators/Linux/RetroArch/cores/mgba_libretro.so", configuration.Launch.Linux.Arguments);
        Assert.DoesNotContain(".dll", configuration.Launch.Linux.Arguments);
    }

    private static GameConfiguration Configuration(LauncherKind windowsLauncher)
    {
        var configuration = new GameConfiguration();
        configuration.Launch.Windows.Launcher = windowsLauncher;
        configuration.Launch.Linux.Enabled = false;
        return configuration;
    }
}
