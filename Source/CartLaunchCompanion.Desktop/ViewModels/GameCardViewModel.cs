using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Portable;
using CommunityToolkit.Mvvm.Input;
using CartLaunchCompanion.Desktop.Themes;

namespace CartLaunchCompanion.Desktop.ViewModels;

public sealed class GameCardViewModel : ViewModelBase, IDisposable
{
    private readonly Action<GameCardViewModel> _openGame;
    private readonly List<Bitmap> _screenshotImages = [];
    private readonly DispatcherTimer _screenshotTimer;
    private bool _isSelected;
    private bool _isVersionSelected;
    private int _screenshotIndex;
    private Bitmap? _currentScreenshotImage;

    public GameCardViewModel(
        GameLibraryEntry entry,
        Action<GameCardViewModel> openGame)
    {
        Entry = entry;
        _openGame = openGame;

        OpenCommand = new RelayCommand(
            () => _openGame(this));

        CoverImage = TryLoadBitmap(entry.CoverPath);
        var background = TryLoadBitmap(entry.BackgroundPath);
        var hero = TryLoadBitmap(entry.HeroPath);
        // Older configurations stored Steam's panoramic library hero in Background.
        // Recognize it by shape so existing carts immediately render correctly.
        if (background is not null && background.PixelSize.Width / (double)background.PixelSize.Height >= 2.25)
        {
            if (hero is null)
                hero = background;
            else
                background.Dispose();
            background = null;
        }
        BackgroundImage = background;
        HeroImage = hero;
        LogoImage = TryLoadBitmap(entry.LogoPath);
        LauncherLogoImage = TryLoadBitmap(
            entry.LaunchTarget?.Launcher == LauncherKind.Custom &&
            entry.LaunchTarget.RequiredLauncher is null
                ? ResolvePlatformAssetPath(entry, "Logo.png")
                : null)
            ?? TryLoadBitmap(ResolveLauncherAssetPath(entry, "Logo.png"));
        LauncherBannerImage = TryLoadBitmap(
            entry.LaunchTarget?.Launcher == LauncherKind.Custom &&
            entry.LaunchTarget.RequiredLauncher is null
                ? ResolvePlatformAssetPath(entry, "Banner.png")
                : null)
            ?? TryLoadBitmap(ResolveLauncherAssetPath(entry, "Banner.png"));

        foreach (var path in entry.ScreenshotPaths)
        {
            var screenshot = TryLoadBitmap(path);
            if (screenshot is not null)
                _screenshotImages.Add(screenshot);
        }

        _currentScreenshotImage = _screenshotImages.FirstOrDefault() ?? BackgroundImage ?? HeroImage;
        var indicatorCount = _screenshotImages.Count > 0
            ? _screenshotImages.Count
            : CurrentScreenshotImage is not null ? 1 : 0;
        for (var index = 0; index < indicatorCount; index++)
            ScreenshotIndicators.Add(new ScreenshotIndicatorViewModel(AccentBrightColor, index == 0));
        _screenshotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _screenshotTimer.Tick += AdvanceScreenshot;
        if (_screenshotImages.Count > 1)
            _screenshotTimer.Start();
    }

    public GameLibraryEntry Entry { get; }
    public ObservableCollection<GameCardViewModel> Versions { get; } = [];
    public ObservableCollection<ScreenshotIndicatorViewModel> ScreenshotIndicators { get; } = [];

    public string Name => Entry.Configuration.Game.Name;
    public string VersionGroup => Entry.Configuration.Game.VersionGroup;
    public string PlatformLabel => Entry.Configuration.Game.PlatformLabel;
    public string PlatformDisplay => string.IsNullOrWhiteSpace(PlatformLabel) ? Launcher : PlatformLabel;
    public bool IsPrimaryVersion => Entry.Configuration.Game.PrimaryVersion;
    public bool HasMultipleVersions => Versions.Count > 1;
    public string Description => Entry.Configuration.Game.Description;
    public string Developer => Entry.Configuration.Game.Developer;
    public string Publisher => Entry.Configuration.Game.Publisher;
    public string Genre => Entry.Configuration.Game.Genre;
    public string ReleaseDate => Entry.Configuration.Game.ReleaseDate;
    public string Players => Entry.Configuration.Game.Players;
    public SteamDeckCompatibility SteamDeckCompatibility =>
        Entry.Configuration.Game.SteamDeckCompatibility;
    public bool HasSteamDeckCompatibility =>
        SteamDeckCompatibility is not SteamDeckCompatibility.Unknown;
    public string SteamDeckCompatibilityText => SteamDeckCompatibility switch
    {
        SteamDeckCompatibility.Verified => "DECK VERIFIED",
        SteamDeckCompatibility.Playable => "DECK PLAYABLE",
        SteamDeckCompatibility.Unsupported => "DECK UNSUPPORTED",
        _ => "DECK UNKNOWN"
    };
    public string SteamDeckCompatibilityGlyph => SteamDeckCompatibility switch
    {
        SteamDeckCompatibility.Verified => "✓",
        SteamDeckCompatibility.Playable => "!",
        SteamDeckCompatibility.Unsupported => "×",
        _ => "?"
    };
    public string SteamDeckCompatibilityColor => SteamDeckCompatibility switch
    {
        SteamDeckCompatibility.Verified => "#66C56C",
        SteamDeckCompatibility.Playable => "#E1B84B",
        SteamDeckCompatibility.Unsupported => "#D46A6A",
        _ => "#8A929E"
    };
    public GamepadSupport GamepadSupport => Entry.Configuration.Game.GamepadSupport;
    public bool HasGamepadSupport => GamepadSupport is not GamepadSupport.Unknown;
    public string GamepadSupportText => GamepadSupport switch
    {
        GamepadSupport.Full => "GAMEPAD FULL",
        GamepadSupport.Partial => "GAMEPAD PARTIAL",
        GamepadSupport.Unsupported => "NO GAMEPAD",
        _ => "GAMEPAD UNKNOWN"
    };
    public string GamepadSupportColor => GamepadSupport switch
    {
        GamepadSupport.Full => "#66C56C",
        GamepadSupport.Partial => "#E1B84B",
        GamepadSupport.Unsupported => "#D46A6A",
        _ => "#8A929E"
    };

    public string Launcher =>
        Entry.LaunchTarget?.BrandingLauncher switch
        {
            LauncherKind.Local => "EXE",
            LauncherKind.Custom => "Emulator",
            LauncherKind.BattleNet => "Battle.net",
            LauncherKind.HoYoverse => "HoYoverse",
            LauncherKind.ItchIo => "itch.io",
            { } launcher => launcher.ToString(),
            null => "Unavailable"
        };

    public LauncherKind LauncherKind =>
        Entry.LaunchTarget?.BrandingLauncher
        ?? LauncherKind.Custom;

    public bool UsesLauncherBranding =>
        LauncherKind != LauncherKind.Local && LauncherLogoImage is not null;
    public bool UsesCartLaunchBranding => !UsesLauncherBranding;

    public LauncherTheme Theme =>
        LauncherThemeCatalog.Get(LauncherKind);

    public string AccentColor => Theme.Accent;
    public string AccentBrightColor => Theme.AccentBright;
    public string AccentMutedColor => Theme.AccentMuted;
    public string BeamCenterColor => Theme.BeamCenter;
    public string BeamEdgeColor => Theme.BeamEdge;
    public string FloorLightColor => Theme.FloorLight;
    public string GlyphForegroundColor => Theme.GlyphForeground;

    public Bitmap? CoverImage { get; }
    public Bitmap? HeroImage { get; }
    public Bitmap? BackgroundImage { get; }
    public Bitmap? LogoImage { get; }
    public Bitmap? LauncherLogoImage { get; }
    public Bitmap? LauncherBannerImage { get; }
    public Bitmap? CurrentScreenshotImage
    {
        get => _currentScreenshotImage;
        private set => SetProperty(ref _currentScreenshotImage, value);
    }

    public bool HasCover => CoverImage is not null;
    public bool HasNoCover => CoverImage is null;
    public bool HasLauncherBanner => LauncherBannerImage is not null;
    public bool HasNoLauncherBanner => LauncherBannerImage is null;
    public bool HasBackground => BackgroundImage is not null;
    public bool HasHero => HeroImage is not null;
    public bool HasHeroOnly => BackgroundImage is null && HeroImage is not null;
    public bool HasLogo => LogoImage is not null;
    public bool HasNoLogo => LogoImage is null;
    public bool HasTrailer => Entry.TrailerPath is not null;
    public string? TrailerSource => Entry.TrailerSource;
    public bool HasTrailerSource => !string.IsNullOrWhiteSpace(TrailerSource);
    public bool HasNoTrailerSource => !HasTrailerSource;
    public bool HasScreenshots => CurrentScreenshotImage is not null;
    public bool IsLaunchable => Entry.IsLaunchable;


    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string DeveloperDisplay =>
        string.IsNullOrWhiteSpace(Developer)
            ? "Developer not listed"
            : Developer;

    public string PublisherDisplay =>
        string.IsNullOrWhiteSpace(Publisher)
            ? "Publisher not listed"
            : Publisher;

    public string GenreDisplay =>
        string.IsNullOrWhiteSpace(Genre)
            ? "Genre not listed"
            : Genre;

    public string ReleaseDateDisplay =>
        string.IsNullOrWhiteSpace(ReleaseDate)
            ? "Release date not listed"
            : ReleaseDate;

    public string PlayersDisplay =>
        string.IsNullOrWhiteSpace(Players)
            ? "Players not listed"
            : Players;

    public string DescriptionDisplay =>
        string.IsNullOrWhiteSpace(Description)
            ? "No description is available for this game."
            : Description;

    public string TrailerStatus =>
        HasTrailer
            ? "Trailer ready"
            : "No local trailer configured";

    public string Initials
    {
        get
        {
            var words = Name
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            return string.Concat(
                words.Take(2).Select(
                    word => char.ToUpperInvariant(word[0])));
        }
    }

    public IRelayCommand OpenCommand { get; }

    public void SetVersions(IEnumerable<GameCardViewModel> versions)
    {
        Versions.Clear();
        foreach (var version in versions)
            Versions.Add(version);
        OnPropertyChanged(nameof(HasMultipleVersions));
    }

    public bool IsVersionSelected
    {
        get => _isVersionSelected;
        set => SetProperty(ref _isVersionSelected, value);
    }

    public void Dispose()
    {
        _screenshotTimer.Stop();
        _screenshotTimer.Tick -= AdvanceScreenshot;
        CoverImage?.Dispose();
        BackgroundImage?.Dispose();
        if (!ReferenceEquals(HeroImage, BackgroundImage)) HeroImage?.Dispose();
        LogoImage?.Dispose();
        LauncherLogoImage?.Dispose();
        LauncherBannerImage?.Dispose();
        foreach (var screenshot in _screenshotImages)
            screenshot.Dispose();
    }

    private void AdvanceScreenshot(object? sender, EventArgs e)
    {
        if (_screenshotImages.Count < 2)
            return;

        _screenshotIndex = (_screenshotIndex + 1) % _screenshotImages.Count;
        CurrentScreenshotImage = _screenshotImages[_screenshotIndex];
        for (var index = 0; index < ScreenshotIndicators.Count; index++)
            ScreenshotIndicators[index].IsActive = index == _screenshotIndex;
    }

    private static Bitmap? TryLoadBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveLauncherAssetPath(
        GameLibraryEntry entry,
        string fileName)
    {
        var gamesFolder = Directory.GetParent(entry.FolderPath);
        var portableRoot = gamesFolder?.Parent;

        if (portableRoot is null)
            return null;

        var launcherFolder = entry.LaunchTarget?.Launcher == LauncherKind.Custom &&
                             entry.LaunchTarget.RequiredLauncher is null
            ? "Emulator"
            : LauncherAssetCatalog.FolderName(entry.LaunchTarget?.BrandingLauncher ?? LauncherKind.Local);

        return Path.Combine(
            portableRoot.FullName,
            "System",
            "Assets",
            "Launchers",
            launcherFolder,
            fileName);
    }

    private static string? ResolvePlatformAssetPath(GameLibraryEntry entry, string fileName)
    {
        if (string.IsNullOrWhiteSpace(entry.Configuration.Game.PlatformLabel))
            return null;

        var gamesFolder = Directory.GetParent(entry.FolderPath);
        var portableRoot = gamesFolder?.Parent;
        if (portableRoot is null)
            return null;

        var assetsRoot = Path.Combine(portableRoot.FullName, "System", "Assets");
        return PlatformAssetCatalog.ResolveAsset(
            assetsRoot,
            entry.Configuration.Game.PlatformLabel,
            fileName);
    }
}
