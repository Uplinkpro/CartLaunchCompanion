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
}
