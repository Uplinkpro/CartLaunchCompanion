using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class SafeMediaEjectServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-EjectTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Eject_RevalidatesIdentityAndPassesOnlyTrackedRoot()
    {
        var identity = await CreateCartAsync("Expected cart");
        var platform = new FakePlatform();

        var result = await new SafeMediaEjectService(platform).EjectAsync(_root, identity.Identity.CartId);

        Assert.Equal(SafeMediaEjectOutcome.Ejected, result);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root)), platform.ReceivedRoot);
        Assert.Equal(1, platform.Calls);
    }

    [Fact]
    public async Task Eject_RejectsSubstitutedCartBeforePlatformOperation()
    {
        await CreateCartAsync("Substitute cart");
        var platform = new FakePlatform();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SafeMediaEjectService(platform).EjectAsync(_root, Guid.NewGuid().ToString()));

        Assert.Contains("no longer matches", error.Message);
        Assert.Equal(0, platform.Calls);
    }

    [Fact]
    public async Task Eject_PropagatesBusyOrFlushFailureWithoutClaimingSuccess()
    {
        var identity = await CreateCartAsync("Busy cart");
        var platform = new FakePlatform { Failure = new IOException("pending writes could not be flushed") };

        var error = await Assert.ThrowsAsync<IOException>(() =>
            new SafeMediaEjectService(platform).EjectAsync(_root, identity.Identity.CartId));

        Assert.Contains("pending writes", error.Message);
        Assert.Equal(1, platform.Calls);
    }

    [Fact]
    public async Task Eject_ReturnsAlreadyRemovedWhenCartDisappearsDuringPlatformOperation()
    {
        var identity = await CreateCartAsync("Vanishing cart");
        var platform = new FakePlatform
        {
            OnEject = root =>
            {
                Directory.Delete(root, recursive: true);
                throw new IOException("device vanished");
            }
        };

        var result = await new SafeMediaEjectService(platform).EjectAsync(_root, identity.Identity.CartId);

        Assert.Equal(SafeMediaEjectOutcome.AlreadyRemoved, result);
    }

    [Fact]
    public async Task Eject_ReturnsAlreadyRemovedWithoutCallingPlatformWhenRootIsGone()
    {
        var platform = new FakePlatform();
        var result = await new SafeMediaEjectService(platform).EjectAsync(_root, Guid.NewGuid().ToString());
        Assert.Equal(SafeMediaEjectOutcome.AlreadyRemoved, result);
        Assert.Equal(0, platform.Calls);
    }

    [Fact]
    public void LinuxResolver_MapsOnlyExactMountRootToBlockDevice()
    {
        string[] info =
        [
            "25 1 8:17 / /run/media/deck/CART rw - ext4 /dev/sdb1 rw",
            "26 1 8:33 / /run/media/deck/OTHER rw - ext4 /dev/sdc1 rw"
        ];
        Assert.Equal("/dev/sdb1", LinuxPhysicalMediaEjectPlatform.ResolveDevice("/run/media/deck/CART", info));
        Assert.Throws<IOException>(() => LinuxPhysicalMediaEjectPlatform.ResolveDevice("/run/media/deck/CAR", info));
    }

    private async Task<VerifiedCartIdentity> CreateCartAsync(string name)
    {
        Directory.CreateDirectory(_root);
        var identities = new CartIdentityService();
        return await identities.SaveNewAsync(_root, identities.Create(name));
    }

    private sealed class FakePlatform : IPhysicalMediaEjectPlatform
    {
        public int Calls { get; private set; }
        public string? ReceivedRoot { get; private set; }
        public Exception? Failure { get; init; }
        public Action<string>? OnEject { get; init; }
        public Task EjectVolumeAsync(string volumeRoot, CancellationToken cancellationToken)
        {
            Calls++; ReceivedRoot = volumeRoot;
            OnEject?.Invoke(volumeRoot);
            if (Failure is not null) throw Failure;
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
