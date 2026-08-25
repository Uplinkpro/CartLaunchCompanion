namespace CartLaunchCompanion.Core.Portable;

public sealed record CartContentPathResult(
    bool IsPortable,
    string ConfiguredPath,
    string DisplayPath,
    string Category,
    string Message);

public sealed class CartContentPathConverter
{
    private static readonly string[] AllowedMediaFolders =
        ["Games", "Emulators", "Roms", "SteamLibrary", "steamapps", "XboxGames", "Windows Games", "WindowsApps"];

    private static readonly string[] GameContentFolders =
        ["Games", "SteamLibrary", "steamapps", "XboxGames", "Windows Games", "WindowsApps"];

    public static bool IsGameContentCategory(string category) =>
        GameContentFolders.Contains(category, StringComparer.OrdinalIgnoreCase);

    public static bool IsEmulatorCategory(string category) =>
        string.Equals(category, "Emulators", StringComparison.OrdinalIgnoreCase);

    public static bool IsRomCategory(string category) =>
        string.Equals(category, "Roms", StringComparison.OrdinalIgnoreCase);

    public CartContentPathResult Convert(string configurationFolder, string selectedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);

        var config = Path.GetFullPath(configurationFolder);
        var selected = Path.GetFullPath(selectedPath);
        var cart = FindCartFolder(config);
        if (cart is null)
        {
            return new CartContentPathResult(
                false, "", selected, "External",
                "Choose a configuration folder inside Cart/Games before locating portable content.");
        }

        // CLC supports both layouts:
        //   SSD/CartLaunchCompanion plus sibling launcher libraries, and
        //   CLC unpacked directly at the SSD root with launcher libraries inside it.
        // Prefer the CLC root when the selected file is beneath it so the first
        // relative segment remains SteamLibrary, XboxGames, Games, and so on.
        var mediaRoot = IsContained(cart, selected)
            ? cart
            : Directory.GetParent(cart)?.FullName;

        // Windows launcher libraries commonly have to live at the volume root
        // (for example H:\SteamLibrary and H:\XboxGames). If CLC was unpacked
        // into an extra nested folder, its immediate parent is not the media
        // root even though both paths are on the same portable SSD.
        if ((mediaRoot is null || !IsContained(mediaRoot, selected)) &&
            OperatingSystem.IsWindows())
        {
            var cartVolume = Path.GetPathRoot(cart);
            var selectedVolume = Path.GetPathRoot(selected);
            if (!string.IsNullOrWhiteSpace(cartVolume) &&
                string.Equals(cartVolume, selectedVolume, StringComparison.OrdinalIgnoreCase))
            {
                mediaRoot = selectedVolume;
            }
        }

        if (mediaRoot is null || !IsContained(mediaRoot, selected))
        {
            return new CartContentPathResult(
                false, "", selected, "External",
                $"This file is outside the detected cart media root ({mediaRoot ?? "unknown"}) and would stop working after reinsertion.");
        }

        var mediaRelative = Path.GetRelativePath(mediaRoot, selected).Replace('\\', '/');
        var category = mediaRelative.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var isInsideCart = string.Equals(category, Path.GetFileName(cart), Comparison);
        if (!isInsideCart && !AllowedMediaFolders.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            return new CartContentPathResult(
                false, "", mediaRelative, category,
                "Select files from Games, SteamLibrary, XboxGames, Windows Games, WindowsApps, Emulators, Roms, or Cart so the path remains portable.");
        }

        var configured = Path.GetRelativePath(config, selected).Replace('\\', '/');
        return new CartContentPathResult(
            true,
            configured,
            $"{category} / {string.Join(" / ", mediaRelative.Split('/').Skip(1))}",
            category,
            "Portable cart path. No drive letter or mount location will be saved.");
    }

    private static string? FindCartFolder(string start)
    {
        for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
        {
            if (string.Equals(current.Name, "Cart", Comparison) ||
                (Directory.Exists(Path.Combine(current.FullName, "Games")) &&
                 Directory.Exists(Path.Combine(current.FullName, "System"))))
            {
                return current.FullName;
            }
        }

        return null;
    }

    private static bool IsContained(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(normalizedRoot))
            normalizedRoot += Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, Comparison);
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
