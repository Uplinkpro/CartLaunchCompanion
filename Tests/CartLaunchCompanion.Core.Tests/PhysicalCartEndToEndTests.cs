using CartLaunchCompanion.Core.PhysicalCarts;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PhysicalCartEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-EndToEnd-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreparedCart_CanBeReviewedTrustedStagedAndAuthorizedWithoutRunningFromMedia()
    {
        var source = CreatePortableCartSource();
        var media = Path.Combine(_root, "media");
        var package = await new CartPackageCreator().CreateAsync(new(source, media));
        var readiness = await new PhysicalCartReadinessService().PrepareAsync(media, "Integration Test Cart");

        Assert.True(readiness.IsReady);
        Assert.NotNull(readiness.Identity);
        Assert.Equal(Path.Combine(media, "Cart"), package.CartRoot);
        Assert.All(new[] { "Cart", "Games", "Emulators", "Roms" }, name =>
            Assert.True(Directory.Exists(Path.Combine(media, name))));

        var trustStore = new TrustedCartStore(Path.Combine(_root, "host-data", "trusted-carts.json"));
        await trustStore.TrustAsync(readiness.Identity!, approveAutoLaunch: false, readiness.RuntimeApprovals);
        var trusted = await trustStore.LoadAsync();
        var record = Assert.Single(trusted.Carts);
        Assert.False(record.AutoLaunchApproved);
        Assert.Equal(readiness.Identity!.Identity.CartId, record.CartId);

        var sessions = Path.Combine(_root, "host-data", "Sessions");
        var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
            media, readiness.Identity!, trusted, "Windows-x64", sessions);
        try
        {
            Assert.True(File.Exists(prepared.ExecutablePath));
            Assert.StartsWith(Path.GetFullPath(sessions) + Path.DirectorySeparatorChar,
                Path.GetFullPath(prepared.ExecutablePath), StringComparison.OrdinalIgnoreCase);
            Assert.False(Path.GetFullPath(prepared.ExecutablePath).StartsWith(
                Path.GetFullPath(media) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            await new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, trustStore);
        }
        finally { TrustedRuntimeStagingService.DeleteSession(prepared); }

        Assert.Empty(Directory.EnumerateFileSystemEntries(sessions));
    }

    [Fact]
    public async Task RuntimeChangedAfterTrust_IsRejectedBeforeLocalStaging()
    {
        var source = CreatePortableCartSource();
        var media = Path.Combine(_root, "tampered-media");
        await new CartPackageCreator().CreateAsync(new(source, media));
        var readiness = await new PhysicalCartReadinessService().PrepareAsync(media, "Tamper Test Cart");
        var trustStore = new TrustedCartStore(Path.Combine(_root, "tamper-host-data", "trusted-carts.json"));
        await trustStore.TrustAsync(readiness.Identity!, false, readiness.RuntimeApprovals);

        await File.AppendAllTextAsync(
            Path.Combine(media, "Cart", "System", "Windows-x64", "CartLaunchCompanion.Desktop.exe"),
            "changed after approval");

        var sessions = Path.Combine(_root, "tamper-host-data", "Sessions");
        var trusted = await trustStore.LoadAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new TrustedRuntimeStagingService().PrepareAsync(
            media, readiness.Identity!, trusted, "Windows-x64", sessions));
        Assert.False(Directory.Exists(sessions) && Directory.EnumerateFileSystemEntries(sessions).Any());
    }

    private string CreatePortableCartSource()
    {
        var source = Path.Combine(_root, "source-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(source, "System", "Windows-x64");
        Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(runtime, "CartLaunchCompanion.Desktop.exe"), "portable launcher runtime");
        File.WriteAllText(Path.Combine(runtime, "CartLaunchCompanion.Core.dll"), "portable dependency");
        Directory.CreateDirectory(Path.Combine(source, "Games", "Demo"));
        File.WriteAllText(Path.Combine(source, "Games", "Demo", "game.json"), "{}");
        return source;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
