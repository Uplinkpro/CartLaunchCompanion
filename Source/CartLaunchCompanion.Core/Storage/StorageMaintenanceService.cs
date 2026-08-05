namespace CartLaunchCompanion.Core.Storage;

public sealed class StorageMaintenanceService
{
    public void EnsureDirectories(
        params string[] directories)
    {
        foreach (var directory in directories.Where(
                     value => !string.IsNullOrWhiteSpace(value)))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void TrimLogs(
        string logsFolder,
        int maximumFiles = 10,
        long maximumTotalBytes = 5 * 1024 * 1024)
    {
        if (!Directory.Exists(logsFolder))
            return;

        var files = new DirectoryInfo(logsFolder)
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        long retainedBytes = 0;

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var keep =
                index < maximumFiles &&
                retainedBytes + file.Length <= maximumTotalBytes;

            if (keep)
            {
                retainedBytes += file.Length;
                continue;
            }

            TryDelete(file.FullName);
        }
    }

    public void ClearDisposableCache(string cacheFolder)
    {
        if (!Directory.Exists(cacheFolder))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     cacheFolder,
                     "*",
                     SearchOption.AllDirectories))
        {
            TryDelete(file);
        }

        DeleteEmptyDirectories(cacheFolder);
    }

    public void TrimCache(
        string cacheFolder,
        TimeSpan? maximumAge = null,
        long maximumTotalBytes = 250L * 1024 * 1024)
    {
        if (!Directory.Exists(cacheFolder))
            return;

        var cutoff = DateTime.UtcNow - (maximumAge ?? TimeSpan.FromDays(30));
        var files = new DirectoryInfo(cacheFolder)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        long retainedBytes = 0;

        foreach (var file in files)
        {
            var keep = file.LastWriteTimeUtc >= cutoff &&
                       retainedBytes + file.Length <= maximumTotalBytes;
            if (keep)
                retainedBytes += file.Length;
            else
                TryDelete(file.FullName);
        }

        DeleteEmptyDirectories(cacheFolder);
    }

    private static void DeleteEmptyDirectories(string root)
    {
        foreach (var directory in Directory
                     .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                Directory.Delete(directory, recursive: false);
            }
            catch
            {
                // Cache cleanup is best effort.
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Storage maintenance must not block application startup.
        }
    }
}
