using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class LauncherAssetCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-LauncherAssets-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetAvailableWindowsLaunchers_LoadsSupportedLauncherFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Launchers", "Steam"));
        Directory.CreateDirectory(Path.Combine(_root, "Launchers", "DirectExe"));
        Directory.CreateDirectory(Path.Combine(_root, "Launchers", "battlenet"));
        Directory.CreateDirectory(Path.Combine(_root, "Launchers", "Unknown Launcher"));

        Assert.Equal(
            [LauncherKind.Steam, LauncherKind.BattleNet, LauncherKind.Local, LauncherKind.Custom],
            LauncherAssetCatalog.GetAvailableWindowsLaunchers(_root));
    }

    [Theory]
    [InlineData(LauncherKind.Local, "DirectExe")]
    [InlineData(LauncherKind.BattleNet, "battlenet")]
    [InlineData(LauncherKind.HoYoverse, "Hoyoverse")]
    [InlineData(LauncherKind.ItchIo, "itchi")]
    public void FolderName_UsesAssetFolderConvention(LauncherKind launcher, string expected)
    {
        Assert.Equal(expected, LauncherAssetCatalog.FolderName(launcher));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
