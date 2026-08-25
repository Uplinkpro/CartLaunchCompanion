using System.Diagnostics;
using Avalonia.Media.Imaging;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Core.Updating;
using CartLaunchCompanion.Core.PhysicalCarts;
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
    private readonly IRuntimeUpdateService _updateService;
    private readonly Action _exitApplication;
    private readonly Action<bool> _setWindowVisible;
    private readonly Func<CancellationToken, Task> _prepareTrailerRuntime;
    private readonly string? _trustedCartId;

    private DateTimeOffset _lastInputAt = DateTimeOffset.MinValue;
    private LauncherAction _lastInputAction = LauncherAction.None;
    private bool _isLoadInProgress;
    private RuntimeUpdateAvailability? _availableUpdate;
    private CancellationTokenSource? _updateCancellation;
    private CancellationTokenSource? _metadataLoadingCancellation;
    private readonly List<GameCardViewModel> _allGameCards = [];
    private bool _metadataOpenedFromVersionPicker;
    private GameCardViewModel? _versionGroupRepresentative;

    public MainViewModel(
        IGameLibraryService libraryService,
        IGameLaunchService launchService,
        PortablePaths portablePaths,
        PlatformKind platform,
        Action exitApplication,
        Action<bool> setWindowVisible)
        : this(
            libraryService,
            launchService,
            portablePaths,
            platform,
            new UnavailableRuntimeUpdateService(),
            exitApplication,
            setWindowVisible)
    {
    }

    public MainViewModel(
        IGameLibraryService libraryService,
        IGameLaunchService launchService,
        PortablePaths portablePaths,
        PlatformKind platform,
        IRuntimeUpdateService updateService,
        Action exitApplication,
        Action<bool> setWindowVisible,
        Func<CancellationToken, Task>? prepareTrailerRuntime = null)
    {
        _libraryService = libraryService;
        _launchService = launchService;
        _portablePaths = portablePaths;
        _platform = platform;
        _updateService = updateService;
        _exitApplication = exitApplication;
        _setWindowVisible = setWindowVisible;
        _prepareTrailerRuntime = prepareTrailerRuntime ?? (_ => Task.CompletedTask);
        _trustedCartId = Environment.GetEnvironmentVariable("CLC_TRUSTED_CART_ID");

        ReloadCommand = new AsyncRelayCommand(LoadAsync);
        ExitCommand = new RelayCommand(OpenExitConfirmation);
        ConfirmExitCommand = new RelayCommand(_exitApplication);
        EjectCartCommand = new AsyncRelayCommand(EjectCartAsync, () => IsSafeEjectAvailable);
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

        SelectVersionCommand = new RelayCommand<GameCardViewModel>(game =>
        {
            if (game is not null) OpenVersionMetadata(game);
        });
        ConfirmSelectedVersionCommand = new RelayCommand(
            () =>
            {
                if (SelectedVersion is not null) OpenVersionMetadata(SelectedVersion);
            },
            () => SelectedVersion is not null);
        CloseVersionPickerCommand = new RelayCommand(CloseVersionPicker);

        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsUpdateBusy);
        OpenAvailableUpdateCommand = new RelayCommand(OpenAvailableUpdate, () => _availableUpdate is not null && !IsUpdateBusy);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => _availableUpdate is not null && !IsUpdateBusy);
        CloseUpdateCommand = new RelayCommand(CloseUpdate, () => !IsUpdateBusy);
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];
    public ObservableCollection<GameShelfViewModel> Shelves { get; } = [];
    public ObservableCollection<GameCardViewModel> VersionChoices { get; } = [];

    [ObservableProperty]
    public partial CollectionConfiguration Collection { get; set; } = new();

    [ObservableProperty]
    public partial Bitmap? CollectionLogoImage { get; set; }

    [ObservableProperty]
    public partial GameCardViewModel? SelectedGame { get; set; }

    [ObservableProperty]
    public partial GameCardViewModel? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Loading portable library…";

    [ObservableProperty]
    public partial string MetadataStatus { get; set; } = "";

    [ObservableProperty]
    public partial string LibraryErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLaunching { get; set; }

    [ObservableProperty]
    public partial bool IsHomeVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsMetadataVisible { get; set; }

    [ObservableProperty]
    public partial bool IsMetadataLoading { get; set; }

    [ObservableProperty]
    public partial GameCardViewModel? LoadingGame { get; set; }

    [ObservableProperty]
    public partial bool IsVersionPickerVisible { get; set; }

    [ObservableProperty]
    public partial bool IsExitVisible { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateVisible { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateBusy { get; set; }

    [ObservableProperty]
    public partial string UpdateTitle { get; set; } = "SOFTWARE UPDATE";

    [ObservableProperty]
    public partial string UpdateMessage { get; set; } = "Check for a newer signed release.";

    [ObservableProperty]
    public partial string UpdateActionText { get; set; } = "CHECK FOR UPDATES";

    [ObservableProperty]
    public partial double UpdateProgress { get; set; }

    public bool HasAvailableUpdate => _availableUpdate is not null;

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
    public bool HasCollectionLogo => CollectionLogoImage is not null;
    public bool HasNoCollectionLogo => CollectionLogoImage is null;
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

    public bool IsSafeEjectAvailable => !string.IsNullOrWhiteSpace(_trustedCartId);
    public string EjectStatus { get; private set; } = "";

    public IAsyncRelayCommand ReloadCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand ConfirmExitCommand { get; }
    public IAsyncRelayCommand EjectCartCommand { get; }
    public IRelayCommand CancelExitCommand { get; }
    public IRelayCommand ReturnHomeCommand { get; }
    public IAsyncRelayCommand ConfirmLaunchCommand { get; }
    public IRelayCommand TrailerCommand { get; }
    public IRelayCommand OpenSelectedGameCommand { get; }
    public IRelayCommand<GameCardViewModel> SelectVersionCommand { get; }
    public IRelayCommand ConfirmSelectedVersionCommand { get; }
    public IRelayCommand CloseVersionPickerCommand { get; }
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IRelayCommand OpenAvailableUpdateCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }
    public IRelayCommand CloseUpdateCommand { get; }

    partial void OnIsUpdateBusyChanged(bool value)
    {
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        OpenAvailableUpdateCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
        CloseUpdateCommand.NotifyCanExecuteChanged();
    }

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
        if (_isLoadInProgress)
            return;

        var loadingStartedAt = Stopwatch.GetTimestamp();
        _isLoadInProgress = true;
        IsLoading = true;
        StatusMessage = "Loading portable library…";
        LibraryErrorMessage = string.Empty;

        try
        {
            DisposeCards();
            _allGameCards.Clear();
            Games.Clear();
            Shelves.Clear();
            VersionChoices.Clear();
            SelectedGame = null;

            Collection = await CollectionConfigurationJson.LoadAsync(
                _portablePaths.Config);
            CollectionLogoImage?.Dispose();
            CollectionLogoImage = TryLoadCollectionLogo(Collection.Logo);

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
            var centralPlacements = Collection.Placements
                .SelectMany(item => GetPlacementKeys(item).Select(key => (Key: key, Placement: item)))
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Placement, StringComparer.OrdinalIgnoreCase);

            var orderedEntries = result.Games
                .OrderBy(entry => GetShelfOrder(entry, shelfOrder, centralPlacements))
                .ThenBy(entry => GetShelfName(entry, centralPlacements), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => GetGamePlacement(entry, centralPlacements).Order)
                .ThenBy(entry => GetGameSortName(entry), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in orderedEntries)
                _allGameCards.Add(new GameCardViewModel(entry, OpenGame));

            foreach (var group in _allGameCards.GroupBy(GetVersionGroupKey, StringComparer.OrdinalIgnoreCase))
            {
                var versions = group.ToArray();
                var representative = versions.FirstOrDefault(game => game.IsPrimaryVersion) ?? versions[0];
                representative.SetVersions(versions);
                Games.Add(representative);
            }

            foreach (var group in Games.GroupBy(
                         game => GetShelfName(game.Entry, centralPlacements),
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
            OnPropertyChanged(nameof(HasCollectionLogo));
            OnPropertyChanged(nameof(HasNoCollectionLogo));
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
            var remainingLoadingTime =
                TimeSpan.FromMilliseconds(700) -
                Stopwatch.GetElapsedTime(loadingStartedAt);
            if (remainingLoadingTime > TimeSpan.Zero)
                await Task.Delay(remainingLoadingTime);

            _isLoadInProgress = false;
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

        if (IsLaunching || IsMetadataLoading)
            return;

        if (IsUpdateVisible)
        {
            if (input.Action == LauncherAction.Back && !IsUpdateBusy)
                CloseUpdate();
            else if (input.Action == LauncherAction.Confirm && !IsUpdateBusy)
            {
                if (_availableUpdate is null)
                    await CheckForUpdatesAsync();
                else
                    await InstallUpdateAsync();
            }
            return;
        }

        if (IsExitVisible)
        {
            HandleExitInput(input.Action);
            return;
        }

        if (IsVersionPickerVisible)
        {
            HandleVersionPickerInput(input.Action);
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

        // A library without named shelves is still displayed as a wrapping grid.
        // Preserve useful vertical navigation by moving one visual row when there
        // is no neighboring shelf to target.
        if (targetShelfIndex == currentShelfIndex)
        {
            MoveSelection(direction * (HasCustomCollection ? 6 : 8));
            return;
        }

        var targetShelf = Shelves[targetShelfIndex];

        if (targetShelf.Games.Count > 0)
        {
            SelectedGame = targetShelf.Games[
                Math.Min(currentGameIndex, targetShelf.Games.Count - 1)];
        }
    }

    partial void OnSelectedVersionChanged(GameCardViewModel? value)
    {
        foreach (var version in VersionChoices)
            version.IsVersionSelected = ReferenceEquals(version, value);
        ConfirmSelectedVersionCommand.NotifyCanExecuteChanged();
    }

    private void HandleVersionPickerInput(LauncherAction action)
    {
        switch (action)
        {
            case LauncherAction.NavigateLeft:
                MoveVersionSelection(-1);
                break;
            case LauncherAction.NavigateRight:
                MoveVersionSelection(1);
                break;
            case LauncherAction.Confirm when SelectedVersion is not null:
                OpenVersionMetadata(SelectedVersion);
                break;
            case LauncherAction.Back:
                CloseVersionPicker();
                break;
        }
    }

    private void MoveVersionSelection(int delta)
    {
        if (VersionChoices.Count == 0) return;
        var index = SelectedVersion is null ? 0 : VersionChoices.IndexOf(SelectedVersion);
        SelectedVersion = VersionChoices[Math.Clamp(index + delta, 0, VersionChoices.Count - 1)];
    }

    private string GetShelfName(GameLibraryEntry entry, IReadOnlyDictionary<string, CollectionGamePlacementConfiguration> placements)
    {
        var configured = GetGamePlacement(entry, placements).Shelf?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var fallback = Collection.DefaultShelf?.Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "" : fallback;
    }

    private int GetShelfOrder(
        GameLibraryEntry entry,
        IReadOnlyDictionary<string, int> shelfOrder,
        IReadOnlyDictionary<string, CollectionGamePlacementConfiguration> placements)
    {
        var name = GetShelfName(entry, placements);
        return shelfOrder.TryGetValue(name, out var order)
            ? order
            : int.MaxValue;
    }

    private CollectionGamePlacementConfiguration GetGamePlacement(
        GameLibraryEntry entry,
        IReadOnlyDictionary<string, CollectionGamePlacementConfiguration> placements)
    {
        var gameId = GameIdentity.Resolve(entry.Configuration.Game);
        if (placements.TryGetValue("id:" + gameId, out var stablePlacement))
            return stablePlacement;

        var relative = NormalizeCollectionConfigurationPath(
            Path.GetRelativePath(_portablePaths.Root, entry.ConfigurationPath));
        return placements.TryGetValue("path:" + relative, out var placement)
            ? placement
            : new CollectionGamePlacementConfiguration();
    }

    private static string NormalizeCollectionConfigurationPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static IEnumerable<string> GetPlacementKeys(
        CollectionGamePlacementConfiguration placement)
    {
        if (!string.IsNullOrWhiteSpace(placement.GameId))
            yield return "id:" + placement.GameId.Trim();
        if (!string.IsNullOrWhiteSpace(placement.Configuration))
            yield return "path:" + NormalizeCollectionConfigurationPath(placement.Configuration);
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
            OpenGame(SelectedGame);
    }

    private void OpenGame(GameCardViewModel game)
    {
        if (!game.HasMultipleVersions)
        {
            _metadataOpenedFromVersionPicker = false;
            OpenMetadata(game);
            return;
        }

        VersionChoices.Clear();
        _versionGroupRepresentative = game;
        foreach (var version in game.Versions)
            VersionChoices.Add(version);
        SelectedVersion = VersionChoices.FirstOrDefault(version => version.IsPrimaryVersion) ?? VersionChoices.FirstOrDefault();
        IsHomeVisible = false;
        IsMetadataVisible = false;
        IsVersionPickerVisible = true;
    }

    private void OpenVersionMetadata(GameCardViewModel game)
    {
        _metadataOpenedFromVersionPicker = true;
        OpenMetadata(game);
    }

    private void OpenMetadata(GameCardViewModel game)
    {
        if (IsLaunching || IsMetadataLoading)
            return;

        _metadataLoadingCancellation?.Cancel();
        _metadataLoadingCancellation?.Dispose();
        var transition = new CancellationTokenSource();
        _metadataLoadingCancellation = transition;

        LoadingGame = game;
        IsMetadataLoading = true;
        IsExitVisible = false;
        IsHomeVisible = false;
        IsVersionPickerVisible = false;
        IsMetadataVisible = false;

        _ = CompleteMetadataTransitionAsync(game, transition);
    }

    private async Task CompleteMetadataTransitionAsync(
        GameCardViewModel game,
        CancellationTokenSource transition)
    {
        try
        {
            var minimumDisplay = Task.Delay(
                UseMotionEffects ? 180 : 30,
                transition.Token);
            if (game.HasTrailerSource)
            {
                await Task.WhenAll(
                    minimumDisplay,
                    _prepareTrailerRuntime(transition.Token));
            }
            else
            {
                await minimumDisplay;
            }

            SelectedGame = game;
            IsTrailerPlaybackEnabled = false;
            MetadataStatus = game.IsLaunchable
                ? string.Empty
                : "This game is not launchable on the current platform.";

            IsMetadataVisible = true;
            // Keep the loading layer above the page through its 240 ms entrance
            // animation. The native trailer remains hidden until the overlay is
            // gone and its surface has completed a real layout pass.
            await Task.Delay(UseMotionEffects ? 260 : 30, transition.Token);
            IsMetadataLoading = false;
            LoadingGame = null;
            await Task.Delay(16, transition.Token);
            IsTrailerPlaybackEnabled = true;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_metadataLoadingCancellation, transition))
            {
                IsMetadataLoading = false;
                LoadingGame = null;
                _metadataLoadingCancellation = null;
                transition.Dispose();
            }
        }
    }

    private void CloseVersionPicker()
    {
        _metadataOpenedFromVersionPicker = false;
        IsVersionPickerVisible = false;
        VersionChoices.Clear();
        SelectedVersion = null;
        if (_versionGroupRepresentative is not null)
            SelectedGame = _versionGroupRepresentative;
        _versionGroupRepresentative = null;
        IsHomeVisible = true;
    }

    private void ReturnHome()
    {
        if (IsLaunching)
            return;

        _metadataLoadingCancellation?.Cancel();

        IsTrailerPlaybackEnabled = false;
        IsExitVisible = false;
        IsMetadataVisible = false;
        if (_metadataOpenedFromVersionPicker && VersionChoices.Count > 1)
        {
            _metadataOpenedFromVersionPicker = false;
            IsVersionPickerVisible = true;
            IsHomeVisible = false;
            return;
        }

        IsVersionPickerVisible = false;
        VersionChoices.Clear();
        SelectedVersion = null;
        _versionGroupRepresentative = null;
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

    private async Task CheckForUpdatesAsync()
    {
        IsUpdateVisible = true;
        IsUpdateBusy = true;
        UpdateTitle = "CHECKING FOR UPDATES";
        UpdateMessage = "Contacting the official Cart Launch Companion release channel…";
        UpdateProgress = 0;
        _availableUpdate = null;
        OnPropertyChanged(nameof(HasAvailableUpdate));

        try
        {
            var platform = GetUpdatePlatform();
            var current = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0);
            _availableUpdate = await _updateService.CheckAsync(current, platform);
            if (_availableUpdate is null)
            {
                UpdateTitle = "YOU'RE UP TO DATE";
                UpdateMessage = $"Cart Launch Companion {current.ToString(3)} is the newest signed release.";
                UpdateActionText = "CHECK AGAIN";
            }
            else
            {
                UpdateTitle = $"VERSION {_availableUpdate.Version} AVAILABLE";
                UpdateMessage = $"A signed {FormatBytes(_availableUpdate.PayloadBytes)} update is ready. Your games, artwork, and configuration will not be changed.";
                UpdateActionText = "DOWNLOAD AND RESTART";
            }
            OnPropertyChanged(nameof(HasAvailableUpdate));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Update check failed: {ex}");
            UpdateTitle = "UPDATE CHECK UNAVAILABLE";
            UpdateMessage = "CLC could not reach or validate the official release channel. You can continue using the launcher normally.";
            UpdateActionText = "TRY AGAIN";
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    public Task CheckForUpdatesInteractivelyAsync() => CheckForUpdatesAsync();

    public async Task CheckForUpdatesSilentlyAsync()
    {
        if (IsUpdateBusy || _availableUpdate is not null)
            return;

        try
        {
            var current = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0);
            _availableUpdate = await _updateService.CheckAsync(current, GetUpdatePlatform());
            OnPropertyChanged(nameof(HasAvailableUpdate));
            OpenAvailableUpdateCommand.NotifyCanExecuteChanged();
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Silent update check unavailable: {ex.Message}");
        }
    }

    private void OpenAvailableUpdate()
    {
        if (_availableUpdate is null || IsUpdateBusy)
            return;

        UpdateTitle = $"VERSION {_availableUpdate.Version} AVAILABLE";
        UpdateMessage = $"A signed {FormatBytes(_availableUpdate.PayloadBytes)} update is ready. Your games, artwork, and configuration will not be changed.";
        UpdateActionText = "DOWNLOAD AND RESTART";
        UpdateProgress = 0;
        IsUpdateVisible = true;
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is null)
            return;

        IsUpdateBusy = true;
        UpdateTitle = "DOWNLOADING SIGNED UPDATE";
        UpdateMessage = "Keep the cart connected. CLC will verify every file before restarting.";
        _updateCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(value => UpdateProgress = value * 100);
            var prepared = await _updateService.DownloadAndPrepareAsync(
                _availableUpdate, _portablePaths.Root, GetUpdatePlatform(), progress, _updateCancellation.Token);
            StartMaintenanceUpdater(prepared);
            _exitApplication();
        }
        catch (OperationCanceledException)
        {
            UpdateTitle = "UPDATE CANCELLED";
            UpdateMessage = "No runtime files were changed.";
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Update preparation failed: {ex}");
            UpdateTitle = "UPDATE NOT INSTALLED";
            UpdateMessage = $"CLC rejected the update safely. {ex.Message}";
        }
        finally
        {
            _updateCancellation?.Dispose();
            _updateCancellation = null;
            IsUpdateBusy = false;
        }
    }

    private void StartMaintenanceUpdater(PreparedRuntimeUpdate prepared)
    {
        var executable = Path.Combine(
            _portablePaths.Maintenance,
            prepared.Platform,
            prepared.Platform == "Windows-x64" ? "CartLaunchCompanion.Updater.exe" : "CartLaunchCompanion.Updater");
        if (!File.Exists(executable))
            throw new FileNotFoundException("The cart maintenance updater is missing.", executable);

        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        };
        start.ArgumentList.Add("--cart-root");
        start.ArgumentList.Add(_portablePaths.Root);
        start.ArgumentList.Add("--platform");
        start.ArgumentList.Add(prepared.Platform);
        start.ArgumentList.Add("--staged-runtime");
        start.ArgumentList.Add(prepared.StagedRuntimeRoot);
        start.ArgumentList.Add("--manifest");
        start.ArgumentList.Add(prepared.ManifestPath);
        start.ArgumentList.Add("--wait-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--wait-timeout-seconds");
        start.ArgumentList.Add("60");
        _ = Process.Start(start) ??
            throw new InvalidOperationException("The maintenance updater did not start.");
    }

    private string GetUpdatePlatform() => _platform switch
    {
        PlatformKind.Windows when Environment.Is64BitOperatingSystem => "Windows-x64",
        PlatformKind.Linux when Environment.Is64BitOperatingSystem => "Linux-x64",
        _ => throw new PlatformNotSupportedException("Automatic updates require a 64-bit Windows or Linux system.")
    };

    private void CloseUpdate()
    {
        if (IsUpdateBusy)
            return;
        IsUpdateVisible = false;
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
            : $"{bytes / (1024d * 1024):0.0} MB";

    private void CancelExitConfirmation()
    {
        IsExitVisible = false;
    }

    private async Task EjectCartAsync()
    {
        if (_trustedCartId is null) return;
        try
        {
            EjectStatus = "Asking Cart Launch Host to safely remove this cart…";
            OnPropertyChanged(nameof(EjectStatus));
            var response = await CartHostEjectProtocol.RequestAsync(_trustedCartId);
            if (!response.Accepted)
            {
                EjectStatus = response.Message;
                OnPropertyChanged(nameof(EjectStatus));
                return;
            }
            _exitApplication();
        }
        catch (Exception ex)
        {
            EjectStatus = "Safe eject is unavailable: " + ex.Message;
            OnPropertyChanged(nameof(EjectStatus));
        }
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
        foreach (var game in _allGameCards)
            game.Dispose();
    }

    private static string GetVersionGroupKey(GameCardViewModel game) =>
        string.IsNullOrWhiteSpace(game.VersionGroup)
            ? "config:" + game.Entry.ConfigurationPath
            : "group:" + game.VersionGroup.Trim();

    private Bitmap? TryLoadCollectionLogo(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var normalized = configuredPath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(_portablePaths.Root, normalized);

        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Collection logo could not be loaded from '{path}': {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        DisposeCards();
        CollectionLogoImage?.Dispose();
        _metadataLoadingCancellation?.Cancel();
        _metadataLoadingCancellation?.Dispose();
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
    }

    private sealed class UnavailableRuntimeUpdateService : IRuntimeUpdateService
    {
        public Task<RuntimeUpdateAvailability?> CheckAsync(
            Version currentVersion,
            string platform,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RuntimeUpdateAvailability?>(null);

        public Task<PreparedRuntimeUpdate> DownloadAndPrepareAsync(
            RuntimeUpdateAvailability update,
            string cartRoot,
            string platform,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
