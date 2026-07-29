using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.Configuration;

public sealed class GameConfiguration
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "../../../Schemas/game.schema.json";

    public int FormatVersion { get; set; } = 2;

    public GameInformation Game { get; set; } = new();

    public ArtworkConfiguration Artwork { get; set; } = new();

    public LaunchConfiguration Launch { get; set; } = new();

    public BehaviorConfiguration Behavior { get; set; } = new();

    public string Notes { get; set; } = "";
}

public sealed class GameInformation
{
    public string Name { get; set; } = "";
    public string SortName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Genre { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string Players { get; set; } = "";
}

public sealed class ArtworkConfiguration
{
    public string Cover { get; set; } = "Artwork/Cover.jpg";
    public string Background { get; set; } = "Artwork/Background.jpg";
    public string Logo { get; set; } = "Artwork/Logo.png";
    public string Icon { get; set; } = "Artwork/Icon.png";
    public string Trailer { get; set; } = "Media/Trailer.mp4";

    public string SteamMetadataId { get; set; } = "";
    public bool DownloadMissingArtwork { get; set; } = true;

    public string CoverUrl { get; set; } = "";
    public string BackgroundUrl { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string TrailerUrl { get; set; } = "";
}

public sealed class LaunchConfiguration
{
    public PreferredPlatform PreferredPlatform { get; set; } =
        PreferredPlatform.Automatic;

    public WindowsLaunchConfiguration Windows { get; set; } = new();

    public LinuxLaunchConfiguration Linux { get; set; } = new();
}

public sealed class WindowsLaunchConfiguration
{
    public bool Enabled { get; set; } = true;
    public LauncherKind Launcher { get; set; } = LauncherKind.Steam;

    public string SteamId { get; set; } = "";
    public string XboxAppId { get; set; } = "";
    public string EpicAppName { get; set; } = "";
    public string GogGameId { get; set; } = "";
    public string UbisoftGameId { get; set; } = "";
    public string RockstarGameId { get; set; } = "";
    public string AmazonGameId { get; set; } = "";

    public string Executable { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Uri { get; set; } = "";
}

public sealed class LinuxLaunchConfiguration
{
    public bool Enabled { get; set; } = true;
    public LauncherKind Launcher { get; set; } = LauncherKind.Steam;

    public string SteamId { get; set; } = "";
    public string HeroicGameId { get; set; } = "";
    public string FlatpakAppId { get; set; } = "";

    public string Executable { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Uri { get; set; } = "";
    public string CompatibilityTool { get; set; } = "";
    public string WinePrefix { get; set; } = "";
}

public sealed class BehaviorConfiguration
{
    public bool RestoreLauncherAfterExit { get; set; } = true;
    public bool HideWhileGameRuns { get; set; } = true;
    public int ProcessStartTimeoutSeconds { get; set; } = 120;
    public int ProcessExitPollSeconds { get; set; } = 2;
}

[JsonConverter(typeof(JsonStringEnumConverter<PreferredPlatform>))]
public enum PreferredPlatform
{
    Automatic,
    Windows,
    Linux
}

[JsonConverter(typeof(JsonStringEnumConverter<LauncherKind>))]
public enum LauncherKind
{
    Steam,
    Xbox,
    Epic,
    Heroic,
    GOG,
    Ubisoft,
    Rockstar,
    Amazon,
    Local,
    Flatpak,
    Wine,
    Proton,
    Custom
}
