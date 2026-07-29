using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CartLaunchCompanion.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IGameLibraryService _libraryService;
    private readonly IGameLaunchService _launchService;
    private readonly PortablePaths _portablePaths;
    private readonly PlatformKind _platform;
    private readonly Action _exitApplication;
    private readonly Action<bool> _setWindowVisible;

    public MainViewModel(
        IGameLibraryService libraryService,
        IGameLaunchService launchService,
        PortablePaths portablePaths,
        PlatformKind platform,
        Action exitApplication,
        Action<bool> setWindowVisible)
    {
        _libraryService = libraryService;
        _launchService = launchService;
        _portablePaths = portablePaths;
        _platform = platform;
        _exitApplication = exitApplication;
        _setWindowVisible = setWindowVisible;

        ReloadCommand = new AsyncRelayCommand(LoadAsync);
        ExitCommand = new RelayCommand(_exitApplication);
        ReturnHomeCommand = new RelayCommand(
            ReturnHome,
            () => !IsLaunching);
        ConfirmLaunchCommand = new AsyncRelayCommand(
            ConfirmLaunchAsync,
            () => SelectedGame?.IsLaunchable == true &&
                  !IsLaunching);
        TrailerCommand = new RelayCommand(
            ToggleTrailerPlaceholder,
            () => !IsLaunching);
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    [ObservableProperty]
    public partial GameCardViewModel? SelectedGame { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Loading portable library…";

    [ObservableProperty]
    public partial string MetadataStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLaunching { get; set; }

    [ObservableProperty]
    public partial bool IsHomeVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsMetadataVisible { get; set; }

    [ObservableProperty]
    public partial bool IsTrailerPlaceholderActive { get; set; }

    public bool HasGames => Games.Count > 0;
    public bool HasNoGames => !HasGames && !IsLoading;
    public bool HasSelectedGame => SelectedGame is not null;
    public string PortableRoot => _portablePaths.Root;
    public string PlatformName => _platform.ToString();

    public IAsyncRelayCommand ReloadCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand ReturnHomeCommand { get; }
    public IAsyncRelayCommand ConfirmLaunchCommand { get; }
    public IRelayCommand TrailerCommand { get; }

    partial void OnSelectedGameChanged(
        GameCardViewModel? value)
    {
        ConfirmLaunchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedGame));
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        ConfirmLaunchCommand.NotifyCanExecuteChanged();
        ReturnHomeCommand.NotifyCanExecuteChanged();
        TrailerCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        StatusMessage = "Loading portable library…";

        try
        {
            DisposeCards();
            Games.Clear();
            SelectedGame = null;

            var result = await _libraryService.LoadAsync(
                _portablePaths,
                _platform);

            foreach (var entry in result.Games.Take(10))
            {
                Games.Add(
                    new GameCardViewModel(
                        entry,
                        OpenMetadata));
            }

            SelectedGame = Games.FirstOrDefault();
            StatusMessage = BuildStatusMessage(result);

            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(HasNoGames));
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"The library could not be loaded: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoGames));
        }
    }

    private void OpenMetadata(GameCardViewModel game)
    {
        if (IsLaunching)
            return;

        SelectedGame = game;
        IsTrailerPlaceholderActive = false;

        MetadataStatus = game.IsLaunchable
            ? "Ready to launch."
            : "This game is not launchable on the current platform.";

        IsHomeVisible = false;
        IsMetadataVisible = true;
    }

    private void ReturnHome()
    {
        if (IsLaunching)
            return;

        IsTrailerPlaceholderActive = false;
        IsMetadataVisible = false;
        IsHomeVisible = true;

        StatusMessage = HasGames
            ? $"{Games.Count} game{(Games.Count == 1 ? "" : "s")} loaded."
            : "No games found.";
    }

    private async Task ConfirmLaunchAsync()
    {
        var game = SelectedGame;

        if (game?.Entry.LaunchTarget is null ||
            !game.IsLaunchable ||
            IsLaunching)
        {
            MetadataStatus =
                "This game cannot be launched on the current platform.";
            return;
        }

        IsLaunching = true;
        IsTrailerPlaceholderActive = false;
        MetadataStatus = $"Launching {game.Name}…";

        var request = new GameLaunchRequest(
            game.Name,
            game.Entry.FolderPath,
            game.Entry.LaunchTarget,
            game.Entry.Configuration.Behavior);

        try
        {
            var result =
                await _launchService.LaunchAsync(request);

            MetadataStatus = result.Message;

            if (!result.Succeeded ||
                result.Session is null)
            {
                return;
            }

            await using var session = result.Session;

            var shouldHide =
                request.Behavior.HideWhileGameRuns &&
                session.CanMonitor;

            if (shouldHide)
                _setWindowVisible(false);

            if (session.CanMonitor)
                await session.WaitForExitAsync();

            if (shouldHide &&
                request.Behavior.RestoreLauncherAfterExit)
            {
                _setWindowVisible(true);
                ReturnHome();
            }
            else if (!session.CanMonitor)
            {
                MetadataStatus = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            MetadataStatus = "Launch monitoring was cancelled.";
            _setWindowVisible(true);
        }
        catch (Exception ex)
        {
            MetadataStatus =
                $"The launch failed: {ex.Message}";
            _setWindowVisible(true);
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private void ToggleTrailerPlaceholder()
    {
        if (SelectedGame is null ||
            IsLaunching)
        {
            return;
        }

        IsTrailerPlaceholderActive =
            !IsTrailerPlaceholderActive;

        MetadataStatus = SelectedGame.HasTrailer
            ? IsTrailerPlaceholderActive
                ? "Trailer playback placeholder active."
                : "Trailer paused."
            : "No local trailer is configured.";
    }

    private static string BuildStatusMessage(
        GameLibraryLoadResult result)
    {
        var loaded = result.Games.Count;
        var failed = result.Failures.Count;
        var limited = loaded > 10;
        var shown = Math.Min(loaded, 10);

        var message =
            $"{shown} game{(shown == 1 ? "" : "s")} loaded";

        if (failed > 0)
        {
            message +=
                $"; {failed} folder{(failed == 1 ? "" : "s")} could not be loaded";
        }

        if (limited)
            message += "; only the first 10 are shown";

        return message + ".";
    }

    private void DisposeCards()
    {
        foreach (var game in Games)
            game.Dispose();
    }

    public void Dispose()
    {
        DisposeCards();
    }
}
