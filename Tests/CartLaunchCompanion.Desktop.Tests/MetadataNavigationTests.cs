using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Tests;

public sealed class MetadataNavigationTests
{
    [Fact]
    public async Task OpenCommand_MovesFromHomeToMetadata()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        var game = Assert.Single(viewModel.Games);
        game.OpenCommand.Execute(null);

        Assert.True(viewModel.IsMetadataLoading);
        await Task.Delay(500);

        Assert.False(viewModel.IsHomeVisible);
        Assert.True(viewModel.IsMetadataVisible);
        Assert.Same(game, viewModel.SelectedGame);
    }

    [Fact]
    public async Task ReturnHomeCommand_RestoresHome()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();
        viewModel.Games[0].OpenCommand.Execute(null);
        viewModel.ReturnHomeCommand.Execute(null);

        Assert.True(viewModel.IsHomeVisible);
        Assert.False(viewModel.IsMetadataVisible);
    }

    [Fact]
    public async Task ConfirmLaunchCommand_UsesLaunchService()
    {
        var launchService = new StubLaunchService();
        var visibility = new List<bool>();

        var viewModel = CreateViewModel(
            launchService,
            visible => visibility.Add(visible));

        await viewModel.LoadAsync();
        viewModel.Games[0].OpenCommand.Execute(null);
        await Task.Delay(500);

        await viewModel.ConfirmLaunchCommand.ExecuteAsync(null);

        Assert.Equal(1, launchService.CallCount);
        Assert.Contains("started", viewModel.MetadataStatus);
        Assert.Empty(visibility);
    }

    [Fact]
    public async Task TrailerAction_TogglesActualPlaybackState()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        viewModel.Games[0].OpenCommand.Execute(null);
        await Task.Delay(500);

        Assert.True(viewModel.ShouldPlayTrailer);

        await viewModel.HandleInputAsync(new LauncherInputEvent(
            LauncherAction.Trailer,
            InputDeviceKind.Keyboard,
            DateTimeOffset.UtcNow));

        Assert.False(viewModel.ShouldPlayTrailer);
        Assert.Equal("Trailer paused.", viewModel.MetadataStatus);
    }

    private static MainViewModel CreateViewModel(
        IGameLaunchService? launchService = null,
        Action<bool>? setVisible = null)
        => new(
            new StubLibraryService(),
            launchService ?? new StubLaunchService(),
            PortablePaths.FromRoot(Path.GetTempPath()),
            PlatformKind.Windows,
            () => { },
            setVisible ?? (_ => { }));

    private sealed class StubLaunchService
        : IGameLaunchService
    {
        public int CallCount { get; private set; }

        public Task<GameLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            return Task.FromResult(
                GameLaunchResult.Success(
                    "Game started.",
                    CompletedGameLaunchSession.Instance));
        }
    }

    private sealed class StubLibraryService : IGameLibraryService
    {
        public Task<GameLibraryLoadResult> LoadAsync(
            PortablePaths paths,
            PlatformKind platform,
            CancellationToken cancellationToken = default)
        {
            var configuration = new GameConfiguration
            {
                Game =
                {
                    Name = "Portal 2",
                    Developer = "Valve",
                    Publisher = "Valve",
                    Genre = "Puzzle",
                    ReleaseDate = "2011-04-19"
                },
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

            var result = new GameLibraryLoadResult();
            result.Games.Add(
                new GameLibraryEntry
                {
                    FolderPath = Path.GetTempPath(),
                    ConfigurationPath =
                        Path.Combine(Path.GetTempPath(), "game.json"),
                    Configuration = configuration,
                    TrailerSource = "https://example.test/trailer.m3u8",
                    LaunchTarget = new LaunchTargetSelection(
                        PlatformKind.Windows,
                        LauncherKind.Steam,
                        true,
                        "620",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "")
                });

            return Task.FromResult(result);
        }
    }
}
