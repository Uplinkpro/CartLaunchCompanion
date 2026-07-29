using CartLaunchCompanion.Core.Input;
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

    private DateTimeOffset _lastInputAt = DateTimeOffset.MinValue;
    private LauncherAction _lastInputAction = LauncherAction.None;

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
        ExitCommand = new RelayCommand(OpenExitConfirmation);
        ConfirmExitCommand = new RelayCommand(_exitApplication);
        CancelExitCommand = new RelayCommand(CancelExitConfirmation);

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

        OpenSelectedGameCommand = new RelayCommand(
            OpenSelectedMetadata,
            () => SelectedGame is not null &&
                  !IsLaunching);
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
    public partial bool IsExitVisible { get; set; }

    [ObservableProperty]
    public partial bool IsTrailerPlaceholderActive { get; set; }

    [ObservableProperty]
    public partial InputDeviceKind LastInputDevice { get; set; } =
        InputDeviceKind.Keyboard;

    [ObservableProperty]
    public partial bool IsControllerConnected { get; set; }

    [ObservableProperty]
    public partial string ControllerStatus { get; set; } =
        "Controller service starting…";

    [ObservableProperty]
    public partial string LastControllerAction { get; set; } = "None";

    public bool HasGames => Games.Count > 0;
    public bool HasNoGames => !HasGames && !IsLoading;
    public bool HasSelectedGame => SelectedGame is not null;
    public string PortableRoot => _portablePaths.Root;
    public string PlatformName => _platform.ToString();

    public string ConfirmPrompt =>
        LastInputDevice is InputDeviceKind.Controller or InputDeviceKind.Remote
            ? "A"
            : "ENTER";

    public string BackPrompt =>
        LastInputDevice is InputDeviceKind.Controller or InputDeviceKind.Remote
            ? "B"
            : "ESC";

    public string TrailerPrompt =>
        LastInputDevice is InputDeviceKind.Controller or InputDeviceKind.Remote
            ? "X"
            : "X / SPACE";

    public IAsyncRelayCommand ReloadCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand ConfirmExitCommand { get; }
    public IRelayCommand CancelExitCommand { get; }
    public IRelayCommand ReturnHomeCommand { get; }
    public IAsyncRelayCommand ConfirmLaunchCommand { get; }
    public IRelayCommand TrailerCommand { get; }
    public IRelayCommand OpenSelectedGameCommand { get; }

    partial void OnSelectedGameChanged(
        GameCardViewModel? value)
    {
        ConfirmLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedGameCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedGame));
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        ConfirmLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedGameCommand.NotifyCanExecuteChanged();
        ReturnHomeCommand.NotifyCanExecuteChanged();
        TrailerCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastInputDeviceChanged(InputDeviceKind value)
    {
        OnPropertyChanged(nameof(ConfirmPrompt));
        OnPropertyChanged(nameof(BackPrompt));
        OnPropertyChanged(nameof(TrailerPrompt));
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

    public void UpdateControllerConnection(
        bool connected,
        string description)
    {
        IsControllerConnected = connected;
        ControllerStatus = description;

        if (connected)
            LastInputDevice = InputDeviceKind.Controller;
    }

    public void UpdateControllerDiagnostic(string diagnostic)
    {
        ControllerStatus = diagnostic;
    }

    public async Task HandleInputAsync(
        LauncherInputEvent input)
    {
        LastInputDevice = input.Device;

        if (input.Device == InputDeviceKind.Controller)
            LastControllerAction = input.Action.ToString();

        if (ShouldDebounce(input))
            return;

        _lastInputAction = input.Action;
        _lastInputAt = input.Timestamp;

        if (IsLaunching)
            return;

        if (IsExitVisible)
        {
            HandleExitInput(input.Action);
            return;
        }

        if (IsMetadataVisible)
        {
            await HandleMetadataInputAsync(input.Action);
            return;
        }

        if (IsHomeVisible)
            HandleHomeInput(input.Action);
    }

    private bool ShouldDebounce(LauncherInputEvent input)
    {
        if (input.Action is LauncherAction.None)
            return true;

        if (input.Action != _lastInputAction)
            return false;

        var elapsed = input.Timestamp - _lastInputAt;

        var minimumDelay =
            input.Action is
                LauncherAction.NavigateLeft or
                LauncherAction.NavigateRight or
                LauncherAction.NavigateUp or
                LauncherAction.NavigateDown
                ? TimeSpan.FromMilliseconds(115)
                : TimeSpan.FromMilliseconds(250);

        return elapsed < minimumDelay;
    }

    private void HandleHomeInput(LauncherAction action)
    {
        switch (action)
        {
            case LauncherAction.NavigateLeft:
                MoveSelection(-1);
                break;

            case LauncherAction.NavigateRight:
                MoveSelection(1);
                break;

            case LauncherAction.NavigateUp:
                MoveSelection(-GetRowStride());
                break;

            case LauncherAction.NavigateDown:
                MoveSelection(GetRowStride());
                break;

            case LauncherAction.Confirm:
                OpenSelectedMetadata();
                break;

            case LauncherAction.Back:
                OpenExitConfirmation();
                break;
        }
    }

    private async Task HandleMetadataInputAsync(
        LauncherAction action)
    {
        switch (action)
        {
            case LauncherAction.Confirm:
                await ConfirmLaunchAsync();
                break;

            case LauncherAction.Back:
                ReturnHome();
                break;

            case LauncherAction.Trailer:
                ToggleTrailerPlaceholder();
                break;
        }
    }

    private void HandleExitInput(LauncherAction action)
    {
        switch (action)
        {
            // B/Back opens Exit from Home and confirms it here.
            case LauncherAction.Back:
                _exitApplication();
                break;

            // A/Confirm cancels the Exit confirmation.
            case LauncherAction.Confirm:
                CancelExitConfirmation();
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (Games.Count == 0)
            return;

        var currentIndex = SelectedGame is null
            ? 0
            : Games.IndexOf(SelectedGame);

        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = Math.Clamp(
            currentIndex + delta,
            0,
            Games.Count - 1);

        SelectedGame = Games[nextIndex];
    }

    private int GetRowStride()
    {
        if (Games.Count <= 5)
            return Games.Count;

        return (int)Math.Ceiling(Games.Count / 2.0);
    }

    private void OpenSelectedMetadata()
    {
        if (SelectedGame is not null)
            OpenMetadata(SelectedGame);
    }

    private void OpenMetadata(GameCardViewModel game)
    {
        if (IsLaunching)
            return;

        SelectedGame = game;
        IsTrailerPlaceholderActive = false;
        IsExitVisible = false;

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
        IsExitVisible = false;
        IsMetadataVisible = false;
        IsHomeVisible = true;

        StatusMessage = HasGames
            ? $"{Games.Count} game{(Games.Count == 1 ? "" : "s")} loaded."
            : "No games found.";
    }

    private void OpenExitConfirmation()
    {
        if (IsLaunching)
            return;

        IsExitVisible = true;
    }

    private void CancelExitConfirmation()
    {
        IsExitVisible = false;
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
