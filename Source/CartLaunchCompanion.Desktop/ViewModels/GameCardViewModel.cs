using Avalonia.Media.Imaging;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Library;
using CommunityToolkit.Mvvm.Input;
using CartLaunchCompanion.Desktop.Themes;

namespace CartLaunchCompanion.Desktop.ViewModels;

public sealed class GameCardViewModel : ViewModelBase, IDisposable
{
    private readonly Action<GameCardViewModel> _openGame;

    public GameCardViewModel(
        GameLibraryEntry entry,
        Action<GameCardViewModel> openGame)
    {
        Entry = entry;
        _openGame = openGame;

        OpenCommand = new RelayCommand(
            () => _openGame(this));

        CoverImage = TryLoadBitmap(entry.CoverPath);
        BackgroundImage = TryLoadBitmap(entry.BackgroundPath);
        LogoImage = TryLoadBitmap(entry.LogoPath);
    }

    public GameLibraryEntry Entry { get; }

    public string Name => Entry.Configuration.Game.Name;
    public string Description => Entry.Configuration.Game.Description;
    public string Developer => Entry.Configuration.Game.Developer;
    public string Publisher => Entry.Configuration.Game.Publisher;
    public string Genre => Entry.Configuration.Game.Genre;
    public string ReleaseDate => Entry.Configuration.Game.ReleaseDate;
    public string Players => Entry.Configuration.Game.Players;

    public string Launcher =>
        Entry.LaunchTarget?.Launcher.ToString()
        ?? "Unavailable";

    public LauncherKind LauncherKind =>
        Entry.LaunchTarget?.Launcher
        ?? LauncherKind.Custom;

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
    public Bitmap? BackgroundImage { get; }
    public Bitmap? LogoImage { get; }

    public bool HasCover => CoverImage is not null;
    public bool HasNoCover => CoverImage is null;
    public bool HasBackground => BackgroundImage is not null;
    public bool HasLogo => LogoImage is not null;
    public bool HasTrailer => Entry.TrailerPath is not null;
    public bool IsLaunchable => Entry.IsLaunchable;

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

    public void Dispose()
    {
        CoverImage?.Dispose();
        BackgroundImage?.Dispose();
        LogoImage?.Dispose();
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
}
