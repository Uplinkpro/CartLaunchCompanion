using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GameLibraryServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            "CLC-LibraryTests-" + Guid.NewGuid());

    [Fact]
    public async Task LoadAsync_LoadsVersion2AndIsolatesBrokenGame()
    {
        var paths = PortablePaths.FromRoot(_root);
        Directory.CreateDirectory(paths.Games);

        var validFolder = Path.Combine(paths.Games, "Portal 2");
        Directory.CreateDirectory(validFolder);
        Directory.CreateDirectory(
            Path.Combine(validFolder, "Artwork"));

        File.WriteAllText(
            Path.Combine(validFolder, "Artwork", "Cover.jpg"),
            "fake-image");

        var validConfiguration = new GameConfiguration
        {
            Game = { Name = "Portal 2" },
            Launch =
            {
                Windows =
                {
                    Launcher = LauncherKind.Steam,
                    SteamId = "620"
                },
                Linux =
                {
                    Launcher = LauncherKind.Steam,
                    SteamId = "620"
                }
            }
        };

        await GameConfigurationJson.SaveAsync(
            Path.Combine(validFolder, "game.json"),
            validConfiguration);

        var brokenFolder = Path.Combine(paths.Games, "Broken");
        Directory.CreateDirectory(brokenFolder);
        await File.WriteAllTextAsync(
            Path.Combine(brokenFolder, "game.json"),
            "{ not-json");

        var service = CreateService();

        var result = await service.LoadAsync(
            paths,
            PlatformKind.Windows);

        Assert.Single(result.Games);
        Assert.Single(result.Failures);
        Assert.Equal(
            "Portal 2",
            result.Games[0].Configuration.Game.Name);
        Assert.Equal(
            LauncherKind.Steam,
            result.Games[0].LaunchTarget?.Launcher);
    }

    [Fact]
    public async Task LoadAsync_RejectsConfigurationsWithoutCurrentFormat()
    {
        var paths = PortablePaths.FromRoot(_root);
        var folder = Path.Combine(paths.Games, "Unsupported");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(
            Path.Combine(folder, "Game.json"),
            """
            {
              "Name": "Unsupported Game",
              "Launcher": "Steam",
              "SteamID": "123"
            }
            """);

        var service = CreateService();

        var result = await service.LoadAsync(
            paths,
            PlatformKind.Windows);

        Assert.Empty(result.Games);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("formatVersion 2", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_IgnoresExamplesFolder()
    {
        var paths = PortablePaths.FromRoot(_root);
        var examples = Path.Combine(paths.Games, "Examples");
        Directory.CreateDirectory(examples);
        await File.WriteAllTextAsync(
            Path.Combine(examples, "game.json"),
            "{}");

        var service = CreateService();

        var result = await service.LoadAsync(
            paths,
            PlatformKind.Windows);

        Assert.Empty(result.Games);
        Assert.Empty(result.Failures);
    }

    private static GameLibraryService CreateService() =>
        new(
            new GameConfigurationValidator(),
            new GamePathResolver(),
            new LaunchTargetSelector());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
