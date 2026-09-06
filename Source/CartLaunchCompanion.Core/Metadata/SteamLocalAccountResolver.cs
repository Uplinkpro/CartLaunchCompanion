using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CartLaunchCompanion.Core.Metadata;

public static partial class SteamLocalAccountResolver
{
    public static string? ResolveMostRecentAccountId()
    {
        var environment = Environment.GetEnvironmentVariable("CLC_STEAM_ACCOUNT_ID")?.Trim();
        if (IsSteamId(environment))
            return environment;

        foreach (var path in CandidateLoginUserFiles())
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                var matches = LoginUserRegex().Matches(File.ReadAllText(path));
                var mostRecent = matches
                    .Cast<Match>()
                    .FirstOrDefault(match => MostRecentRegex().IsMatch(match.Groups["body"].Value));
                var selected = mostRecent ?? matches.Cast<Match>().FirstOrDefault();
                if (selected is not null)
                    return selected.Groups["id"].Value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked or unreadable Steam configuration should only hide
                // account-specific modules; it must never block the launcher.
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateLoginUserFiles()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            AddRegistryPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistryPath(roots, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFiles))
                roots.Add(Path.Combine(programFiles, "Steam"));
        }
        else if (OperatingSystem.IsLinux())
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userHome))
            {
                roots.Add(Path.Combine(userHome, ".steam", "steam"));
                roots.Add(Path.Combine(userHome, ".local", "share", "Steam"));
                roots.Add(Path.Combine(userHome, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam"));
            }
        }

        return roots.Select(root => Path.Combine(root, "config", "loginusers.vdf"));
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryPath(
        HashSet<string> roots,
        RegistryKey hive,
        string keyPath,
        string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                roots.Add(value.Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
        }
    }

    private static bool IsSteamId(string? value) =>
        value is { Length: 17 } && value.All(char.IsDigit) && value.StartsWith("7656", StringComparison.Ordinal);

    [GeneratedRegex("\\\"(?<id>7656\\d{13})\\\"\\s*\\{(?<body>.*?)\\r?\\n\\s*\\}", RegexOptions.Singleline)]
    private static partial Regex LoginUserRegex();

    [GeneratedRegex("\\\"MostRecent\\\"\\s+\\\"1\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex MostRecentRegex();
}
