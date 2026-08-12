namespace CartLaunchCompanion.Core.Updating;

internal static class UpdateFilesystemGuard
{
    internal static readonly TimeSpan StagingRetention = TimeSpan.FromDays(7);

    public static void ValidateCartMaintenancePaths(string cartRoot, params string[] paths)
    {
        var fullCartRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartRoot));
        RejectLink(fullCartRoot, "cart root");

        var maintenanceRoot = Path.Combine(fullCartRoot, ".cartlaunch");
        if (Directory.Exists(maintenanceRoot))
            RejectLink(maintenanceRoot, "maintenance folder");

        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!RuntimePathPolicy.IsContainedDirectory(fullCartRoot, fullPath))
                throw new InvalidDataException("Update paths must remain inside the cart.");
            RejectExistingPathChain(fullCartRoot, fullPath);
        }
    }

    public static void CleanupAbandonedStaging(string cartRoot, string? preservePath = null)
    {
        var stagingRoot = Path.Combine(Path.GetFullPath(cartRoot), ".cartlaunch", "update-staging");
        ValidateCartMaintenancePaths(cartRoot, stagingRoot);
        if (!Directory.Exists(stagingRoot))
            return;

        var cutoff = DateTime.UtcNow - StagingRetention;
        var preserved = preservePath is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(preservePath));
        foreach (var directory in new DirectoryInfo(stagingRoot).EnumerateDirectories())
        {
            RejectLink(directory.FullName, "update staging folder");
            if (preserved is not null && string.Equals(
                    Path.TrimEndingDirectorySeparator(directory.FullName),
                    preserved,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                continue;
            if (directory.LastWriteTimeUtc < cutoff)
                directory.Delete(recursive: true);
        }
    }

    private static void RejectExistingPathChain(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) || File.Exists(current))
                RejectLink(current, "update path");
        }
    }

    private static void RejectLink(string path, string description)
    {
        FileSystemInfo info = new FileInfo(path);
        if (!info.Exists)
            info = new DirectoryInfo(path);
        if (info.Exists && ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null))
            throw new InvalidDataException($"The {description} cannot be a symbolic link or reparse point.");
    }
}
