using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.Tests;

public sealed class UpdateFilesystemGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "CLC-UpdateGuardTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidatesOrdinaryContainedMaintenancePaths()
    {
        var staging = Path.Combine(_root, ".cartlaunch", "update-staging", "package");
        Directory.CreateDirectory(staging);

        UpdateFilesystemGuard.ValidateCartMaintenancePaths(_root, staging);
    }

    [Fact]
    public void RejectsPathOutsideCart()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetTempPath(), "CLC-outside-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() =>
            UpdateFilesystemGuard.ValidateCartMaintenancePaths(_root, outside));
    }

    [Fact]
    public void CleanupRemovesOnlyExpiredStagingDirectories()
    {
        var staging = Path.Combine(_root, ".cartlaunch", "update-staging");
        var expired = Path.Combine(staging, "expired");
        var recent = Path.Combine(staging, "recent");
        Directory.CreateDirectory(expired);
        Directory.CreateDirectory(recent);
        Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow - UpdateFilesystemGuard.StagingRetention - TimeSpan.FromDays(1));

        UpdateFilesystemGuard.CleanupAbandonedStaging(_root);

        Assert.False(Directory.Exists(expired));
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public void CleanupPreservesCurrentPackageEvenWhenExpired()
    {
        var current = Path.Combine(_root, ".cartlaunch", "update-staging", "current");
        Directory.CreateDirectory(current);
        Directory.SetLastWriteTimeUtc(current, DateTime.UtcNow - TimeSpan.FromDays(30));

        UpdateFilesystemGuard.CleanupAbandonedStaging(_root, current);

        Assert.True(Directory.Exists(current));
    }

    [Fact]
    public void RejectsLinkedMaintenancePathWhenSupported()
    {
        var outside = Path.Combine(_root, "outside");
        var cart = Path.Combine(_root, "cart");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(cart);
        var link = Path.Combine(cart, ".cartlaunch");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<InvalidDataException>(() =>
            UpdateFilesystemGuard.ValidateCartMaintenancePaths(cart, Path.Combine(link, "update-staging")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
