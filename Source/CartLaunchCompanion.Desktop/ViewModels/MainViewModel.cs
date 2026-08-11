using System.Diagnostics;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Desktop.Controls;
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
            ToggleTrailerPlayback,
            () => !IsLaunching);

        OpenSelectedGameCommand = new RelayCommand(
            OpenSelectedMetadata,
            () => SelectedGame is not null &&
                  !IsLaunching);
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];
    public ObservableCollection<GameShelfViewModel> Shelves { get; } = [];

    [ObservableProperty]
    public partial CollectionConfiguration Collection { get; set; } = new();

    [ObservableProperty]
    public partial GameCardViewModel? SelectedGame { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Loading portable library…";

    [ObservableProperty]
    public partial string MetadataStatus { get; set; } = "";

    [ObservableProperty]
    public partial string LibraryErrorMessage { get; set; } = "";

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
    public partial bool IsTrailerPlaybackEnabled { get; set; } = true;

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
    public bool HasLibraryError => !string.IsNullOrWhiteSpace(LibraryErrorMessage);
    public bool ShowEmptyLibrary => HasNoGames && !HasLibraryError;
    public bool HasSelectedGame => SelectedGame is not null;
    public bool HasNoSelectedGame => SelectedGame is null;
    public bool HasCustomCollection =>
        Collection.Enabled && !string.IsNullOrWhiteSpace(Collection.Name);
    public bool ShowCartLaunchBranding =>
        !HasCustomCollection &&
        (SelectedGame is null || SelectedGame.UsesCartLaunchBranding);
    public bool ShowLauncherBranding =>
        !HasCustomCollection &&
        SelectedGame is not null && !SelectedGame.UsesCartLaunchBranding;
    public bool UseMotionEffects =>
        !AnimationPreferenceParser.IsReducedMotionValue(
            Environment.GetEnvironmentVariable("CLC_REDUCE_MOTION"));
    public bool ShouldPlayTrailer =>
        IsMetadataVisible && UseMotionEffects && IsTrailerPlaybackEnabled;
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

    public bool ShowOnScreenActionButtons =>
        LastInputDevice is not InputDeviceKind.Keyboard;

    public string ExitInstruction =>
        LastInputDevice switch
        {
            InputDeviceKind.Keyboard =>
                "Press Escape again to exit, or Enter to cancel.",
            InputDeviceKind.Mouse =>
                "Choose Exit to close the launcher, or Cancel to return.",
            InputDeviceKind.Remote =>
                "Press Back again to exit, or Confirm to cancel.",
            _ =>
                "Press B again to exit, or A to cancel."
        };

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
        foreach (var game in Games)
            game.IsSelected = ReferenceEquals(game, value);

        ConfirmLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedGameCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedGame));
        OnPropertyChanged(nameof(HasNoSelectedGame));
        OnPropertyChanged(nameof(ShowCartLaunchBranding));
        OnPropertyChanged(nameof(ShowLauncherBranding));
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        ConfirmLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedGameCommand.NotifyCanExecuteChanged();
        ReturnHomeCommand.NotifyCanExecuteChanged();
        TrailerCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMetadataVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldPlayTrailer));
    }

    partial void OnIsTrailerPlaybackEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldPlayTrailer));
    }

    partial void OnLastInputDeviceChanged(InputDeviceKind value)
    {
        OnPropertyChanged(nameof(ConfirmPrompt));
        OnPropertyChanged(nameof(BackPrompt));
        OnPropertyChanged(nameof(TrailerPrompt));
        OnPropertyChanged(nameof(ShowOnScreenActionButtons));
        OnPropertyChanged(nameof(ExitInstruction));
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        StatusMessage = "Loading portable library…";
        LibraryErrorMessage = string.Empty;

        try
        {
            DisposeCards();
            Games.Clear();
            Shelves.Clear();
            SelectedGame = null;

            Collection = await CollectionConfigurationJson.LoadAsync(
                _portablePaths.Config);

            var result = await _libraryService.LoadAsync(
                _portablePaths,
                _platform);

            var shelfOrder = Collection.Shelves
                .Where(shelf => !string.IsNullOrWhiteSpace(shelf.Name))
                .GroupBy(shelf => shelf.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Order,
                    StringComparer.OrdinalIgnoreCase);

            var orderedEntries = result.Games
                .OrderBy(entry => GetShelfOrder(entry, shelfOrder))
                .ThenBy(entry => GetShelfName(entry), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Configuration.Collection.Order)
                .ThenBy(entry => GetGameSortName(entry), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in orderedEntries)
            {
                Games.Add(
                    new GameCardViewModel(
                        entry,
                        OpenMetadata));
            }

            foreach (var group in Games.GroupBy(
                         game => GetShelfName(game.Entry),
                         StringComparer.OrdinalIgnoreCase))
            {
                Shelves.Add(new GameShelfViewModel(group.Key, group));
            }

            SelectedGame = Games.FirstOrDefault();
            StatusMessage = BuildStatusMessage(result);

            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(HasNoGames));
            OnPropertyChanged(nameof(HasLibraryError));
            OnPropertyChanged(nameof(ShowEmptyLibrary));
            OnPropertyChanged(nameof(HasCustomCollection));
            OnPropertyChanged(nameof(ShowCartLaunchBranding));
            OnPropertyChanged(nameof(ShowLauncherBranding));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Library load failed: {ex}");
            LibraryErrorMessage =
                $"The library could not be loaded. {ex.Message}";
            StatusMessage = LibraryErrorMessage;
            OnPropertyChanged(nameof(HasLibraryError));
            OnPropertyChanged(nameof(ShowEmptyLibrary));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoGames));
            OnPropertyChanged(nameof(ShowEmptyLibrary));
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
                MoveBetweenShelves(-1);
                break;

            case LauncherAction.NavigateDown:
                MoveBetweenShelves(1);
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
                ToggleTrailerPlayback();
                break;
        }
    }

    private void HandleExitInput(LauncherAction action)
    {
        switch (action)
        {
            case LauncherAction.Back:
                _exitApplication();
                break;

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

    private void MoveBetweenShelves(int direction)
    {
        if (SelectedGame is null || Shelves.Count == 0)
            return;

        var currentShelfIndex = -1;
        var currentGameIndex = 0;

        for (var shelfIndex = 0; shelfIndex < Shelves.Count; shelfIndex++)
        {
            var gameIndex = Shelves[shelfIndex].Games.IndexOf(SelectedGame);
            if (gameIndex < 0)
                continue;

            currentShelfIndex = shelfIndex;
            currentGameIndex = gameIndex;
            break;
        }

        if (currentShelfIndex < 0)
            return;

        var targetShelfIndex = Math.Clamp(
            currentShelfIndex + direction,
            0,
            Shelves.Count - 1);
        var targetShelf = Shelves[targetShelfIndex];

        if (targetShelf.Games.Count > 0)
        {
            SelectedGame = targetShelf.Games[
                Math.Min(currentGameIndex, targetShelf.Games.Count - 1)];
        }
    }

    private string GetShelfName(GameLibraryEntry entry)
    {
        var configured = entry.Configuration.Collection.Shelf?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var fallback = Collection.DefaultShelf?.Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "Library" : fallback;
    }

    private int GetShelfOrder(
        GameLibraryEntry entry,
        IReadOnlyDictionary<string, int> shelfOrder)
    {
        var name = GetShelfName(entry);
        return shelfOrder.TryGetValue(name, out var order)
            ? order
            : int.MaxValue;
    }

    private static string GetGameSortName(GameLibraryEntry entry)
    {
        var sortName = entry.Configuration.Game.SortName;
        return string.IsNullOrWhiteSpace(sortName)
            ? entry.Configuration.Game.Name
            : sortName;
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
        IsTrailerPlaybackEnabled = true;
        IsExitVisible = false;

        MetadataStatus = game.IsLaunchable
            ? string.Empty
            : "This game is not launchable on the current platform.";

        IsHomeVisible = false;
        IsMetadataVisible = true;
    }

    private void ReturnHome()
    {
        if (IsLaunching)
            return;

        IsTrailerPlaybackEnabled = false;
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
        IsTrailerPlaybackEnabled = false;
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
            Trace.WriteLine($"Launch monitoring cancelled for '{game.Name}'.");
            MetadataStatus = "Launch monitoring was cancelled.";
            _setWindowVisible(true);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Launch failed for '{game.Name}': {ex}");
            MetadataStatus =
                $"The launch failed: {ex.Message}";
            _setWindowVisible(true);
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private void ToggleTrailerPlayback()
    {
        if (SelectedGame is null ||
            IsLaunching)
        {
            return;
        }

        if (!SelectedGame.HasTrailerSource)
        {
            MetadataStatus = "No trailer is configured for this game.";
            return;
        }

        IsTrailerPlaybackEnabled = !IsTrailerPlaybackEnabled;
        MetadataStatus = IsTrailerPlaybackEnabled
            ? "Trailer playing."
            : "Trailer paused.";
    }

    private static string BuildStatusMessage(
        GameLibraryLoadResult result)
    {
        var loaded = result.Games.Count;
        var failed = result.Failures.Count;
        var message =
            $"{loaded} game{(loaded == 1 ? "" : "s")} loaded";

        if (failed > 0)
        {
            message +=
                $"; {failed} folder{(failed == 1 ? "" : "s")} could not be loaded";
        }

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
