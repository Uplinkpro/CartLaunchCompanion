namespace CartLaunchCompanion.Core.Configuration.Validation;

public sealed class GameConfigurationValidator
{
    public ConfigurationValidationResult Validate(
        GameConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var result = new ConfigurationValidationResult();

        if (configuration.FormatVersion != 2)
        {
            AddError(
                result,
                "formatVersion",
                "Cart Launch Companion 2.0 requires formatVersion 2.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Game.Name))
        {
            AddError(
                result,
                "game.name",
                "A display name is required.");
        }

        ValidateBehavior(configuration.Behavior, result);
        ValidateWindows(configuration.Launch.Windows, result);
        ValidateLinux(configuration.Launch.Linux, result);

        if (!configuration.Launch.Windows.Enabled &&
            !configuration.Launch.Linux.Enabled)
        {
            AddError(
                result,
                "launch",
                "At least one platform launch configuration must be enabled.");
        }

        return result;
    }

    private static void ValidateBehavior(
        BehaviorConfiguration behavior,
        ConfigurationValidationResult result)
    {
        if (behavior.ProcessStartTimeoutSeconds is < 1 or > 3600)
        {
            AddError(
                result,
                "behavior.processStartTimeoutSeconds",
                "The process start timeout must be between 1 and 3600 seconds.");
        }

        if (behavior.ProcessExitPollSeconds is < 1 or > 60)
        {
            AddError(
                result,
                "behavior.processExitPollSeconds",
                "The process exit poll interval must be between 1 and 60 seconds.");
        }
    }

    private static void ValidateWindows(
        WindowsLaunchConfiguration launch,
        ConfigurationValidationResult result)
    {
        if (!launch.Enabled)
            return;

        var hasTarget = launch.Launcher switch
        {
            LauncherKind.Steam => Has(launch.SteamId) || Has(launch.Uri),
            LauncherKind.Xbox => Has(launch.XboxAppId) || Has(launch.Uri),
            LauncherKind.Epic => Has(launch.EpicAppName) || Has(launch.Uri),
            LauncherKind.GOG => Has(launch.GogGameId) ||
                                Has(launch.Executable) ||
                                Has(launch.Uri),
            LauncherKind.Ubisoft => Has(launch.UbisoftGameId) ||
                                     Has(launch.Uri),
            LauncherKind.Rockstar => Has(launch.RockstarGameId) ||
                                     Has(launch.Uri),
            LauncherKind.Amazon => Has(launch.AmazonGameId) ||
                                   Has(launch.Uri),
            LauncherKind.Local => Has(launch.Executable),
            LauncherKind.Custom => Has(launch.Executable) || Has(launch.Uri),
            _ => Has(launch.Executable) || Has(launch.Uri)
        };

        if (!hasTarget)
        {
            AddError(
                result,
                "launch.windows",
                $"The enabled Windows {launch.Launcher} configuration has no launch target.");
        }

        if (launch.Launcher == LauncherKind.Local &&
            string.IsNullOrWhiteSpace(launch.Executable))
        {
            AddError(
                result,
                "launch.windows.executable",
                "A local Windows game requires an executable.");
        }
    }

    private static void ValidateLinux(
        LinuxLaunchConfiguration launch,
        ConfigurationValidationResult result)
    {
        if (!launch.Enabled)
            return;

        var hasTarget = launch.Launcher switch
        {
            LauncherKind.Steam => Has(launch.SteamId) || Has(launch.Uri),
            LauncherKind.Heroic => Has(launch.HeroicGameId) ||
                                   Has(launch.Uri),
            LauncherKind.Flatpak => Has(launch.FlatpakAppId) ||
                                    Has(launch.Executable),
            LauncherKind.Local => Has(launch.Executable),
            LauncherKind.Wine => Has(launch.Executable),
            LauncherKind.Proton => Has(launch.SteamId) ||
                                   Has(launch.Executable),
            LauncherKind.Custom => Has(launch.Executable) || Has(launch.Uri),
            _ => Has(launch.Executable) || Has(launch.Uri)
        };

        if (!hasTarget)
        {
            AddError(
                result,
                "launch.linux",
                $"The enabled Linux {launch.Launcher} configuration has no launch target.");
        }

        if (launch.Launcher is LauncherKind.Wine or LauncherKind.Proton &&
            !Has(launch.Executable) &&
            !Has(launch.SteamId))
        {
            AddError(
                result,
                "launch.linux.executable",
                "Wine or Proton requires an executable or Steam App ID.");
        }
    }

    private static bool Has(string value) =>
        !string.IsNullOrWhiteSpace(value);

    private static void AddError(
        ConfigurationValidationResult result,
        string path,
        string message)
    {
        result.Issues.Add(
            new ConfigurationValidationIssue(
                path,
                message,
                ValidationSeverity.Error));
    }
}
