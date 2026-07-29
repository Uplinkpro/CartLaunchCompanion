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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
