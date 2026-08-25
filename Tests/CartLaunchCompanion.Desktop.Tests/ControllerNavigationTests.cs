using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Tests;

public sealed class ControllerNavigationTests : IDisposable
{
    private readonly List<string> _temporaryRoots = [];
    [Fact]
    public async Task Home_RightAndConfirm_SelectsAndOpensMetadata()
    {
        var viewModel = CreateViewModel(gameCount: 3);
        await viewModel.LoadAsync();

        var timestamp = DateTimeOffset.UtcNow;

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.NavigateRight,
                InputDeviceKind.Controller,
                timestamp));

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Confirm,
                InputDeviceKind.Controller,
                timestamp.AddMilliseconds(300)));

        Assert.True(viewModel.IsMetadataLoading);
        await Task.Delay(500);

        Assert.Equal("Game 2", viewModel.SelectedGame?.Name);
        Assert.True(viewModel.IsMetadataVisible);
        Assert.False(viewModel.IsHomeVisible);
        Assert.Equal("A", viewModel.ConfirmPrompt);
    }

    [Fact]
    public async Task Metadata_Back_ReturnsHome()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        var timestamp = DateTimeOffset.UtcNow;

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Confirm,
                InputDeviceKind.Controller,
                timestamp));

        await Task.Delay(500);

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Back,
                InputDeviceKind.Controller,
                timestamp.AddMilliseconds(300)));

        Assert.True(viewModel.IsHomeVisible);
        Assert.False(viewModel.IsMetadataVisible);
    }

    [Fact]
    public async Task Home_Back_OpensExitAndBackConfirmsExit()
    {
        var exitCalled = false;
        var viewModel = CreateViewModel(
            exitApplication: () => exitCalled = true);

        await viewModel.LoadAsync();
        var timestamp = DateTimeOffset.UtcNow;

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Back,
                InputDeviceKind.Controller,
                timestamp));

        Assert.True(viewModel.IsExitVisible);
        Assert.False(exitCalled);

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Back,
                InputDeviceKind.Controller,
                timestamp.AddMilliseconds(300)));

        Assert.True(exitCalled);
    }

    [Fact]
    public async Task ExitConfirmation_ConfirmCancels()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        var timestamp = DateTimeOffset.UtcNow;

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Back,
                InputDeviceKind.Controller,
                timestamp));

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Confirm,
                InputDeviceKind.Controller,
                timestamp.AddMilliseconds(300)));

        Assert.False(viewModel.IsExitVisible);
        Assert.True(viewModel.IsHomeVisible);
    }

    [Fact]
    public async Task RepeatedNavigationInsideDebounceWindow_IsIgnored()
    {
        var viewModel = CreateViewModel(gameCount: 3);
        await viewModel.LoadAsync();

        var timestamp = DateTimeOffset.UtcNow;

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.NavigateRight,
                InputDeviceKind.Controller,
                timestamp));

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.NavigateRight,
                InputDeviceKind.Controller,
                timestamp.AddMilliseconds(30)));

        Assert.Equal("Game 2", viewModel.SelectedGame?.Name);
    }

    [Fact]
    public async Task Home_Down_MovesToSameColumnOnNextShelf()
    {
        var viewModel = CreateViewModel(
            gameCount: 4,
            shelves: ["2D Era", "2D Era", "3D Era", "3D Era"]);
        await viewModel.LoadAsync();
        var timestamp = DateTimeOffset.UtcNow;

        await viewModel.HandleInputAsync(new LauncherInputEvent(
            LauncherAction.NavigateRight,
            InputDeviceKind.Controller,
            timestamp));
        await viewModel.HandleInputAsync(new LauncherInputEvent(
            LauncherAction.NavigateDown,
            InputDeviceKind.Controller,
            timestamp.AddMilliseconds(150)));

        Assert.Equal("Game 4", viewModel.SelectedGame?.Name);
        Assert.Equal(2, viewModel.Shelves.Count);
    }

    [Fact]
    public async Task Home_Down_InSingleWrappingShelf_MovesOneVisualRow()
    {
        var viewModel = CreateViewModel(gameCount: 10);
        await viewModel.LoadAsync();

        await viewModel.HandleInputAsync(new LauncherInputEvent(
            LauncherAction.NavigateDown,
            InputDeviceKind.Controller,
            DateTimeOffset.UtcNow));

        Assert.Equal("Game 9", viewModel.SelectedGame?.Name);
    }

    [Fact]
    public async Task Home_DefaultShelf_HasNoVisibleName()
    {
        var viewModel = CreateViewModel(gameCount: 2);
        await viewModel.LoadAsync();

        var shelf = Assert.Single(viewModel.Shelves);
        Assert.Equal("", shelf.Name);
        Assert.False(shelf.HasName);
    }

    [Fact]
    public async Task StableGameId_PreservesShelfWhenConfigurationPathChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "clc-stable-placement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        try
        {
            const string gameId = "game-stable-test";
            await CollectionConfigurationJson.SaveAsync(Path.Combine(root, "Config"), new CollectionConfiguration
            {
                Enabled = true,
                Name = "Collection",
                Placements =
                [
                    new CollectionGamePlacementConfiguration
                    {
                        GameId = gameId,
                        Configuration = "Games/Old Folder/game.json",
                        Shelf = "Stable Era",
                        Order = 10
                    }
                ]
            });

            var viewModel = CreateViewModel(
                portableRoot: root,
                gameIds: [gameId]);
            await viewModel.LoadAsync();

            Assert.Equal("Stable Era", Assert.Single(viewModel.Shelves).Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GroupedPlatformVersions_UseOneShelfCardAndOpenPicker()
    {
        var viewModel = CreateViewModel(
            gameCount: 3,
            versionGroups: ["same-game", "same-game", ""]);
        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Games.Count);
        Assert.Equal(2, viewModel.Games[0].Versions.Count);

        await viewModel.HandleInputAsync(new LauncherInputEvent(
            LauncherAction.Confirm,
            InputDeviceKind.Controller,
            DateTimeOffset.UtcNow));

        Assert.True(viewModel.IsVersionPickerVisible);
        Assert.False(viewModel.IsMetadataVisible);
        Assert.Equal(2, viewModel.VersionChoices.Count);
    }

    private MainViewModel CreateViewModel(
        int gameCount = 1,
        Action? exitApplication = null,
        string[]? shelves = null,
        string[]? versionGroups = null,
        string? portableRoot = null,
        string[]? gameIds = null)
    {
        var root = portableRoot ?? Path.Combine(
            Path.GetTempPath(),
            "CLC-ControllerTests-" + Guid.NewGuid().ToString("N"));
        if (portableRoot is null)
            _temporaryRoots.Add(root);

        if (shelves is not null)
        {
            var collection = new CollectionConfiguration
            {
                Enabled = true,
                Shelves = shelves.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select((name, index) => new CollectionShelfConfiguration
                    {
                        Name = name,
                        Order = index
                    }).ToList(),
                Placements = shelves.Select((shelf, index) =>
                    new CollectionGamePlacementConfiguration
                    {
                        Configuration = $"Games/Renamed Game {index + 1}/game.json",
                        Shelf = shelf,
                        Order = index + 1
                    }).ToList()
            };
            CollectionConfigurationJson.SaveAsync(
                Path.Combine(root, "Config"),
                collection).GetAwaiter().GetResult();
        }

        return new(
            new StubLibraryService(gameCount, versionGroups, root, gameIds),
            new StubLaunchService(),
            PortablePaths.FromRoot(root),
            PlatformKind.Windows,
            exitApplication ?? (() => { }),
            _ => { });
    }

    private sealed class StubLaunchService
        : IGameLaunchService
    {
        public Task<GameLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                GameLaunchResult.Success(
                    "Game started.",
                    CompletedGameLaunchSession.Instance));
    }

    private sealed class StubLibraryService(
        int gameCount,
        string[]? versionGroups = null,
        string? portableRoot = null,
        string[]? gameIds = null)
        : IGameLibraryService
    {
        public Task<GameLibraryLoadResult> LoadAsync(
            PortablePaths paths,
            PlatformKind platform,
            CancellationToken cancellationToken = default)
        {
            var result = new GameLibraryLoadResult();

            for (var index = 1; index <= gameCount; index++)
            {
                var configuration = new GameConfiguration
                {
                    Game =
                    {
                        Id = gameIds is not null && index <= gameIds.Length
                            ? gameIds[index - 1]
                            : "",
                        Name = $"Game {index}",
                        SortName = $"Game {index:D2}",
                        VersionGroup = versionGroups is not null && index <= versionGroups.Length
                            ? versionGroups[index - 1]
                            : "",
                        PlatformLabel = $"Platform {index}"
                    },
                    Launch =
                    {
                        Windows =
                        {
                            Launcher = LauncherKind.Steam,
                            SteamId = index.ToString()
                        },
                        Linux =
                        {
                            Launcher = LauncherKind.Steam,
                            SteamId = index.ToString()
                        }
                    }
                };

                result.Games.Add(
                    new GameLibraryEntry
                    {
                        FolderPath = Path.GetTempPath(),
                        ConfigurationPath =
                            Path.Combine(
                                portableRoot ?? Path.GetTempPath(),
                                "Games",
                                $"Renamed Game {index}",
                                "game.json"),
                        Configuration = configuration,
                        LaunchTarget = new LaunchTargetSelection(
                            PlatformKind.Windows,
                            LauncherKind.Steam,
                            true,
                            index.ToString(),
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "")
                    });
            }

            return Task.FromResult(result);
        }
    }

    public void Dispose()
    {
        foreach (var root in _temporaryRoots)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
