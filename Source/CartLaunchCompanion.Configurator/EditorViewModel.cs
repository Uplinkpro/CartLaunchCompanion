using System.ComponentModel;
using System.Runtime.CompilerServices;
using CartLaunchCompanion.Core.Configuration;
using Avalonia.Media.Imaging;

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

    public GameConfiguration Configuration { get => _configuration; set { _configuration = value; Changed(); Changed(nameof(JsonPreview)); } }
    public string FilePath { get => _filePath; set { _filePath = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string JsonPreview { get => _jsonPreview; set { _jsonPreview = value; Changed(); } }
    public bool HasErrors { get => _hasErrors; set { _hasErrors = value; Changed(); } }
    public Bitmap? ArtworkPreview { get => _artworkPreview; set { _artworkPreview = value; Changed(); Changed(nameof(HasArtworkPreview)); } }
    public string ArtworkPreviewTitle { get => _artworkPreviewTitle; set { _artworkPreviewTitle = value; Changed(); } }
    public bool HasArtworkPreview => ArtworkPreview is not null;
    public string PathStatus { get => _pathStatus; set { _pathStatus = value; Changed(); } }
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
    public void RefreshPreview() => JsonPreview = GameConfigurationJson.Serialize(Configuration);
    public void Reset() { Configuration = CreateDefault(); FilePath = "No game folder selected"; Status = "New configuration ready."; PathStatus = "Choose a game configuration folder before locating portable files."; ArtworkPreview = null; ArtworkPreviewTitle = "No artwork selected yet"; RefreshPreview(); }
    private static GameConfiguration CreateDefault() { var c = new GameConfiguration(); c.Launch.Linux.Enabled = false; return c; }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
