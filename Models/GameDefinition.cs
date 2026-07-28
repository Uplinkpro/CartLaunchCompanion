using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;

namespace CartLaunchCompanion.Models;

public sealed class GameDefinition : INotifyPropertyChanged
{
    private ImageSource? _coverImage;
    private ImageSource? _headerImage;
    private ImageSource? _launcherLogo;
    private Brush _cardBorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public string Id { get; set; } = "";
    public string Name { get; set; } = "Unnamed Game";
    public string Description { get; set; } = "";
    public string DetailedDescription { get; set; } = "";
    public string Launcher { get; set; } = "Direct";
    [JsonPropertyName("SteamID")] public string SteamId { get; set; } = "";
    // Optional Steam app ID used only for metadata, artwork, trailers, and store fallback.
    // This does not change how the game is launched.
    [JsonPropertyName("SteamMetadataID")] public string SteamMetadataId { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string LaunchUri { get; set; } = "";
    public string EpicAppName { get; set; } = "";
    public string GogGameId { get; set; } = "";
    public string AmazonGameId { get; set; } = "";
    public string RockstarGameId { get; set; } = "";
    public string UbisoftGameId { get; set; } = "";
    // Optional executable process name (without .exe) used to restore the launcher after the game exits.
    public string ProcessName { get; set; } = "";
    public bool RestoreOnExit { get; set; } = true;
    public int ProcessStartTimeoutSeconds { get; set; } = 120;
    public int ProcessExitPollSeconds { get; set; } = 2;
    // Xbox/Game Pass application user model ID (AUMID), for example PackageFamilyName!App.
    public string XboxAppId { get; set; } = "";
    public string FlashFile { get; set; } = "";
    public string FlashPlayer { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string HeaderUrl { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Genre { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string Website { get; set; } = "";
    public string VideoUrl { get; set; } = "";

    // Backward-compatible aliases accepted in Game.json.
    [JsonPropertyName("TrailerUrl")]
    public string TrailerUrl
    {
        get => VideoUrl;
        set { if (!string.IsNullOrWhiteSpace(value)) VideoUrl = value; }
    }

    // Optional YouTube trailer. Kept separate from VideoUrl so storefront
    // metadata enrichment cannot overwrite an explicitly configured embed.
    [JsonPropertyName("YouTubeUrl")]
    public string YouTubeUrl { get; set; } = "";

    public string VideoFile { get; set; } = "";

    [JsonIgnore] public string FolderPath { get; set; } = "";
    [JsonIgnore] public string CoverPath { get; set; } = "";
    [JsonIgnore] public string HeaderPath { get; set; } = "";
    [JsonIgnore] public IReadOnlyList<string> SteamTrailerUrls { get; set; } = [];
    [JsonIgnore] public string EffectiveSteamMetadataId =>
        !string.IsNullOrWhiteSpace(SteamMetadataId) ? SteamMetadataId.Trim() : SteamId.Trim();


    [JsonIgnore] public string LauncherDisplayName { get; set; } = "";
    [JsonIgnore] public Brush LauncherBannerBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.DimGray);


    [JsonIgnore]
    public Brush CardBorderBrush
    {
        get => _cardBorderBrush;
        set
        {
            if (ReferenceEquals(_cardBorderBrush, value)) return;
            _cardBorderBrush = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public ImageSource? LauncherLogo
    {
        get => _launcherLogo;
        set
        {
            if (ReferenceEquals(_launcherLogo, value)) return;
            _launcherLogo = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public ImageSource? CoverImage
    {
        get => _coverImage;
        set
        {
            if (ReferenceEquals(_coverImage, value)) return;
            _coverImage = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public ImageSource? HeaderImage
    {
        get => _headerImage;
        set
        {
            if (ReferenceEquals(_headerImage, value)) return;
            _headerImage = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
