using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class StorageItemPathResolverTests
{
    [Fact]
    public void Resolve_AcceptsFileUri()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CLC storage test"));
        var uri = new Uri(expected);

        Assert.Equal(expected, StorageItemPathResolver.Resolve(uri));
    }

    [Fact]
    public void Resolve_AcceptsRelativeLocalPath()
    {
        var relative = Path.Combine("test-media", "cart");

        Assert.Equal(Path.GetFullPath(relative), StorageItemPathResolver.Resolve(new Uri(relative, UriKind.Relative)));
    }

    [Fact]
    public void Resolve_AcceptsWindowsDriveRootReturnedAsRelativeUri()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(@"H:\", StorageItemPathResolver.Resolve(new Uri(@"H:\", UriKind.Relative)));
    }

    [Fact]
    public void Resolve_RejectsNonFileUri()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            StorageItemPathResolver.Resolve(new Uri("https://example.com/cart")));

        Assert.Contains("not a local file-system path", error.Message);
    }
}
