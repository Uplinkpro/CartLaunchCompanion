using CartLaunchCompanion.Core.Configuration;
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
        var library = new StubLibraryService();
        var viewModel = new MainViewModel(
            library,
            PortablePaths.FromRoot(Path.GetTempPath()),
            PlatformKind.Windows,
            () => { });

        await viewModel.LoadAsync();

        var game = Assert.Single(viewModel.Games);
        game.OpenCommand.Execute(null);

        Assert.False(viewModel.IsHomeVisible);
        Assert.True(viewModel.IsMetadataVisible);
        Assert.Same(game, viewModel.SelectedGame);
    }

    [Fact]
    public async Task ReturnHomeCommand_RestoresHome()
    {
        var viewModel = new MainViewModel(
            new StubLibraryService(),
            PortablePaths.FromRoot(Path.GetTempPath()),
            PlatformKind.Windows,
            () => { });

        await viewModel.LoadAsync();
        viewModel.Games[0].OpenCommand.Execute(null);
        viewModel.ReturnHomeCommand.Execute(null);

        Assert.True(viewModel.IsHomeVisible);
        Assert.False(viewModel.IsMetadataVisible);
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
