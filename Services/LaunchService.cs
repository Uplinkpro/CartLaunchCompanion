using System.Diagnostics;
using CartLaunchCompanion.Models;

namespace CartLaunchCompanion.Services;

public static class LaunchService
{
    public static void Launch(GameDefinition game)
    {
        var launcher = NormalizeLauncher(game.Launcher);
        var target = ResolveTarget(game, launcher);
        var workingDirectory = ResolveWorkingDirectory(game, target);

        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        };

        if (!string.IsNullOrWhiteSpace(game.Arguments) && !Uri.TryCreate(target, UriKind.Absolute, out _))
            startInfo.Arguments = game.Arguments;

        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException($"Windows did not accept the launch target: {target}");
    }

    private static string ResolveTarget(GameDefinition game, string launcher)
    {
        if (!string.IsNullOrWhiteSpace(game.LaunchUri))
            return game.LaunchUri;

        return launcher switch
        {
            "Steam" when !string.IsNullOrWhiteSpace(game.SteamId)
                => $"steam://rungameid/{game.SteamId}",

            "Epic" when !string.IsNullOrWhiteSpace(game.EpicAppName)
                => $"com.epicgames.launcher://apps/{Uri.EscapeDataString(game.EpicAppName)}?action=launch&silent=true",

            "GOG" when !string.IsNullOrWhiteSpace(game.GogGameId)
                => $"goggalaxy://launchGame/{Uri.EscapeDataString(game.GogGameId)}",

            "Xbox" when !string.IsNullOrWhiteSpace(game.XboxAppId)
                => $"shell:AppsFolder\\{game.XboxAppId}",

            "Rockstar" when !string.IsNullOrWhiteSpace(game.RockstarGameId)
                => $"rockstar://launch/game/{Uri.EscapeDataString(game.RockstarGameId)}",

            "Ubisoft" when !string.IsNullOrWhiteSpace(game.UbisoftGameId)
                => $"uplay://launch/{Uri.EscapeDataString(game.UbisoftGameId)}/0",

            "Flash" => ResolveFlashTarget(game),

            _ => ResolveExecutable(game)
        };
    }

    private static string ResolveFlashTarget(GameDefinition game)
    {
        if (!string.IsNullOrWhiteSpace(game.FlashPlayer))
            return ResolvePath(game.FolderPath, game.FlashPlayer);

        if (!string.IsNullOrWhiteSpace(game.Executable))
            return ResolvePath(game.FolderPath, game.Executable);

        throw new InvalidOperationException(
            "Flash cartridges require FlashPlayer, Executable, or LaunchUri. " +
            "For an SWF file, set FlashPlayer to a standalone projector/player and Arguments to the SWF path.");
    }

    private static string ResolveExecutable(GameDefinition game)
    {
        if (string.IsNullOrWhiteSpace(game.Executable))
            throw new InvalidOperationException("No launch target is configured for this cartridge.");

        var path = ResolvePath(game.FolderPath, game.Executable);
        if (!File.Exists(path))
            throw new FileNotFoundException("The configured executable was not found.", path);
        return path;
    }

    private static string ResolvePath(string folder, string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(folder, path);

    private static string ResolveWorkingDirectory(GameDefinition game, string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile)
            return game.FolderPath;

        return Path.GetDirectoryName(target) ?? game.FolderPath;
    }

    public static string NormalizeLauncher(string? launcher)
    {
        var value = (launcher ?? string.Empty).Trim();
        if (value.Equals("Direct", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("DirectExe", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Direct EXE", StringComparison.OrdinalIgnoreCase))
            return "DirectExe";
        if (value.Equals("Epic Games", StringComparison.OrdinalIgnoreCase)) return "Epic";
        if (value.Equals("GOG Galaxy", StringComparison.OrdinalIgnoreCase)) return "GOG";
        if (value.Equals("Amazon Games", StringComparison.OrdinalIgnoreCase)) return "Amazon";
        if (value.Equals("Xbox Game Pass", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Game Pass", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Microsoft Store", StringComparison.OrdinalIgnoreCase)) return "Xbox";
        if (value.Equals("Flash Player", StringComparison.OrdinalIgnoreCase)) return "Flash";
        if (value.Equals("Rockstar Games", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Rockstar Games Launcher", StringComparison.OrdinalIgnoreCase)) return "Rockstar";
        if (value.Equals("Ubisoft Connect", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Uplay", StringComparison.OrdinalIgnoreCase)) return "Ubisoft";
        return string.IsNullOrWhiteSpace(value) ? "DirectExe" : value;
    }
}
