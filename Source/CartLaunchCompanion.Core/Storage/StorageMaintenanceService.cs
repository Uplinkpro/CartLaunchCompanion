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

        foreach (var directory in Directory
                     .EnumerateDirectories(
                         cacheFolder,
                         "*",
                         SearchOption.AllDirectories)
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
