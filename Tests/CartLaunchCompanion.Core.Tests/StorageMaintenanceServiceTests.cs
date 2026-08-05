using CartLaunchCompanion.Core.Storage;

namespace CartLaunchCompanion.Core.Tests;

public sealed class StorageMaintenanceServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            "CLC-StorageTests-" + Guid.NewGuid());

    [Fact]
    public void TrimLogs_KeepsNewestFilesWithinLimit()
    {
        Directory.CreateDirectory(_root);

        for (var index = 0; index < 5; index++)
        {
            var path = Path.Combine(_root, $"{index}.log");
            File.WriteAllText(path, new string('x', 100));
            File.SetLastWriteTimeUtc(
                path,
                DateTime.UtcNow.AddMinutes(index));
        }

        var service = new StorageMaintenanceService();

        service.TrimLogs(
            _root,
            maximumFiles: 2,
            maximumTotalBytes: 1000);

        Assert.Equal(
            2,
            Directory.EnumerateFiles(_root).Count());
    }

    [Fact]
    public void TrimCache_RemovesExpiredFilesAndKeepsFreshFiles()
    {
        var cache = Path.Combine(_root, "Cache", "Metadata");
        Directory.CreateDirectory(cache);
        var expired = Path.Combine(cache, "expired.json");
        var fresh = Path.Combine(cache, "fresh.json");
        File.WriteAllText(expired, "old");
        File.WriteAllText(fresh, "new");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-40));

        new StorageMaintenanceService().TrimCache(
            Path.Combine(_root, "Cache"),
            maximumAge: TimeSpan.FromDays(30));

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(fresh));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
