using CartLaunchCompanion.Core.Library;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GamePathResolverTests
{
    [Fact]
    public void Resolve_NormalizesPortableForwardSlashes()
    {
        var resolver = new GamePathResolver();
        var folder = Path.Combine("C:", "Games", "Example");

        var resolved = resolver.Resolve(
            folder,
            "Artwork/Cover.jpg");

        Assert.EndsWith(
            Path.Combine("Artwork", "Cover.jpg"),
            resolved);
    }

    [Fact]
    public void Resolve_LeavesBlankPathBlank()
    {
        var resolver = new GamePathResolver();

        Assert.Equal("", resolver.Resolve("C:\\Game", ""));
    }

    [Fact]
    public void ResolveExistingWithAnyExtension_FindsMatchingArtworkStem()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"clc-path-{Guid.NewGuid():N}");
        var artwork = Path.Combine(root, "Artwork");
        Directory.CreateDirectory(artwork);
        var pngPath = Path.Combine(artwork, "Cover.png");
        File.WriteAllText(pngPath, "test");

        try
        {
            var resolver = new GamePathResolver();

            var resolved = resolver.ResolveExistingWithAnyExtension(
                root,
                "Artwork/Cover.jpg");

            Assert.Equal(pngPath, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
