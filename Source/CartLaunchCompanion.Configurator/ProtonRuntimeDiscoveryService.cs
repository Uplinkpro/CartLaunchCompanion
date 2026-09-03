namespace CartLaunchCompanion.Configurator;

internal sealed record ProtonRuntimeInventory(
    bool UmuAvailable,
    string UmuLocation,
    IReadOnlyList<string> ProtonVersions)
{
    public string Summary => !OperatingSystem.IsLinux()
        ? "Installed Proton versions are detected when the configurator runs on Linux or Steam Deck. UMU-Proton and GE-Proton can still be selected for automatic installation on that system."
        : UmuAvailable
            ? $"UMU is ready at {UmuLocation}. Found {ProtonVersions.Count} installed Proton version{(ProtonVersions.Count == 1 ? "" : "s")}."
            : "UMU Launcher was not found. Install it once on this Linux system before launching portable Windows games.";
}

internal sealed class ProtonRuntimeDiscoveryService
{
    public ProtonRuntimeInventory Discover()
    {
        if (!OperatingSystem.IsLinux())
            return new(false, "", []);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var umu = FindCommand("umu-run", home);
        var roots = new[]
        {
            Path.Combine(home, ".local", "share", "Steam", "compatibilitytools.d"),
            Path.Combine(home, ".steam", "root", "compatibilitytools.d"),
            Path.Combine(home, ".steam", "steam", "compatibilitytools.d"),
            Path.Combine(home, ".local", "share", "Steam", "steamapps", "common"),
            Path.Combine(home, ".steam", "steam", "steamapps", "common"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "compatibilitytools.d"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "steamapps", "common"),
            "/usr/share/steam/compatibilitytools.d"
        };

        var versions = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root))
            .Where(IsProtonDirectory)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(!string.IsNullOrWhiteSpace(umu), umu ?? "", versions);
    }

    private static string? FindCommand(string command, string home)
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, command))
            .Append(Path.Combine(home, ".local", "bin", command))
            .Append(Path.Combine(home, ".local", "share", "umu", "umu-run"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsProtonDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return (name.Contains("Proton", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("proton", StringComparison.OrdinalIgnoreCase)) &&
               (File.Exists(Path.Combine(path, "proton")) ||
                File.Exists(Path.Combine(path, "toolmanifest.vdf")));
    }
}
