using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CartLaunchCompanion.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IGameLibraryService _libraryService;
    private readonly PortablePaths _portablePaths;
    private readonly PlatformKind _platform;
    private readonly Action _exitApplication;

    public MainViewModel(
        IGameLibraryService libraryService,
        PortablePaths portablePaths,
        PlatformKind platform,
        Action exitApplication)
    {
        _libraryService = libraryService;
        _portablePaths = portablePaths;
        _platform = platform;
        _exitApplication = exitApplication;

        ReloadCommand = new AsyncRelayCommand(LoadAsync);
        ExitCommand = new RelayCommand(_exitApplication);
        ReturnHomeCommand = new RelayCommand(ReturnHome);
        ConfirmLaunchCommand = new RelayCommand(ConfirmLaunch);
        TrailerCommand = new RelayCommand(ToggleTrailerPlaceholder);
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
    public IRelayCommand ConfirmLaunchCommand { get; }
    public IRelayCommand TrailerCommand { get; }

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
            OnPropertyChanged(nameof(HasSelectedGame));
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
        SelectedGame = game;
        IsTrailerPlaceholderActive = false;
        MetadataStatus = game.IsLaunchable
            ? "Ready to launch."
            : "This game is not launchable on the current platform.";

        IsHomeVisible = false;
        IsMetadataVisible = true;

        OnPropertyChanged(nameof(HasSelectedGame));
    }

    private void ReturnHome()
    {
        IsTrailerPlaceholderActive = false;
        IsMetadataVisible = false;
        IsHomeVisible = true;

        StatusMessage = HasGames
            ? $"{Games.Count} game{(Games.Count == 1 ? "" : "s")} loaded."
            : "No games found.";
    }

    private void ConfirmLaunch()
    {
        if (SelectedGame is null)
            return;

        MetadataStatus = SelectedGame.IsLaunchable
            ? "Launch handoff is prepared for Phase 5."
            : "This game cannot be launched on the current platform.";
    }

    private void ToggleTrailerPlaceholder()
    {
        if (SelectedGame is null)
            return;

        IsTrailerPlaceholderActive = !IsTrailerPlaceholderActive;

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
