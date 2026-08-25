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
    [InlineData("Windows Games", "game.exe")]
    [InlineData("WindowsApps", "game.exe")]
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

    [Theory]
    [InlineData("SteamLibrary", "steamapps/common/Grand Theft Auto/gta.exe")]
    [InlineData("XboxGames", "Example/Content/game.exe")]
    [InlineData("Windows Games", "Example/Content/game.exe")]
    [InlineData("WindowsApps", "Publisher.Game_1.0.0.0_x64/game.exe")]
    public void ConvertsLauncherLibrariesWhenClcIsAtTheMediaRoot(string category, string relativeFile)
    {
        var portableRoot = Path.Combine(_root, "PortableRoot");
        var config = Path.Combine(portableRoot, "Games", "Example");
        var selected = Path.Combine(portableRoot, category, relativeFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.Combine(portableRoot, "System"));
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(Path.GetDirectoryName(selected)!);
        File.WriteAllText(selected, "test");

        var result = new CartContentPathConverter().Convert(config, selected);

        Assert.True(result.IsPortable);
        Assert.Equal(category, result.Category);
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
        Assert.True(CartContentPathConverter.IsGameContentCategory("windows games"));
        Assert.True(CartContentPathConverter.IsGameContentCategory("WINDOWSAPPS"));
        Assert.True(CartContentPathConverter.IsEmulatorCategory("Emulators"));
        Assert.True(CartContentPathConverter.IsRomCategory("Roms"));
        Assert.False(CartContentPathConverter.IsGameContentCategory("Emulators"));
    }

    [Fact]
    public void Drive_root_containment_does_not_add_a_second_separator()
    {
        if (!OperatingSystem.IsWindows()) return;

        var method = typeof(CartContentPathConverter).GetMethod(
            "IsContained",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = (bool)method.Invoke(null, [@"H:\", @"H:\SteamLibrary\steamapps\common\Game\game.exe"])!;

        Assert.True(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
