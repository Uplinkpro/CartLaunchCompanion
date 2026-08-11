using System.ComponentModel;
using System.Runtime.CompilerServices;
using CartLaunchCompanion.Core.Configuration;
using Avalonia.Media.Imaging;
using System.Collections.ObjectModel;

namespace CartLaunchCompanion.Configurator;

public sealed class EditorViewModel : INotifyPropertyChanged
{
    private GameConfiguration _configuration = CreateDefault();
    private string _filePath = "No game folder selected";
    private string _status = "Start a new configuration or open an existing game.json file.";
    private string _jsonPreview = "";
    private bool _hasErrors;
    private Bitmap? _artworkPreview;
    private string _artworkPreviewTitle = "No artwork selected yet";
    private string _pathStatus = "Choose a game configuration folder before locating portable files.";
    private CollectionConfiguration _collection = new();
    private Bitmap? _collectionLogoPreview;
    private string _collectionLogoStatus = "No collection logo configured.";
    private ExistingGameOption? _selectedExistingGame;
    private string _artworkAuditSummary = "Artwork has not been checked yet.";

    public GameConfiguration Configuration { get => _configuration; set { _configuration = value; Changed(); Changed(nameof(JsonPreview)); } }
    public string FilePath { get => _filePath; set { _filePath = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string JsonPreview { get => _jsonPreview; set { _jsonPreview = value; Changed(); } }
    public bool HasErrors { get => _hasErrors; set { _hasErrors = value; Changed(); } }
    public Bitmap? ArtworkPreview { get => _artworkPreview; set { _artworkPreview = value; Changed(); Changed(nameof(HasArtworkPreview)); } }
    public string ArtworkPreviewTitle { get => _artworkPreviewTitle; set { _artworkPreviewTitle = value; Changed(); } }
    public bool HasArtworkPreview => ArtworkPreview is not null;
    public string PathStatus { get => _pathStatus; set { _pathStatus = value; Changed(); } }
    public CollectionConfiguration Collection { get => _collection; set { _collection = value; Changed(); } }
    public Bitmap? CollectionLogoPreview { get => _collectionLogoPreview; set { _collectionLogoPreview?.Dispose(); _collectionLogoPreview = value; Changed(); Changed(nameof(HasCollectionLogoPreview)); } }
    public bool HasCollectionLogoPreview => CollectionLogoPreview is not null;
    public string CollectionLogoStatus { get => _collectionLogoStatus; set { _collectionLogoStatus = value; Changed(); } }
    public ObservableCollection<ExistingGameOption> ExistingGames { get; } = [];
    public ExistingGameOption? SelectedExistingGame { get => _selectedExistingGame; set { _selectedExistingGame = value; Changed(); } }
    public ObservableCollection<ArtworkAuditItem> ArtworkAuditResults { get; } = [];
    public string ArtworkAuditSummary { get => _artworkAuditSummary; set { _artworkAuditSummary = value; Changed(); } }
    public bool HasExistingGames => ExistingGames.Count > 0;
    public Array LauncherKinds { get; } = Enum.GetValues<LauncherKind>();
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
    public void RefreshPreview() => JsonPreview = GameConfigurationJson.Serialize(Configuration);
    public void Reset() { Configuration = CreateDefault(); FilePath = "No game folder selected"; Status = "New configuration ready."; PathStatus = "Choose a game configuration folder before locating portable files."; ArtworkPreview = null; ArtworkPreviewTitle = "No artwork selected yet"; RefreshPreview(); }
    private static GameConfiguration CreateDefault() { var c = new GameConfiguration(); c.Launch.Linux.Enabled = false; return c; }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ExistingGameOption(string Name, string ConfigurationPath)
{
    public override string ToString() => Name;
}

public sealed record ArtworkAuditItem(
    string Game,
    string Asset,
    string Symbol,
    string StatusColor,
    string Message);
