using Microsoft.Win32;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Platform;
using System.Runtime.Versioning;

namespace CartLaunchCompanion.Core.Portable;

public sealed record HostLauncherDetectionResult(
    LauncherKind Launcher,
    bool Found,
    string Location,
    string Message);

public sealed class HostLauncherDetectionService
{
    public HostLauncherDetectionResult Detect(LauncherKind launcher, PlatformKind platform)
    {
        if (launcher is LauncherKind.Local or LauncherKind.Custom or LauncherKind.Flash or LauncherKind.Wine ||
            launcher == LauncherKind.Proton && platform != PlatformKind.Linux)
        {
            return new HostLauncherDetectionResult(
                launcher, true, "Cart-managed", "This launch method uses files selected from the cart.");
        }

        var locations = platform == PlatformKind.Windows && OperatingSystem.IsWindows()
            ? DetectWindows(launcher)
            : platform == PlatformKind.Linux && OperatingSystem.IsLinux()
                ? DetectLinux(launcher)
                : [];
        var location = locations.FirstOrDefault(Directory.Exists) ?? locations.FirstOrDefault(File.Exists);
        return location is null
            ? new HostLauncherDetectionResult(
                launcher, false, "", $"{DisplayName(launcher)} was not found. You can locate or install it later; game files must still remain on the cart.")
            : new HostLauncherDetectionResult(
                launcher, true, location, $"{DisplayName(launcher)} is available on this computer. CLC will use only game content stored on this cart.");
    }

    [SupportedOSPlatform("windows")]
    private static List<string> DetectWindows(LauncherKind launcher)
    {
        var results = new List<string>();
        if (launcher == LauncherKind.Steam)
        {
            AddRegistryValue(results, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistryValue(results, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");
        }

        var tokens = SearchTokens(launcher);
        if (tokens.Length == 0)
            return results;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var keyPath in new[]
                 {
                     @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                     @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                 })
        {
            try
            {
                using var root = hive.OpenSubKey(keyPath);
                if (root is null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var entry = root.OpenSubKey(name);
                    var display = entry?.GetValue("DisplayName") as string ?? "";
                    if (!tokens.Any(token => display.Contains(token, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var install = entry?.GetValue("InstallLocation") as string;
                    var icon = entry?.GetValue("DisplayIcon") as string;
                    if (!string.IsNullOrWhiteSpace(install)) results.Add(install.Trim('"'));
                    if (!string.IsNullOrWhiteSpace(icon)) results.Add(icon.Split(',', 2)[0].Trim('"'));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }
        return results;
    }

    private static List<string> DetectLinux(LauncherKind launcher)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return launcher switch
        {
            LauncherKind.Steam =>
            [
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam")
            ],
            LauncherKind.Heroic =>
            [
                Path.Combine(home, ".config", "heroic"),
                Path.Combine(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic")
            ],
            LauncherKind.Flatpak =>
            [
                Path.Combine(home, ".local", "share", "flatpak"),
                Path.Combine(Path.DirectorySeparatorChar.ToString(), "var", "lib", "flatpak")
            ],
            LauncherKind.Proton => FindLinuxCommands("umu-run", home),
            _ => []
        };
    }

    private static List<string> FindLinuxCommands(string command, string home)
    {
        var results = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, command))
            .Where(File.Exists)
            .ToList();
        foreach (var candidate in new[]
                 {
                     Path.Combine(home, ".local", "bin", command),
                     Path.Combine(home, ".local", "share", "umu", command)
                 })
        {
            if (File.Exists(candidate) && !results.Contains(candidate, StringComparer.Ordinal))
                results.Add(candidate);
        }
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryValue(List<string> results, RegistryKey hive, string keyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                results.Add(value.Replace('/', Path.DirectorySeparatorChar));
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
    }

    private static string[] SearchTokens(LauncherKind launcher) => launcher switch
    {
        LauncherKind.Steam => ["Steam"],
        LauncherKind.Xbox => ["Xbox", "Gaming Services"],
        LauncherKind.Epic => ["Epic Games Launcher"],
        LauncherKind.GOG => ["GOG GALAXY"],
        LauncherKind.Ubisoft => ["Ubisoft Connect", "Uplay"],
        LauncherKind.Rockstar => ["Rockstar Games Launcher"],
        LauncherKind.Amazon => ["Amazon Games"],
        LauncherKind.EA => ["EA app", "EA Desktop"],
        LauncherKind.BattleNet => ["Battle.net"],
        LauncherKind.HoYoverse => ["HoYoPlay", "HoYoverse"],
        LauncherKind.ItchIo => ["itch"],
        LauncherKind.Heroic => ["Heroic Games Launcher"],
        _ => []
    };

    private static string DisplayName(LauncherKind launcher) => launcher switch
    {
        LauncherKind.GOG => "GOG Galaxy",
        LauncherKind.Xbox => "Xbox / Microsoft Store",
        LauncherKind.EA => "EA app",
        LauncherKind.BattleNet => "Battle.net",
        LauncherKind.HoYoverse => "HoYoverse",
        LauncherKind.ItchIo => "itch.io",
        _ => launcher.ToString()
    };
}
