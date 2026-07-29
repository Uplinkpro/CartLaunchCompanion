using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Launching;

public sealed class LaunchTargetSelector : ILaunchTargetSelector
{
    public LaunchTargetSelection? Select(
        GameConfiguration configuration,
        PlatformKind currentPlatform)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var selectedPlatform = configuration.Launch.PreferredPlatform switch
        {
            PreferredPlatform.Windows => PlatformKind.Windows,
            PreferredPlatform.Linux => PlatformKind.Linux,
            _ => currentPlatform
        };

        return selectedPlatform switch
        {
            PlatformKind.Windows =>
                FromWindows(configuration.Launch.Windows),

            PlatformKind.Linux =>
                FromLinux(configuration.Launch.Linux),

            _ => null
        };
    }

    private static LaunchTargetSelection FromWindows(
        WindowsLaunchConfiguration launch)
    {
        var applicationId = launch.Launcher switch
        {
            LauncherKind.Xbox => launch.XboxAppId,
            LauncherKind.Epic => launch.EpicAppName,
            LauncherKind.GOG => launch.GogGameId,
            LauncherKind.Ubisoft => launch.UbisoftGameId,
            LauncherKind.Rockstar => launch.RockstarGameId,
            LauncherKind.Amazon => launch.AmazonGameId,
            _ => ""
        };

        return new LaunchTargetSelection(
            PlatformKind.Windows,
            launch.Launcher,
            launch.Enabled,
            launch.SteamId,
            applicationId,
            launch.Executable,
            launch.Arguments,
            launch.WorkingDirectory,
            launch.ProcessName,
            launch.Uri,
            "",
            "");
    }

    private static LaunchTargetSelection FromLinux(
        LinuxLaunchConfiguration launch)
    {
        var applicationId = launch.Launcher switch
        {
            LauncherKind.Heroic => launch.HeroicGameId,
            LauncherKind.Flatpak => launch.FlatpakAppId,
            _ => ""
        };

        return new LaunchTargetSelection(
            PlatformKind.Linux,
            launch.Launcher,
            launch.Enabled,
            launch.SteamId,
            applicationId,
            launch.Executable,
            launch.Arguments,
            launch.WorkingDirectory,
            launch.ProcessName,
            launch.Uri,
            launch.CompatibilityTool,
            launch.WinePrefix);
    }
}
