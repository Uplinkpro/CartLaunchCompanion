using System.Text.RegularExpressions;
using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Launching;

public sealed record LinuxLaunchSuggestionResult(bool Applied, string Message);

public static partial class LinuxLaunchAutoConfigurator
{
    public static LinuxLaunchSuggestionResult Apply(
        GameConfiguration configuration,
        string? linuxEmulatorExecutable = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var windows = configuration.Launch.Windows;
        var linux = configuration.Launch.Linux;
        if (!windows.Enabled)
            return new(false, "Enable and configure the Windows launch target first.");

        var targetWasBlank = IsTargetBlank(linux);
        linux.Enabled = true;

        if (windows.Launcher == LauncherKind.Steam && !string.IsNullOrWhiteSpace(windows.SteamId))
        {
            if (targetWasBlank || linux.Launcher is LauncherKind.Steam or LauncherKind.Proton)
                linux.Launcher = LauncherKind.Steam;
            linux.SteamId = Fill(linux.SteamId, windows.SteamId);
            return new(true, "Configured Linux to launch the same Steam App ID. Steam manages the selected Proton version per game.");
        }

        if (windows.Launcher == LauncherKind.Epic && !string.IsNullOrWhiteSpace(windows.EpicAppName))
        {
            if (targetWasBlank || linux.Launcher == LauncherKind.Heroic)
                linux.Launcher = LauncherKind.Heroic;
            linux.HeroicGameId = Fill(linux.HeroicGameId, windows.EpicAppName);
            return new(true, "Configured Heroic with the Epic app name. Verify the installed edition before saving.");
        }

        if (windows.Launcher == LauncherKind.Custom)
        {
            if (string.IsNullOrWhiteSpace(linuxEmulatorExecutable))
                return new(false, "The Windows emulator was recognized, but its matching Linux executable or AppImage is not installed in the Emulator Library.");

            if (targetWasBlank || linux.Launcher == LauncherKind.Custom)
                linux.Launcher = LauncherKind.Custom;
            linux.Executable = Fill(linux.Executable, linuxEmulatorExecutable);
            linux.Arguments = Fill(linux.Arguments, ConvertEmulatorArguments(windows.Arguments));
            linux.WorkingDirectory = Fill(linux.WorkingDirectory, Path.GetDirectoryName(linuxEmulatorExecutable)?.Replace('\\', '/') ?? "");
            linux.ProcessName = Fill(linux.ProcessName, Path.GetFileNameWithoutExtension(linuxEmulatorExecutable));
            return new(true, "Configured the matching Linux emulator. Verify the Linux core path when RetroArch is used.");
        }

        if (!string.IsNullOrWhiteSpace(windows.Executable) &&
            Path.GetExtension(windows.Executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (targetWasBlank || linux.Launcher is LauncherKind.Wine or LauncherKind.Proton)
                linux.Launcher = LauncherKind.Proton;
            linux.Executable = Fill(linux.Executable, windows.Executable);
            linux.Arguments = Fill(linux.Arguments, windows.Arguments);
            linux.WorkingDirectory = Fill(linux.WorkingDirectory, windows.WorkingDirectory);
            linux.ProcessName = Fill(linux.ProcessName, windows.ProcessName);
            linux.CompatibilityTool = Fill(linux.CompatibilityTool, "UMU-Proton");
            return new(true, "Configured the portable Windows executable through UMU-Proton. The default Proton build is downloaded and updated automatically on Linux; choose another installed or managed version if needed.");
        }

        return new(false, $"CLC cannot safely infer a Linux command for {windows.Launcher}. Choose Wine, Proton, Heroic, or a native executable and enter the verified target.");
    }

    private static bool IsTargetBlank(LinuxLaunchConfiguration launch) =>
        string.IsNullOrWhiteSpace(launch.SteamId) &&
        string.IsNullOrWhiteSpace(launch.HeroicGameId) &&
        string.IsNullOrWhiteSpace(launch.FlatpakAppId) &&
        string.IsNullOrWhiteSpace(launch.Executable) &&
        string.IsNullOrWhiteSpace(launch.Uri);

    private static string Fill(string destination, string source) =>
        string.IsNullOrWhiteSpace(destination) && !string.IsNullOrWhiteSpace(source)
            ? source
            : destination;

    private static string ConvertEmulatorArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return arguments;
        var converted = WindowsEmulatorFolderRegex().Replace(arguments.Replace('\\', '/'), "Emulators/Linux/");
        return WindowsCoreRegex().Replace(converted, ".so");
    }

    [GeneratedRegex("Emulators/Windows/", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsEmulatorFolderRegex();

    [GeneratedRegex("\\.dll(?=\\\"|\\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsCoreRegex();
}
