using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Tests;

public sealed class ControllerNavigationTests
{
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

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Back,
                InputDeviceKind.Controller,
                timestamp.AddMilliseconds(300)));

        Assert.True(viewModel.IsHomeVisible);
        Assert.False(viewModel.IsMetadataVisible);
    }

    [Fact]
    public async Task Home_Back_OpensExitAndBackCancels()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        var timestamp = DateTimeOffset.UtcNow;

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Back,
                InputDeviceKind.Controller,
                timestamp));

        Assert.True(viewModel.IsExitVisible);

        await viewModel.HandleInputAsync(
            new LauncherInputEvent(
                LauncherAction.Back,
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

    private static MainViewModel CreateViewModel(
        int gameCount = 1)
        => new(
            new StubLibraryService(gameCount),
            new StubLaunchService(),
            PortablePaths.FromRoot(Path.GetTempPath()),
            PlatformKind.Windows,
            () => { },
            _ => { });

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

    private sealed class StubLibraryService(int gameCount)
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
                    Game = { Name = $"Game {index}" },
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
                                Path.GetTempPath(),
                                $"game-{index}.json"),
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
}
