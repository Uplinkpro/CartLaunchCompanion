using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Desktop.Themes;

namespace CartLaunchCompanion.Desktop.Tests;

public sealed class LauncherThemeCatalogTests
{
    [Theory]
    [InlineData(LauncherKind.Xbox, "#35A936")]
    [InlineData(LauncherKind.Steam, "#3E8BFF")]
    [InlineData(LauncherKind.GOG, "#A94FDC")]
    [InlineData(LauncherKind.Rockstar, "#E0A623")]
    public void Get_ReturnsExpectedLauncherAccent(
        LauncherKind launcher,
        string expected)
    {
        Assert.Equal(
            expected,
            LauncherThemeCatalog.Get(launcher).Accent);
    }

    [Fact]
    public void Get_CustomUsesCartLaunchCompanionPurple()
    {
        Assert.Equal(
            "#9D56E8",
            LauncherThemeCatalog.Get(
                LauncherKind.Custom).Accent);
    }
}
