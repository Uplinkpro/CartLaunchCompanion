namespace CartLaunchCompanion.Core.Portable;

/// <summary>
/// Defines the portable, user-facing source layout for console games that can have updates and DLC.
/// Emulator adapters translate this layout into each emulator's native installed-content layout.
/// </summary>
public static class GameContentLayout
{
    public const string UpdatesFolderName = "Updates";
    public const string DlcFolderName = "DLC";
    public const string GuideFileName = "ABOUT GAME CONTENT.txt";

    private static readonly HashSet<string> ManagedPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlayStation 3",
        "PlayStation 4"
    };

    public static bool UsesPerGameContentFolders(string platformFolder) =>
        ManagedPlatforms.Contains(platformFolder);

    public static string EnsureGame(string mediaRoot, string platformFolder, string gameFolder)
    {
        ValidateSegment(platformFolder, nameof(platformFolder));
        ValidateSegment(gameFolder, nameof(gameFolder));
        if (!UsesPerGameContentFolders(platformFolder))
            throw new NotSupportedException($"{platformFolder} does not have a researched update and DLC layout yet.");

        var gameRoot = Path.Combine(Path.GetFullPath(mediaRoot), "Roms", platformFolder, gameFolder);
        Directory.CreateDirectory(gameRoot);
        Directory.CreateDirectory(Path.Combine(gameRoot, UpdatesFolderName));
        Directory.CreateDirectory(Path.Combine(gameRoot, DlcFolderName));
        return gameRoot;
    }

    public static void PreparePlatform(string mediaRoot, string platformFolder)
    {
        ValidateSegment(platformFolder, nameof(platformFolder));
        if (!UsesPerGameContentFolders(platformFolder)) return;
        var platformRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetFullPath(mediaRoot), "Roms", platformFolder)).FullName;
        foreach (var game in new DirectoryInfo(platformRoot).EnumerateDirectories())
        {
            if (game.LinkTarget is not null || (game.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            Directory.CreateDirectory(Path.Combine(game.FullName, UpdatesFolderName));
            Directory.CreateDirectory(Path.Combine(game.FullName, DlcFolderName));
        }

        var guide = Path.Combine(platformRoot, GuideFileName);
        if (!File.Exists(guide))
            File.WriteAllText(guide,
                "Keep each game in its own folder. Put legally dumped update content in that game's Updates folder and DLC in its DLC folder.\n" +
                "Emulator Companion translates this portable source layout into the native layout required by the selected emulator.\n");
    }

    private static void ValidateSegment(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Folder names must be a single safe path segment.", parameter);
    }
}
