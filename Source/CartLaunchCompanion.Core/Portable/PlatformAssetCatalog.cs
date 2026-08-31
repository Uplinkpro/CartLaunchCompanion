namespace CartLaunchCompanion.Core.Portable;

public static class PlatformAssetCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sonyplaystation"] = "playstation",
        ["ps1"] = "playstation",
        ["psx"] = "playstation",
        ["ps2"] = "playstation2",
        ["ps3"] = "playstation3",
        ["ps4"] = "playstation4",
        ["ps5"] = "playstation5",
        ["psp"] = "playstationportable",
        ["sonypsp"] = "playstationportable",
        ["psvita"] = "playstationvita",
        ["vita"] = "playstationvita",
        ["gb"] = "gameboy",
        ["dmg"] = "gameboy",
        ["gbc"] = "gameboycolor",
        ["gba"] = "gameboyadvance",
        ["gc"] = "gamecube",
        ["gcn"] = "gamecube",
        ["snes"] = "supernintendo",
        ["supernintendoentertainmentsystem"] = "supernintendo",
        ["nintendoswitch"] = "switch",
        ["wiiu"] = "wiiu"
    };

    public static string? ResolveAsset(string assetsRoot, string platformLabel, string fileName)
    {
        if (string.IsNullOrWhiteSpace(assetsRoot) || string.IsNullOrWhiteSpace(platformLabel) ||
            string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(assetsRoot)) return null;
        var platformsRoot = Path.Combine(assetsRoot, "Platforms");
        if (!Directory.Exists(platformsRoot)) return null;

        var requested = Normalize(platformLabel);
        var canonical = Aliases.GetValueOrDefault(requested, requested);
        string[] directories;
        try { directories = Directory.GetDirectories(platformsRoot, "*", SearchOption.TopDirectoryOnly); }
        catch { return null; }

        var directory = directories.FirstOrDefault(path => Normalize(Path.GetFileName(path)) == requested)
            ?? directories.FirstOrDefault(path => Normalize(Path.GetFileName(path)) == canonical);
        if (directory is null) return null;
        var asset = Path.Combine(directory, Path.GetFileName(fileName));
        return File.Exists(asset) ? asset : null;
    }

    public static IReadOnlyList<string> GetAvailablePlatformNames(params string[] assetRoots)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetsRoot in assetRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var platformsRoot = Path.Combine(assetsRoot, "Platforms");
            if (!Directory.Exists(platformsRoot)) continue;
            string[] directories;
            try { directories = Directory.GetDirectories(platformsRoot, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var directory in directories)
                names.Add(Path.GetFileName(directory));
        }
        return names.ToArray();
    }

    public static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
