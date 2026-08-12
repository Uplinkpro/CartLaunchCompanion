using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartHostInstanceLockTests
{
    [Fact]
    public async Task Lock_AllowsOnlyOneHostForSameUserAndName()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var first = CartHostInstanceLock.TryAcquire(suffix);
        Assert.NotNull(first);
        var secondAcquired = await Task.Run(() =>
        {
            using var second = CartHostInstanceLock.TryAcquire(suffix);
            return second is not null;
        });
        Assert.False(secondAcquired);
    }

    [Fact]
    public void Lock_CanBeReacquiredAfterOwnerExits()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using (var first = CartHostInstanceLock.TryAcquire(suffix)) Assert.NotNull(first);
        using var replacement = CartHostInstanceLock.TryAcquire(suffix);
        Assert.NotNull(replacement);
    }

    [Theory]
    [InlineData("bad suffix")]
    [InlineData("bad/slash")]
    public void Lock_RejectsUnsafeDiagnosticSuffix(string suffix) =>
        Assert.Throws<ArgumentException>(() => CartHostInstanceLock.TryAcquire(suffix));
}
