using CartLaunchCompanion.Core.Configuration;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CartLaunchCompanion.Configurator;

public enum LauncherDiscoveryValueKind
{
    SteamAppId,
    XboxAppId,
    EpicAppName,
    UbisoftGameId,
    LaunchUri,
    Executable
}

public sealed record InstalledLauncherMatch(
    string Name,
    LauncherKind Launcher,
    LauncherDiscoveryValueKind ValueKind,
    string Value,
    string Source,
    int MatchScore,
    string Arguments = "",
    string WorkingDirectory = "")
{
    public string Method => ValueKind switch
    {
        LauncherDiscoveryValueKind.SteamAppId => "Steam App ID",
        LauncherDiscoveryValueKind.XboxAppId => "Xbox AUMID",
        LauncherDiscoveryValueKind.EpicAppName => "Epic AppName",
        LauncherDiscoveryValueKind.UbisoftGameId => "Ubisoft game ID",
        LauncherDiscoveryValueKind.LaunchUri => "Launch URI",
        _ => "Executable"
    };

    public string Confidence => MatchScore >= 95 ? "Exact title" : MatchScore >= 75 ? "Strong match" : "Possible match";
}

public sealed partial class InstalledLauncherDiscoveryService
{
    public IReadOnlyList<InstalledLauncherMatch> Discover(string gameName, LauncherKind launcher)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(gameName)) return [];

        var results = new List<InstalledLauncherMatch>();
        if (launcher == LauncherKind.Steam) DiscoverSteam(gameName, results);
        if (launcher == LauncherKind.Epic) DiscoverEpic(gameName, results);
        if (launcher == LauncherKind.Xbox) DiscoverXbox(gameName, results);
        DiscoverShortcuts(gameName, launcher, results);

        return results
            .Where(match => match.MatchScore >= 45)
            .GroupBy(match => $"{match.ValueKind}|{match.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(match => match.MatchScore).First())
            .OrderByDescending(match => match.MatchScore)
            .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static void DiscoverSteam(string gameName, List<InstalledLauncherMatch> results)
    {
        foreach (var steamApps in FindSteamAppsFolders())
        {
            string[] manifests;
            try { manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var manifest in manifests)
            {
                try
                {
                    var text = File.ReadAllText(manifest);
                    var appId = AcfValue(text, "appid");
                    var name = AcfValue(text, "name");
                    if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name)) continue;
                    results.Add(new InstalledLauncherMatch(name, LauncherKind.Steam,
                        LauncherDiscoveryValueKind.SteamAppId, appId, manifest, Score(gameName, name)));
                }
                catch { }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FindSteamAppsFolders()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSteamRegistryRoot(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddSteamRegistryRoot(roots, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles)) roots.Add(Path.Combine(programFiles, "Steam"));

        foreach (var root in roots)
        {
            var primary = Path.Combine(root, "steamapps");
            if (Directory.Exists(primary)) yield return primary;
            var libraryFile = Path.Combine(primary, "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;
            string text;
            try { text = File.ReadAllText(libraryFile); }
            catch { continue; }
            foreach (Match match in VdfPathRegex().Matches(text))
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                var steamApps = Path.Combine(path, "steamapps");
                if (Directory.Exists(steamApps)) yield return steamApps;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddSteamRegistryRoot(HashSet<string> roots, RegistryKey hive, string keyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string value && Directory.Exists(value)) roots.Add(value.Replace('/', '\\'));
        }
        catch { }
    }

    private static string AcfValue(string text, string key)
    {
        var match = Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static void DiscoverEpic(string gameName, List<InstalledLauncherMatch> results)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var manifests = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;
        string[] files;
        try { files = Directory.GetFiles(manifests, "*.item", SearchOption.TopDirectoryOnly); }
        catch { return; }
        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var root = document.RootElement;
                var name = JsonString(root, "DisplayName");
                var appName = JsonString(root, "AppName");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appName)) continue;
                results.Add(new InstalledLauncherMatch(name, LauncherKind.Epic,
                    LauncherDiscoveryValueKind.EpicAppName, appName, file, Score(gameName, name)));
            }
            catch { }
        }
    }

    private static string JsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    [SupportedOSPlatform("windows")]
    private static void DiscoverXbox(string gameName, List<InstalledLauncherMatch> results)
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return;
            shell = Activator.CreateInstance(shellType);
            folder = Invoke(shell!, "NameSpace", BindingFlags.InvokeMethod, "shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
            if (folder is null) return;
            items = Invoke(folder, "Items", BindingFlags.InvokeMethod);
            var count = Convert.ToInt32(Invoke(items!, "Count", BindingFlags.GetProperty) ?? 0);
            for (var index = 0; index < count; index++)
            {
                object? item = null;
                try
                {
                    item = Invoke(items!, "Item", BindingFlags.InvokeMethod, index);
                    if (item is null) continue;
                    var name = Convert.ToString(Invoke(item, "Name", BindingFlags.GetProperty)) ?? "";
                    var aumid = Convert.ToString(Invoke(item, "Path", BindingFlags.GetProperty)) ?? "";
                    if (string.IsNullOrWhiteSpace(name) || !aumid.Contains('!')) continue;
                    results.Add(new InstalledLauncherMatch(name, LauncherKind.Xbox,
                        LauncherDiscoveryValueKind.XboxAppId, aumid, "Windows AppsFolder", Score(gameName, name)));
                }
                catch { }
                finally { ReleaseCom(item); }
            }
        }
        catch { }
        finally
        {
            ReleaseCom(items);
            ReleaseCom(folder);
            ReleaseCom(shell);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DiscoverShortcuts(string gameName, LauncherKind launcher, List<InstalledLauncherMatch> results)
    {
        foreach (var shortcutPath in ShortcutFiles())
        {
            ShortcutTarget? shortcut;
            try { shortcut = ReadShortcut(shortcutPath); }
            catch { continue; }
            if (shortcut is null) continue;
            var name = Path.GetFileNameWithoutExtension(shortcutPath);
            var combined = string.Join(' ', shortcut.Target, shortcut.Arguments).Trim();
            var kind = ClassifyShortcut(launcher, combined, shortcut.Target);
            if (kind is null) continue;

            var value = kind.Value switch
            {
                LauncherDiscoveryValueKind.SteamAppId => SteamIdRegex().Match(combined).Groups[1].Value,
                LauncherDiscoveryValueKind.UbisoftGameId => UbisoftIdRegex().Match(combined).Groups[1].Value,
                LauncherDiscoveryValueKind.EpicAppName => Uri.UnescapeDataString(EpicIdRegex().Match(combined).Groups[1].Value),
                LauncherDiscoveryValueKind.XboxAppId => XboxIdRegex().Match(combined).Groups[1].Value,
                LauncherDiscoveryValueKind.LaunchUri => ExtractUri(combined),
                _ => shortcut.Target
            };
            if (string.IsNullOrWhiteSpace(value)) continue;
            results.Add(new InstalledLauncherMatch(name, launcher, kind.Value, value, shortcutPath,
                Score(gameName, name), shortcut.Arguments, shortcut.WorkingDirectory));
        }
    }

    private static LauncherDiscoveryValueKind? ClassifyShortcut(LauncherKind launcher, string combined, string target)
    {
        if (launcher == LauncherKind.Steam && SteamIdRegex().IsMatch(combined)) return LauncherDiscoveryValueKind.SteamAppId;
        if (launcher == LauncherKind.Ubisoft && UbisoftIdRegex().IsMatch(combined)) return LauncherDiscoveryValueKind.UbisoftGameId;
        if (launcher == LauncherKind.Epic && EpicIdRegex().IsMatch(combined)) return LauncherDiscoveryValueKind.EpicAppName;
        if (launcher == LauncherKind.Xbox && XboxIdRegex().IsMatch(combined)) return LauncherDiscoveryValueKind.XboxAppId;
        if (HasLauncherUri(launcher, combined)) return LauncherDiscoveryValueKind.LaunchUri;
        if (File.Exists(target)) return LauncherDiscoveryValueKind.Executable;
        return null;
    }

    private static bool HasLauncherUri(LauncherKind launcher, string value)
    {
        var tokens = launcher switch
        {
            LauncherKind.Steam => ["steam://"],
            LauncherKind.Epic => ["com.epicgames.launcher://"],
            LauncherKind.GOG => ["goggalaxy://"],
            LauncherKind.Ubisoft => ["uplay://"],
            LauncherKind.Rockstar => ["rockstar:"],
            LauncherKind.Amazon => ["amazon-games:"],
            LauncherKind.EA => ["origin2://", "link2ea://", "ea://"],
            _ => Array.Empty<string>()
        };
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractUri(string value)
    {
        var match = AnyUriRegex().Match(value);
        return match.Success ? match.Value.TrimEnd('"', '\'', ',') : "";
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> ShortcutFiles()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            string[] files;
            try { files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var file in files.Where(file => file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                yield return file;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ShortcutTarget? ReadShortcut(string path)
    {
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            var url = File.ReadLines(path).FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
            return string.IsNullOrWhiteSpace(url) ? null : new ShortcutTarget(url, "", "");
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            shell = Activator.CreateInstance(shellType);
            shortcut = Invoke(shell!, "CreateShortcut", BindingFlags.InvokeMethod, path);
            if (shortcut is null) return null;
            return new ShortcutTarget(
                Convert.ToString(Invoke(shortcut, "TargetPath", BindingFlags.GetProperty)) ?? "",
                Convert.ToString(Invoke(shortcut, "Arguments", BindingFlags.GetProperty)) ?? "",
                Convert.ToString(Invoke(shortcut, "WorkingDirectory", BindingFlags.GetProperty)) ?? "");
        }
        finally
        {
            ReleaseCom(shortcut);
            ReleaseCom(shell);
        }
    }

    private static object? Invoke(object target, string member, BindingFlags flags, params object[] arguments) =>
        target.GetType().InvokeMember(member, flags, null, target, arguments);

    [SupportedOSPlatform("windows")]
    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    internal static int Score(string expected, string candidate)
    {
        var left = Normalize(expected);
        var right = Normalize(candidate);
        if (left.Length == 0 || right.Length == 0) return 0;
        if (left == right) return 100;
        if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal)) return 88;
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var intersection = leftTokens.Intersect(rightTokens).Count();
        if (intersection == 0) return 0;
        return (int)Math.Round(100d * intersection / Math.Max(leftTokens.Count, rightTokens.Count));
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

    private sealed record ShortcutTarget(string Target, string Arguments, string WorkingDirectory);

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
    [GeneratedRegex(@"steam://rungameid/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SteamIdRegex();
    [GeneratedRegex(@"uplay://launch/(\d+)(?:/\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex UbisoftIdRegex();
    [GeneratedRegex("com\\.epicgames\\.launcher://apps/([^?\\s\\\"]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EpicIdRegex();
    [GeneratedRegex("shell:AppsFolder\\\\([^\\s\\\"]+![^\\s\\\"]+)", RegexOptions.IgnoreCase)]
    private static partial Regex XboxIdRegex();
    [GeneratedRegex("(?:steam|uplay|com\\.epicgames\\.launcher|goggalaxy|origin2|link2ea|ea|amazon-games|rockstar):/{0,2}[^\\s\\\"]+", RegexOptions.IgnoreCase)]
    private static partial Regex AnyUriRegex();
}
