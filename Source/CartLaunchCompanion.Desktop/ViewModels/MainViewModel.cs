using System.Diagnostics;
using System.Net.Http;
using Avalonia.Media.Imaging;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Core.Updating;
using CartLaunchCompanion.Core.PhysicalCarts;
using CartLaunchCompanion.Core.Metadata;
using CartLaunchCompanion.Core.Tracking;
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
    private readonly RetroAchievementsClient? _retroAchievementsClient;
    private readonly HttpClient? _metadataHttpClient;
    private readonly SteamPlayerStatsClient? _steamPlayerStatsClient;
    private readonly ExophaseClient? _exophaseClient;
    private readonly GamePlaytimeStore _playtimeStore;
    private readonly string _retroAchievementsCacheDirectory;
    private readonly string _exophaseCacheDirectory;

    private DateTimeOffset _lastInputAt = DateTimeOffset.MinValue;
    private LauncherAction _lastInputAction = LauncherAction.None;
    private bool _isLoadInProgress;
    private RuntimeUpdateAvailability? _availableUpdate;
    private CancellationTokenSource? _updateCancellation;
    private CancellationTokenSource? _metadataLoadingCancellation;
    private CancellationTokenSource? _retroAchievementsCancellation;
    private CancellationTokenSource? _steamStatsCancellation;
    private CancellationTokenSource? _exophaseCancellation;
    private CancellationTokenSource? _playtimeCancellation;
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
        Func<CancellationToken, Task>? prepareTrailerRuntime = null,
        RetroAchievementsClient? retroAchievementsClient = null,
        HttpClient? metadataHttpClient = null,
        GamePlaytimeStore? playtimeStore = null,
        ExophaseClient? exophaseClient = null)
    {
        _libraryService = libraryService;
        _launchService = launchService;
        _portablePaths = portablePaths;
        _platform = platform;
        _updateService = updateService;
        _exitApplication = exitApplication;
        _setWindowVisible = setWindowVisible;
        _prepareTrailerRuntime = prepareTrailerRuntime ?? (_ => Task.CompletedTask);
        _retroAchievementsClient = retroAchievementsClient;
        _metadataHttpClient = metadataHttpClient;
        _steamPlayerStatsClient = metadataHttpClient is null
            ? null
            : new SteamPlayerStatsClient(metadataHttpClient);
        _exophaseClient = exophaseClient;
        _playtimeStore = playtimeStore ?? new GamePlaytimeStore(
            Path.Combine(_portablePaths.Config, "playtime.json"));
        _retroAchievementsCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartLaunchCompanion", "Cache", "RetroAchievements");
        _exophaseCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartLaunchCompanion", "Cache", "Exophase");
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
    public ObservableCollection<RetroAchievementItemViewModel> RetroAchievementsRecent { get; } = [];
    public ObservableCollection<MetadataModuleViewModel> MetadataModules { get; } = [];

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
    public partial bool IsLaunchTransitionVisible { get; set; }

    [ObservableProperty]
    public partial GameCardViewModel? LaunchingGame { get; set; }

    [ObservableProperty]
    public partial bool IsHomeVisible { get; set; } = true;

    [ObservableProperty]
    public partial int ActivePageIndex { get; set; }

    [ObservableProperty]
    public partial bool IsMetadataVisible { get; set; }

    [ObservableProperty]
    public partial bool IsMetadataLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAchievementData))]
    public partial bool IsRetroAchievementsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAchievementData))]
    public partial bool IsSteamAchievementsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAchievementData))]
    public partial bool IsExophaseAchievementsVisible { get; set; }

    [ObservableProperty]
    public partial string SteamAchievementsProgress { get; set; } = "";

    [ObservableProperty]
    public partial double SteamAchievementsPercent { get; set; }

    [ObservableProperty]
    public partial string ExophaseAchievementsProgress { get; set; } = "";

    [ObservableProperty]
    public partial double ExophaseAchievementsPercent { get; set; }

    public bool HasAchievementData =>
        IsRetroAchievementsVisible || IsSteamAchievementsVisible || IsExophaseAchievementsVisible;

    [ObservableProperty]
    public partial bool IsRetroAchievementsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasRetroAchievementsRecent { get; set; }

    [ObservableProperty]
    public partial string RetroAchievementsStatus { get; set; } = "";

    [ObservableProperty]
    public partial string RetroAchievementsProgress { get; set; } = "";

    [ObservableProperty]
    public partial string RetroAchievementsPoints { get; set; } = "";

    [ObservableProperty]
    public partial string RetroAchievementsAward { get; set; } = "";

    [ObservableProperty]
    public partial double RetroAchievementsPercent { get; set; }

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
                IsSafeEjectAvailable
                    ? "Press E to safely eject, Escape again to exit, or Enter to cancel."
                    : "Press Escape again to exit, or Enter to cancel.",
            InputDeviceKind.Mouse =>
                IsSafeEjectAvailable
                    ? "Choose Eject Cart for safe removal, Exit to close, or Cancel to return."
                    : "Choose Exit to close the launcher, or Cancel to return.",
            InputDeviceKind.Remote =>
                IsSafeEjectAvailable
                    ? "Choose Eject Cart for safe removal, Back to exit, or Confirm to cancel."
                    : "Press Back again to exit, or Confirm to cancel.",
            _ =>
                IsSafeEjectAvailable
                    ? "Press X to safely eject, B to exit, or A to cancel."
                    : "Press B again to exit, or A to cancel."
        };

    public string EjectPrompt =>
        LastInputDevice is InputDeviceKind.Controller or InputDeviceKind.Remote
            ? "X"
            : "E";

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
        if (value)
            ActivePageIndex = 2;

        OnPropertyChanged(nameof(ShouldPlayTrailer));
    }

    partial void OnIsHomeVisibleChanged(bool value)
    {
        if (value)
            ActivePageIndex = 0;
    }

    partial void OnIsVersionPickerVisibleChanged(bool value)
    {
        if (value)
            ActivePageIndex = 1;
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
        OnPropertyChanged(nameof(EjectPrompt));
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
        var trailerRuntimePreparation = PrepareTrailerRuntimeSafelyAsync();

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
            await trailerRuntimePreparation;

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
        if (input.Device == InputDeviceKind.Controller)
            LastControllerAction = input.Action.ToString();

        if (ShouldDebounce(input))
            return;

        // Steam Input can emit a keyboard event immediately after the matching
        // SDL gamepad event. Only let accepted input change the visible prompt
        // mode so that filtered duplicates cannot make the UI flicker.
        LastInputDevice = input.Device;

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
            await HandleExitInputAsync(input.Action);
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

    private async Task HandleExitInputAsync(LauncherAction action)
    {
        switch (action)
        {
            case LauncherAction.Back:
                _exitApplication();
                break;

            case LauncherAction.Confirm:
                CancelExitConfirmation();
                break;

            case LauncherAction.Trailer:
            case LauncherAction.Options:
                if (IsSafeEjectAvailable)
                    await EjectCartAsync();
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
            await minimumDisplay;

            SelectedGame = game;
            IsTrailerPlaybackEnabled = false;
            MetadataStatus = game.IsLaunchable
                ? string.Empty
                : "This game is not launchable on the current platform.";

            IsMetadataVisible = true;
            StartMetadataModulesLoad(game);
            StartRetroAchievementsLoad(game);
            // Keep the animated loading layer above the complete page
            // cross-fade. Exposing the transition host earlier can reveal its
            // outgoing presenter for a frame, which looks like the platform
            // chooser flashed for single-platform games.
            await Task.Delay(UseMotionEffects ? 380 : 30, transition.Token);
            IsMetadataLoading = false;
            LoadingGame = null;
            // Give the revealed metadata view one layout pass before attaching
            // the native video surface, preventing a blank first frame.
            await Task.Delay(UseMotionEffects ? 30 : 1, transition.Token);
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

    private async Task PrepareTrailerRuntimeSafelyAsync()
    {
        try
        {
            await _prepareTrailerRuntime(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A missing video runtime should disable trailers, not prevent the
            // cart library or metadata page from opening.
            Trace.WriteLine($"Trailer runtime preparation failed: {ex}");
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
        ClearMetadataModules();
        ClearRetroAchievements();

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
            UseShellExecute = false,
            CreateNoWindow = true
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
            EjectStatus = "Asking CLC-Cart Monitor to safely remove this cart…";
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
        IsLaunchTransitionVisible = true;
        LaunchingGame = game;
        IsTrailerPlaybackEnabled = false;
        MetadataStatus = $"Launching {game.Name}…";

        var launchTransitionStarted = Stopwatch.GetTimestamp();

        var request = new GameLaunchRequest(
            game.Name,
            game.Entry.FolderPath,
            game.Entry.LaunchTarget,
            game.Entry.Configuration.Behavior);

        try
        {
            // Let Avalonia present and animate the launch transition before
            // process creation performs any synchronous platform work.
            await Task.Delay(50);

            var result =
                await _launchService.LaunchAsync(request);
            var playtimeStartedAt = Stopwatch.GetTimestamp();

            MetadataStatus = result.Message;

            var remainingTransitionTime =
                TimeSpan.FromMilliseconds(650) -
                Stopwatch.GetElapsedTime(launchTransitionStarted);
            if (remainingTransitionTime > TimeSpan.Zero)
                await Task.Delay(remainingTransitionTime);

            IsLaunchTransitionVisible = false;
            LaunchingGame = null;

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
            {
                await session.WaitForExitAsync();

                if (session.WasGameObserved)
                {
                    var duration = Stopwatch.GetElapsedTime(playtimeStartedAt);
                    await _playtimeStore.RecordSessionAsync(
                        GameIdentity.Resolve(game.Entry.Configuration.Game),
                        duration,
                        DateTimeOffset.UtcNow);
                }
            }

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
            IsLaunchTransitionVisible = false;
            LaunchingGame = null;
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

        if (!File.Exists(path))
        {
            var packagedPrefix = Path.Combine("System", "Assets") + Path.DirectorySeparatorChar;
            if (normalized.StartsWith(packagedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var sourceRelative = normalized[packagedPrefix.Length..];
                var sourceAssetPath = Path.Combine(_portablePaths.Root, "Assets", sourceRelative);
                if (File.Exists(sourceAssetPath))
                    path = sourceAssetPath;
            }
        }

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
        ClearMetadataModules();
        ClearRetroAchievements();
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
    }

    private void StartRetroAchievementsLoad(GameCardViewModel game)
    {
        ClearRetroAchievements();
        var achievements = game.Entry.Configuration.Achievements;
        if (!achievements.RetroAchievementsEnabled || achievements.RetroAchievementsGameId is not > 0)
            return;

        IsRetroAchievementsLoading = true;
        RetroAchievementsStatus = "CHECKING RETROACHIEVEMENTS…";
        var cancellation = new CancellationTokenSource();
        _retroAchievementsCancellation = cancellation;
        _ = LoadRetroAchievementsAsync(achievements.RetroAchievementsGameId.Value, cancellation);
    }

    private async Task LoadRetroAchievementsAsync(int gameId, CancellationTokenSource cancellation)
    {
        try
        {
            if (_retroAchievementsClient is null || _metadataHttpClient is null)
            {
                RetroAchievementsStatus = "RETROACHIEVEMENTS IS UNAVAILABLE";
                return;
            }

            var userTask = MetadataSecretStore.ReadAsync(MetadataSecretStore.RetroAchievementsUserName, cancellation.Token);
            var keyTask = MetadataSecretStore.ReadAsync(MetadataSecretStore.RetroAchievementsWebApiKey, cancellation.Token);
            await Task.WhenAll(userTask, keyTask);
            var userName = await userTask;
            var apiKey = await keyTask;
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(apiKey))
            {
                RetroAchievementsStatus = "SET UP YOUR ACCOUNT IN THE CONFIGURATOR";
                return;
            }

            var progress = await _retroAchievementsClient.GetUserGameProgressAsync(
                userName, gameId, apiKey, _retroAchievementsCacheDirectory, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            RetroAchievementsProgress = $"{progress.EarnedAchievements} / {progress.TotalAchievements} UNLOCKED";
            RetroAchievementsPoints = $"{progress.EarnedPoints:N0} POINTS";
            RetroAchievementsAward = progress.Award;
            RetroAchievementsPercent = progress.TotalAchievements > 0
                ? progress.EarnedAchievements * 100d / progress.TotalAchievements
                : 0d;
            RetroAchievementsStatus = progress.TotalAchievements > 0
                ? "RETROACHIEVEMENTS"
                : "NO ACHIEVEMENTS ARE AVAILABLE FOR THIS GAME";

            if (progress.TotalAchievements > 0)
            {
                // Prefer the richer RetroAchievements presentation if both
                // providers happen to be configured for the same title.
                IsSteamAchievementsVisible = false;
                IsExophaseAchievementsVisible = false;
                IsRetroAchievementsVisible = true;
                UpsertMetadataModule(CreateAchievementsModule(
                    progress.EarnedAchievements,
                    progress.TotalAchievements));
            }

            var badgeTasks = progress.RecentAchievements.Select(item =>
                CreateAchievementItemAsync(item, cancellation.Token));
            foreach (var item in await Task.WhenAll(badgeTasks))
                RetroAchievementsRecent.Add(item);
            HasRetroAchievementsRecent = RetroAchievementsRecent.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"RetroAchievements progress could not be loaded: {ex}");
            RetroAchievementsStatus = "PROGRESS UNAVAILABLE — OFFLINE DATA WILL BE RETRIED LATER";
        }
        finally
        {
            if (ReferenceEquals(_retroAchievementsCancellation, cancellation))
                IsRetroAchievementsLoading = false;
        }
    }

    private async Task<RetroAchievementItemViewModel> CreateAchievementItemAsync(
        RetroAchievementProgressItem achievement,
        CancellationToken cancellationToken)
    {
        Bitmap? badge = null;
        if (!string.IsNullOrWhiteSpace(achievement.BadgeName))
        {
            try
            {
                var url = RetroAchievementsClient.ResolveBadgeUrl(achievement.BadgeName);
                var cacheName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + ".png";
                var cachePath = Path.Combine(_retroAchievementsCacheDirectory, "Badges", cacheName);
                if (!File.Exists(cachePath))
                {
                    var bytes = await _metadataHttpClient!.GetByteArrayAsync(url, cancellationToken);
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
                }
                badge = new Bitmap(cachePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Trace.WriteLine($"RetroAchievements badge could not be loaded: {ex.Message}");
            }
        }

        return new RetroAchievementItemViewModel(
            achievement.Title, achievement.Description, achievement.Points,
            achievement.EarnedHardcore, badge);
    }

    private void ClearRetroAchievements()
    {
        _retroAchievementsCancellation?.Cancel();
        _retroAchievementsCancellation?.Dispose();
        _retroAchievementsCancellation = null;
        foreach (var achievement in RetroAchievementsRecent) achievement.Dispose();
        RetroAchievementsRecent.Clear();
        IsRetroAchievementsVisible = false;
        IsRetroAchievementsLoading = false;
        HasRetroAchievementsRecent = false;
        RetroAchievementsStatus = "";
        RetroAchievementsProgress = "";
        RetroAchievementsPoints = "";
        RetroAchievementsAward = "";
        RetroAchievementsPercent = 0d;
    }

    private void StartMetadataModulesLoad(GameCardViewModel game)
    {
        ClearMetadataModules();
        StartLocalPlaytimeLoad(game);

        if (game.LauncherKind is not LauncherKind.Local and not LauncherKind.Custom &&
            !string.Equals(game.Launcher, "Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            UpsertMetadataModule(new MetadataModuleViewModel(
                "library",
                "LIBRARY",
                game.Launcher,
                MetadataModuleKind.Library,
                10));
        }

        if (!string.Equals(
                game.PlatformDisplay,
                "Platform unavailable",
                StringComparison.OrdinalIgnoreCase))
        {
            UpsertMetadataModule(new MetadataModuleViewModel(
                "platform",
                "PLATFORM",
                game.PlatformDisplay,
                MetadataModuleKind.Platform,
                20));
        }

        if (_steamPlayerStatsClient is null ||
            !uint.TryParse(game.SteamAppId, out var appId))
        {
            StartExophaseLoad(game);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _steamStatsCancellation = cancellation;
        _ = LoadSteamPlayerStatsAsync(game, appId, cancellation);
    }

    private void StartLocalPlaytimeLoad(GameCardViewModel game)
    {
        var cancellation = new CancellationTokenSource();
        _playtimeCancellation = cancellation;
        _ = LoadLocalPlaytimeAsync(game, cancellation);
    }

    private async Task LoadLocalPlaytimeAsync(
        GameCardViewModel game,
        CancellationTokenSource cancellation)
    {
        try
        {
            var record = await _playtimeStore.GetAsync(
                GameIdentity.Resolve(game.Entry.Configuration.Game),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (record is null)
                return;

            UpsertPlaytimeModules(record);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Local playtime could not be loaded: {ex.Message}");
        }
    }

    private async Task LoadSteamPlayerStatsAsync(
        GameCardViewModel game,
        uint appId,
        CancellationTokenSource cancellation)
    {
        try
        {
            var apiKey = await MetadataSecretStore.ReadAsync(
                MetadataSecretStore.SteamWebApiKey,
                cancellation.Token);
            var steamAccountId = SteamLocalAccountResolver.ResolveMostRecentAccountId();
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamAccountId))
            {
                StartExophaseLoad(game);
                return;
            }

            var stats = await _steamPlayerStatsClient!.GetAsync(
                apiKey,
                steamAccountId,
                appId,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (stats is null)
            {
                StartExophaseLoad(game);
                return;
            }

            var playtime = await _playtimeStore.ImportSteamAsync(
                GameIdentity.Resolve(game.Entry.Configuration.Game),
                stats.PlaytimeMinutes,
                stats.LastPlayed,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            UpsertPlaytimeModules(playtime);

            if (stats.TotalAchievements is > 0 && stats.EarnedAchievements is { } earned)
            {
                if (!IsRetroAchievementsVisible)
                {
                    IsSteamAchievementsVisible = true;
                    IsExophaseAchievementsVisible = false;
                    SteamAchievementsProgress = $"{earned} / {stats.TotalAchievements.Value} UNLOCKED";
                    SteamAchievementsPercent = earned * 100d / stats.TotalAchievements.Value;
                }
                UpsertMetadataModule(CreateAchievementsModule(earned, stats.TotalAchievements.Value));
            }
            else
            {
                StartExophaseLoad(game);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // Do not log the request URI because Steam's API key is a query
            // parameter on these official endpoints.
            Trace.WriteLine($"Steam player modules could not be loaded ({ex.GetType().Name}).");
            StartExophaseLoad(game);
        }
    }

    private void StartExophaseLoad(GameCardViewModel game)
    {
        if (_exophaseClient is null || IsRetroAchievementsVisible || IsSteamAchievementsVisible)
            return;

        _exophaseCancellation?.Cancel();
        _exophaseCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _exophaseCancellation = cancellation;
        _ = LoadExophaseAsync(game, cancellation);
    }

    private async Task LoadExophaseAsync(
        GameCardViewModel game,
        CancellationTokenSource cancellation)
    {
        try
        {
            var playerId = await MetadataSecretStore.ReadAsync(
                MetadataSecretStore.ExophasePlayerId,
                cancellation.Token);
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            var progress = await _exophaseClient!.FindGameAsync(
                playerId,
                game.Entry.Configuration.Game.Name,
                game.PlatformDisplay,
                _exophaseCacheDirectory,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (progress is null)
                return;

            if (progress.PlaytimeMinutes > 0)
            {
                UpsertMetadataModule(new MetadataModuleViewModel(
                    "time-played",
                    "TIME PLAYED",
                    FormatPlaytime(progress.PlaytimeMinutes * 60L),
                    MetadataModuleKind.TimePlayed,
                    40));
            }

            if (progress.LastPlayed is { } lastPlayed)
            {
                UpsertMetadataModule(new MetadataModuleViewModel(
                    "last-played",
                    "LAST PLAYED",
                    lastPlayed.ToLocalTime().ToString("MMM d, yyyy"),
                    MetadataModuleKind.LastPlayed,
                    30));
            }

            if (progress.TotalAchievements <= 0 ||
                IsRetroAchievementsVisible || IsSteamAchievementsVisible)
                return;

            IsExophaseAchievementsVisible = true;
            ExophaseAchievementsProgress =
                $"{progress.EarnedAchievements} / {progress.TotalAchievements} UNLOCKED";
            ExophaseAchievementsPercent = progress.CompletionPercent;
            UpsertMetadataModule(CreateAchievementsModule(
                progress.EarnedAchievements,
                progress.TotalAchievements));

            if (progress.RecentAchievements is { Count: > 0 })
            {
                var badgeTasks = progress.RecentAchievements.Select(item =>
                    CreateExophaseAchievementItemAsync(item, cancellation.Token));
                foreach (var item in await Task.WhenAll(badgeTasks))
                    RetroAchievementsRecent.Add(item);
                HasRetroAchievementsRecent = RetroAchievementsRecent.Count > 0;
            }
            else
            {
                RetroAchievementsStatus =
                    "Reconnect Exophase in Configurator Settings to import earned achievement details.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Exophase modules could not be loaded ({ex.GetType().Name}).");
        }
    }

    private async Task<RetroAchievementItemViewModel> CreateExophaseAchievementItemAsync(
        ExophaseAchievement achievement,
        CancellationToken cancellationToken)
    {
        Bitmap? badge = null;
        if (_metadataHttpClient is not null &&
            TryResolveExophaseImageUrl(achievement.IconUrl, out var imageUrl))
        {
            try
            {
                var cacheName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(imageUrl))).ToLowerInvariant() + ".png";
                var cachePath = Path.Combine(_exophaseCacheDirectory, "Badges", cacheName);
                if (!File.Exists(cachePath))
                {
                    var bytes = await _metadataHttpClient.GetByteArrayAsync(imageUrl, cancellationToken);
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
                }
                badge = new Bitmap(cachePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Trace.WriteLine($"Exophase badge could not be loaded ({ex.GetType().Name}).");
            }
        }

        var detail = achievement.EarnedAt is { } earnedAt
            ? $"UNLOCKED {earnedAt.ToLocalTime():MMM d, yyyy}"
            : "UNLOCKED";
        return new RetroAchievementItemViewModel(
            achievement.Title,
            achievement.Description,
            0,
            false,
            badge,
            detail);
    }

    private static bool TryResolveExophaseImageUrl(string value, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "https" or "http")
        {
            url = absolute.ToString();
            return true;
        }
        if (Uri.TryCreate(new Uri("https://www.exophase.com/"), value, out var relative))
        {
            url = relative.ToString();
            return true;
        }
        return false;
    }

    private static MetadataModuleViewModel CreateAchievementsModule(int earned, int total) =>
        new(
            "achievements",
            "ACHIEVEMENTS",
            $"{earned} / {total}",
            MetadataModuleKind.Achievements,
            50);

    private void UpsertMetadataModule(MetadataModuleViewModel module)
    {
        var existing = MetadataModules.FirstOrDefault(item => item.Key == module.Key);
        if (existing is not null)
            MetadataModules.Remove(existing);

        var index = 0;
        while (index < MetadataModules.Count && MetadataModules[index].Order < module.Order)
            index++;
        MetadataModules.Insert(index, module);
    }

    private void ClearMetadataModules()
    {
        _playtimeCancellation?.Cancel();
        _playtimeCancellation?.Dispose();
        _playtimeCancellation = null;
        _steamStatsCancellation?.Cancel();
        _steamStatsCancellation?.Dispose();
        _steamStatsCancellation = null;
        _exophaseCancellation?.Cancel();
        _exophaseCancellation?.Dispose();
        _exophaseCancellation = null;
        MetadataModules.Clear();
        IsSteamAchievementsVisible = false;
        SteamAchievementsProgress = "";
        SteamAchievementsPercent = 0d;
        IsExophaseAchievementsVisible = false;
        ExophaseAchievementsProgress = "";
        ExophaseAchievementsPercent = 0d;
    }

    private void UpsertPlaytimeModules(GamePlaytimeRecord record)
    {
        if (record.LastPlayed is { } lastPlayed)
        {
            UpsertMetadataModule(new MetadataModuleViewModel(
                "last-played",
                "LAST PLAYED",
                lastPlayed.ToLocalTime().ToString("MMM d, yyyy"),
                MetadataModuleKind.LastPlayed,
                30));
        }

        if (record.TotalSeconds > 0)
        {
            UpsertMetadataModule(new MetadataModuleViewModel(
                "time-played",
                "TIME PLAYED",
                FormatPlaytime(record.TotalSeconds),
                MetadataModuleKind.TimePlayed,
                40));
        }
    }

    private static string FormatPlaytime(long seconds)
    {
        var minutes = Math.Max(1L, (seconds + 59L) / 60L);
        if (minutes < 60)
            return $"{minutes} min";

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;
        return remainingMinutes == 0
            ? $"{hours:N0}h"
            : $"{hours:N0}h {remainingMinutes}m";
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
