using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Portable paths use forward slashes and preserve the existing CLC layout.</summary>
public static class EmulatorPathContract
{
    public const string RegistryRelativePath = "Config/emulator-registry.json";

    public static string ExecutablePath(PlatformKind platform, string folderName, string executablePath)
    {
        ValidateRelativePath(folderName);
        if (folderName.Contains('/'))
            throw new InvalidDataException("An emulator folder must be a single path segment.");
        ValidateRelativePath(executablePath);
        return $"Emulators/{PlatformFolder(platform)}/{folderName}/{executablePath}";
    }

    public static void ValidateExecutablePath(PlatformKind platform, string path)
    {
        ValidateRelativePath(path);
        if (!path.StartsWith($"Emulators/{PlatformFolder(platform)}/", StringComparison.Ordinal) ||
            path.Split('/').Length < 4)
            throw new InvalidDataException("Executable paths must use Emulators/<Windows|Linux>/<Emulator>/<Executable>.");
    }

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => c < 32 || "<>:\"\\|?*".Contains(c)))
            throw new InvalidDataException("Expected a portable relative path using forward slashes.");

        foreach (var segment in path.Split('/'))
        {
            var stem = segment.Split('.')[0];
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') ||
                segment.EndsWith('.') || IsReserved(stem))
                throw new InvalidDataException("The path contains an invalid portable segment.");
        }
    }

    // This resolves existing links defensively, but is not an installation/execution security boundary.
    // A future installer must also guard filesystem changes made after validation.
    public static string Resolve(string mediaRoot, string relativePath)
    {
        ValidateRelativePath(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaRoot);
        var current = Path.GetFullPath(mediaRoot);
        RejectLink(current);
        foreach (var segment in relativePath.Split('/'))
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
        return current;
    }

    private static string PlatformFolder(PlatformKind platform) => platform switch
    {
        PlatformKind.Windows => "Windows",
        PlatformKind.Linux => "Linux",
        _ => throw new InvalidDataException("Only Windows and Linux installations are supported.")
    };

    private static bool IsReserved(string stem) =>
        stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
        (stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
         (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
          stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));

    private static void RejectLink(string path)
    {
        FileSystemInfo info = new DirectoryInfo(path);
        if (!info.Exists && info.LinkTarget is null)
            info = new FileInfo(path);
        if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("Managed emulator paths cannot traverse symbolic links or reparse points.");
    }
}
