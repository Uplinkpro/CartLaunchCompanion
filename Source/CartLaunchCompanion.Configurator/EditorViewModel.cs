using System.ComponentModel;
using System.Runtime.CompilerServices;
using CartLaunchCompanion.Core.Configuration;
using Avalonia.Media.Imaging;
using System.Collections.ObjectModel;

namespace CartLaunchCompanion.Configurator;

public sealed class EditorViewModel : INotifyPropertyChanged
{
    private GameConfiguration _configuration = CreateDefault();
    private string _filePath = "No CLC game folder selected (normally CartLaunchCompanion/Games/Game Name)";
    private string _status = "Start a new configuration or open an existing game.json file.";
    private string _jsonPreview = "";
    private bool _hasErrors;
    private Bitmap? _artworkPreview;
    private string _artworkPreviewTitle = "No artwork selected yet";
    private Bitmap? _coverPreview;
    private Bitmap? _backgroundPreview;
    private Bitmap? _heroPreview;
    private Bitmap? _logoPreview;
    private Bitmap? _iconPreview;
    private string _pathStatus = "Choose a CLC configuration folder inside CartLaunchCompanion/Games—not the Steam or Rockstar installation folder.";
    private CollectionConfiguration _collection = new();
    private Bitmap? _collectionLogoPreview;
    private string _collectionLogoStatus = "No collection logo configured.";
    private ExistingGameOption? _selectedExistingGame;
    private string _artworkAuditSummary = "Artwork has not been checked yet.";
    private string _windowsLauncherStatus = "Choose a launcher, then verify only that launcher on this computer.";
    private string _linuxLauncherStatus = "Choose a launcher, then verify only that launcher on this computer or Steam Deck.";
    private InstalledEmulatorOption? _selectedInstalledEmulator;
    private string _newShelfName = "";
    private string _collectionLayoutStatus = "Choose a shelf for each primary game, adjust its order, then save the layout.";

    public GameConfiguration Configuration { get => _configuration; set { _configuration = value; Changed(); Changed(nameof(JsonPreview)); } }
    public string FilePath { get => _filePath; set { _filePath = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string JsonPreview { get => _jsonPreview; set { _jsonPreview = value; Changed(); } }
    public bool HasErrors { get => _hasErrors; set { _hasErrors = value; Changed(); } }
    public Bitmap? ArtworkPreview { get => _artworkPreview; set { _artworkPreview = value; Changed(); Changed(nameof(HasArtworkPreview)); } }
    public string ArtworkPreviewTitle { get => _artworkPreviewTitle; set { _artworkPreviewTitle = value; Changed(); } }
    public bool HasArtworkPreview => ArtworkPreview is not null;
    public Bitmap? CoverPreview { get => _coverPreview; set { _coverPreview?.Dispose(); _coverPreview = value; Changed(); Changed(nameof(HasCoverPreview)); } }
    public Bitmap? BackgroundPreview { get => _backgroundPreview; set { _backgroundPreview?.Dispose(); _backgroundPreview = value; Changed(); Changed(nameof(HasBackgroundPreview)); Changed(nameof(HasHeroOnlyPreview)); Changed(nameof(HasStagePreview)); } }
    public Bitmap? HeroPreview { get => _heroPreview; set { _heroPreview?.Dispose(); _heroPreview = value; Changed(); Changed(nameof(HasHeroPreview)); Changed(nameof(HasHeroOnlyPreview)); Changed(nameof(HasStagePreview)); } }
    public Bitmap? LogoPreview { get => _logoPreview; set { _logoPreview?.Dispose(); _logoPreview = value; Changed(); Changed(nameof(HasLogoPreview)); } }
    public Bitmap? IconPreview { get => _iconPreview; set { _iconPreview?.Dispose(); _iconPreview = value; Changed(); Changed(nameof(HasIconPreview)); } }
    public bool HasCoverPreview => CoverPreview is not null;
    public bool HasBackgroundPreview => BackgroundPreview is not null;
    public bool HasHeroPreview => HeroPreview is not null;
    public bool HasHeroOnlyPreview => HeroPreview is not null && BackgroundPreview is null;
    public bool HasStagePreview => BackgroundPreview is not null || HeroPreview is not null;
    public bool HasLogoPreview => LogoPreview is not null;
    public bool HasIconPreview => IconPreview is not null;
    public string PathStatus { get => _pathStatus; set { _pathStatus = value; Changed(); } }
    public CollectionConfiguration Collection { get => _collection; set { _collection = value; Changed(); } }
    public Bitmap? CollectionLogoPreview { get => _collectionLogoPreview; set { _collectionLogoPreview?.Dispose(); _collectionLogoPreview = value; Changed(); Changed(nameof(HasCollectionLogoPreview)); } }
    public bool HasCollectionLogoPreview => CollectionLogoPreview is not null;
    public string CollectionLogoStatus { get => _collectionLogoStatus; set { _collectionLogoStatus = value; Changed(); } }
    public ObservableCollection<ExistingGameOption> ExistingGames { get; } = [];
    public ExistingGameOption? SelectedExistingGame { get => _selectedExistingGame; set { _selectedExistingGame = value; Changed(); } }
    public ObservableCollection<ArtworkAuditItem> ArtworkAuditResults { get; } = [];
    public string ArtworkAuditSummary { get => _artworkAuditSummary; set { _artworkAuditSummary = value; Changed(); } }
    public string WindowsLauncherStatus { get => _windowsLauncherStatus; set { _windowsLauncherStatus = value; Changed(); } }
    public string LinuxLauncherStatus { get => _linuxLauncherStatus; set { _linuxLauncherStatus = value; Changed(); } }
    public ObservableCollection<InstalledEmulatorOption> InstalledEmulators { get; } = [];
    public InstalledEmulatorOption? SelectedInstalledEmulator { get => _selectedInstalledEmulator; set { _selectedInstalledEmulator = value; Changed(); } }
    public bool HasInstalledEmulators => InstalledEmulators.Count > 0;
    public ObservableCollection<CollectionShelfEditor> CollectionShelves { get; } = [];
    public ObservableCollection<CollectionGameEditor> UnassignedCollectionGames { get; } = [];
    public ObservableCollection<CollectionGameEditor> CollectionGames { get; } = [];
    public ObservableCollection<string> CollectionShelfChoices { get; } = ["(Unassigned)"];
    public ObservableCollection<string> PlatformSuggestions { get; } = [];
    public ObservableCollection<LauncherKind> WindowsLauncherKinds { get; } = [];
    public string NewShelfName { get => _newShelfName; set { _newShelfName = value; Changed(); } }
    public string CollectionLayoutStatus { get => _collectionLayoutStatus; set { _collectionLayoutStatus = value; Changed(); } }
    public bool HasExistingGames => ExistingGames.Count > 0;
    public LauncherKind[] LinuxLauncherKinds { get; } =
    [
        LauncherKind.Steam,
        LauncherKind.Heroic,
        LauncherKind.Flatpak,
        LauncherKind.Local,
        LauncherKind.Wine,
        LauncherKind.Proton,
        LauncherKind.Custom
    ];
    public Array PreferredPlatforms { get; } = Enum.GetValues<PreferredPlatform>();
    public Array DeckRatings { get; } = Enum.GetValues<SteamDeckCompatibility>();
    public Array GamepadRatings { get; } = Enum.GetValues<GamepadSupport>();
    public string[] ProtonSuggestions { get; } =
    [
        "proton",
        "Proton Experimental",
        "Proton 10",
        "Proton 9",
        "GE-Proton"
    ];

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyExistingGamesChanged() => Changed(nameof(HasExistingGames));
    public void NotifyInstalledEmulatorsChanged() => Changed(nameof(HasInstalledEmulators));
    public void RefreshPreview() => JsonPreview = GameConfigurationJson.Serialize(Configuration);
    public void Reset() { Configuration = CreateDefault(); FilePath = "No CLC game folder selected (normally CartLaunchCompanion/Games/Game Name)"; Status = "New configuration ready. Find the game on Steam to fill details and select its CLC folder automatically."; PathStatus = "Choose a CLC configuration folder inside CartLaunchCompanion/Games—not the Steam or Rockstar installation folder."; ArtworkPreview = null; CoverPreview = null; HeroPreview = null; BackgroundPreview = null; LogoPreview = null; IconPreview = null; ArtworkPreviewTitle = "No artwork selected yet"; RefreshPreview(); }
    private static GameConfiguration CreateDefault() { var c = new GameConfiguration(); c.Game.Id = GameIdentity.Create(); c.Launch.Linux.Enabled = false; return c; }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ExistingGameOption(string Name, string ConfigurationPath, string SortName = "", string PlatformLabel = "")
{
    public string EffectiveSortName => string.IsNullOrWhiteSpace(SortName) ? Name : SortName.Trim();
    public override string ToString() => string.IsNullOrWhiteSpace(PlatformLabel) ? Name : $"{Name} — {PlatformLabel.Trim()}";
}

public sealed record ArtworkAuditItem(
    string Game,
    string Asset,
    string Symbol,
    string StatusColor,
    string Message);
