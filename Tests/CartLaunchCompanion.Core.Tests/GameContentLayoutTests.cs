using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GameContentLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-content-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("PlayStation 3")]
    [InlineData("PlayStation 4")]
    public void EnsureGameCreatesPerGameUpdateAndDlcFolders(string platform)
    {
        var game = GameContentLayout.EnsureGame(_root, platform, "Example Game");

        Assert.True(Directory.Exists(Path.Combine(game, "Updates")));
        Assert.True(Directory.Exists(Path.Combine(game, "DLC")));
    }

    [Fact]
    public void PreparePlatformUpgradesExistingGameFoldersWithoutChangingTheirFiles()
    {
        var game = Path.Combine(_root, "Roms", "PlayStation 4", "CUSA00001");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "eboot.bin"), "keep");

        GameContentLayout.PreparePlatform(_root, "PlayStation 4");

        Assert.Equal("keep", File.ReadAllText(Path.Combine(game, "eboot.bin")));
        Assert.True(Directory.Exists(Path.Combine(game, "Updates")));
        Assert.True(Directory.Exists(Path.Combine(game, "DLC")));
        Assert.True(File.Exists(Path.Combine(_root, "Roms", "PlayStation 4", GameContentLayout.GuideFileName)));
    }

    [Fact]
    public void EnsureGameRejectsPathsAndUnresearchedPlatforms()
    {
        Assert.Throws<ArgumentException>(() => GameContentLayout.EnsureGame(_root, "PlayStation 4", "../escape"));
        Assert.Throws<NotSupportedException>(() => GameContentLayout.EnsureGame(_root, "PlayStation 2", "Game"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
