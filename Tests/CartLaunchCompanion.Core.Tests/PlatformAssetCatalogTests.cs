using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PlatformAssetCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-PlatformAssets-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("PS2", "PlayStation 2")]
    [InlineData("PSP", "PlayStation Portable")]
    [InlineData("GBA", "Game Boy Advance")]
    [InlineData("SNES", "Super Nintendo")]
    [InlineData("Nintendo Switch", "Switch")]
    public void ResolveAsset_MatchesAliasesToReadableFolderNames(string label, string folder)
    {
        var platform = Path.Combine(_root, "Platforms", folder);
        Directory.CreateDirectory(platform);
        var banner = Path.Combine(platform, "Banner.png");
        File.WriteAllText(banner, "banner");

        Assert.Equal(banner, PlatformAssetCatalog.ResolveAsset(_root, label, "Banner.png"));
    }

    [Fact]
    public void GetAvailablePlatformNames_LoadsEveryPlatformFolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Platforms", "Empty"));
        var gameCube = Path.Combine(_root, "Platforms", "GameCube");
        Directory.CreateDirectory(gameCube);
        File.WriteAllText(Path.Combine(gameCube, "Logo.png"), "logo");

        Assert.Equal(["Empty", "GameCube"], PlatformAssetCatalog.GetAvailablePlatformNames(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
