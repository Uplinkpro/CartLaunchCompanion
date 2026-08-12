using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartContentPathConverterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-paths-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Games", "Game.exe")]
    [InlineData("Emulators", "retroarch.exe")]
    [InlineData("Roms", "game.chd")]
    [InlineData("SteamLibrary", "game.exe")]
    [InlineData("steamapps", "appmanifest_10.acf")]
    [InlineData("XboxGames", "game.exe")]
    public void ConvertsMediaContentToConfigurationRelativePath(string category, string fileName)
    {
        var config = Path.Combine(_root, "Cart", "Games", "Example");
        var selected = Path.Combine(_root, category, "Example", fileName);
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(Path.GetDirectoryName(selected)!);
        File.WriteAllText(selected, "test");

        var result = new CartContentPathConverter().Convert(config, selected);

        Assert.True(result.IsPortable);
        Assert.Equal(category, result.Category);
        Assert.False(Path.IsPathRooted(result.ConfiguredPath));
        Assert.Equal(Path.GetRelativePath(config, selected).Replace('\\', '/'), result.ConfiguredPath);
    }

    [Fact]
    public void RejectsFileOutsideMediaRoot()
    {
        var config = Path.Combine(_root, "Cart", "Games", "Example");
        Directory.CreateDirectory(config);
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".exe");

        var result = new CartContentPathConverter().Convert(config, outside);

        Assert.False(result.IsPortable);
        Assert.Empty(result.ConfiguredPath);
    }

    [Fact]
    public void RejectsUnmanagedMediaFolder()
    {
        var config = Path.Combine(_root, "Cart", "Games", "Example");
        var selected = Path.Combine(_root, "Downloads", "unsafe.exe");
        Directory.CreateDirectory(config);

        var result = new CartContentPathConverter().Convert(config, selected);

        Assert.False(result.IsPortable);
    }

    [Fact]
    public void ClassifiesTargetSpecificFolders()
    {
        Assert.True(CartContentPathConverter.IsGameContentCategory("Games"));
        Assert.True(CartContentPathConverter.IsGameContentCategory("SteamLibrary"));
        Assert.True(CartContentPathConverter.IsEmulatorCategory("Emulators"));
        Assert.True(CartContentPathConverter.IsRomCategory("Roms"));
        Assert.False(CartContentPathConverter.IsGameContentCategory("Emulators"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
