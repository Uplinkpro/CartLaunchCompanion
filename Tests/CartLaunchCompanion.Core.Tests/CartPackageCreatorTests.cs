using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartPackageCreatorTests
{
    [Fact]
    public async Task CreateAsync_BuildsCleanMediaLayoutAndExcludesDevelopmentFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "clc-package-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source"); var media = Path.Combine(root, "media");
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "System", "Windows-x64"));
            Directory.CreateDirectory(Path.Combine(source, "Assets", "Collections", "Demo", "Concepts"));
            Directory.CreateDirectory(Path.Combine(source, "Games", "Demo"));
            await File.WriteAllTextAsync(Path.Combine(source, "System", "Windows-x64", "CLC.exe"), "runtime");
            await File.WriteAllTextAsync(Path.Combine(source, "Games", "Demo", "game.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(source, "Assets", "Collections", "Demo", "Concepts", "draft.png"), "draft");
            await File.WriteAllTextAsync(Path.Combine(source, "source.cs"), "code");
            var result = await new CartPackageCreator().CreateAsync(new(source, media));
            Assert.True(File.Exists(Path.Combine(result.CartRoot, "System", "Windows-x64", "CLC.exe")));
            Assert.True(File.Exists(Path.Combine(result.CartRoot, "Games", "Demo", "game.json")));
            Assert.False(File.Exists(Path.Combine(result.CartRoot, "source.cs")));
            Assert.False(Directory.Exists(Path.Combine(result.CartRoot, "Assets", "Collections", "Demo", "Concepts")));
            Assert.True(Directory.Exists(Path.Combine(media, "Games")));
            Assert.True(Directory.Exists(Path.Combine(media, "Emulators")));
            Assert.True(Directory.Exists(Path.Combine(media, "Roms")));
            var identities = new CartIdentityService();
            var identity = await identities.SaveNewAsync(media, identities.Create("Test Cart"));
            Assert.Equal(identity.Identity.CartId, (await identities.LoadAsync(media)).Identity.CartId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CreateAsync_RefusesToOverwriteAnExistingCart()
    {
        var root = Path.Combine(Path.GetTempPath(), "clc-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "source"));
            Directory.CreateDirectory(Path.Combine(root, "media", "Cart"));
            await File.WriteAllTextAsync(Path.Combine(root, "source", "runtime.exe"), "x");
            await File.WriteAllTextAsync(Path.Combine(root, "media", "Cart", "keep.txt"), "x");
            await Assert.ThrowsAsync<InvalidOperationException>(() => new CartPackageCreator().CreateAsync(new(Path.Combine(root, "source"), Path.Combine(root, "media"))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
