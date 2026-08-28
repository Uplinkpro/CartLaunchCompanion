using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class TrustedCartBrandingServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-BrandingTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CachesConfiguredCollectionLogoInsideHostData()
    {
        var media = Path.Combine(_root, "Media");
        var cart = Path.Combine(media, "Cart");
        var config = Path.Combine(cart, "Config");
        var asset = Path.Combine(cart, "System", "Assets", "Collections", "Test", "Logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [1, 2, 3, 4]);
        await CollectionConfigurationJson.SaveAsync(config, new CollectionConfiguration
        {
            Enabled = true,
            Name = "Test Collection",
            Logo = "System/Assets/Collections/Test/Logo.png"
        });
        var cartId = Guid.NewGuid().ToString("D");
        var data = Path.Combine(_root, "HostData");
        var service = new TrustedCartBrandingService();

        var cached = await service.CacheCollectionLogoAsync(media, cartId, data);

        Assert.NotNull(cached);
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(cached!));
        Assert.Equal(cached, service.GetCachedLogoPath(data, cartId));
    }

    [Fact]
    public async Task RejectsCollectionLogoThatEscapesCartFolder()
    {
        var media = Path.Combine(_root, "Media");
        var config = Path.Combine(media, "Cart", "Config");
        await CollectionConfigurationJson.SaveAsync(config, new CollectionConfiguration
        {
            Enabled = true,
            Logo = "../outside.png"
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new TrustedCartBrandingService().CacheCollectionLogoAsync(
                media, Guid.NewGuid().ToString("D"), Path.Combine(_root, "HostData")));
    }

    [Fact]
    public async Task RemovesOnlyCachedBrandingForSelectedCart()
    {
        var media = Path.Combine(_root, "Media");
        var cart = Path.Combine(media, "Cart");
        var asset = Path.Combine(cart, "Logo.png");
        Directory.CreateDirectory(cart);
        await File.WriteAllBytesAsync(asset, [9, 8, 7]);
        await CollectionConfigurationJson.SaveAsync(Path.Combine(cart, "Config"), new CollectionConfiguration { Logo = "Logo.png" });
        var data = Path.Combine(_root, "HostData");
        var first = Guid.NewGuid().ToString("D");
        var second = Guid.NewGuid().ToString("D");
        var service = new TrustedCartBrandingService();
        await service.CacheCollectionLogoAsync(media, first, data);
        var retained = await service.CacheCollectionLogoAsync(media, second, data);

        service.RemoveCachedBranding(data, first);

        Assert.Null(service.GetCachedLogoPath(data, first));
        Assert.Equal(retained, service.GetCachedLogoPath(data, second));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
