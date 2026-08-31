using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace CartLaunchCompanion.Configurator;

public sealed partial class LauncherIdGuideDialog : Window
{
    private static readonly IReadOnlyList<LauncherIdGuideEntry> Guides =
    [
        new(
            "Steam",
            "Steam exposes a stable numeric App ID and is the simplest launcher to configure.",
            "Steam App ID",
            "Digits only, for example 620",
            "Open the game's Steam store page. The number after /app/ in the address is the App ID. The Configurator's Find on Steam button can fill this automatically.",
            "Use the Steam App ID. CLC launches steam://rungameid/{id}.",
            "Stable for a specific Steam product, but demos, test branches, remasters, and separate editions can have different IDs.",
            "https://partner.steamgames.com/doc/store/application",
            [
                new("Portal 2", "620"),
                new("Grand Theft Auto IV: Complete Edition", "12210"),
                new("Grand Theft Auto V Enhanced", "3240220")
            ]),
        new(
            "Xbox / Microsoft Store",
            "CLC needs the installed application's AUMID, not the public Microsoft Store product code.",
            "Xbox App ID",
            "PackageFamilyName!ApplicationId",
            "On the target PC, open shell:AppsFolder and inspect the installed game's AppUserModelId, or use Microsoft's PowerShell AUMID discovery method linked below.",
            "Use the installed AUMID. Because it comes from Windows, automatic local discovery is safer than copying a web list.",
            "AUMIDs are intended to persist across package updates, but they cannot be determined reliably from a game's display name alone.",
            "https://learn.microsoft.com/windows/configuration/store/find-aumid",
            [
                new("Format example", "Publisher.Package_hash!Game"),
                new("Launch form used by CLC", "shell:AppsFolder\\PackageFamilyName!ApplicationId")
            ]),
        new(
            "Epic Games",
            "Epic uses an internal AppName. It is often not the title shown in the store.",
            "Epic app name",
            "AppName from an installed .item manifest",
            "Install the game, then inspect its .item file under Epic's ProgramData manifest folder and copy the AppName value. A desktop shortcut can also preserve the complete Epic launch URI.",
            "Use AppName when known. Otherwise paste the complete shortcut target into Launch URI; do not guess from the display title.",
            "Epic identifiers can differ by offer or edition. CatalogItemId and AppName are different values; CLC's field expects AppName.",
            "https://www.epicgames.com/help/epic-games-store-c-202300000001639",
            [
                new("Manifest key", "\"AppName\": \"…\""),
                new("URI form", "com.epicgames.launcher://apps/{AppName}?action=launch&silent=true")
            ]),
        new(
            "GOG Galaxy",
            "GOG has numeric product IDs, but CLC currently launches GOG titles through an executable or a saved launch URI.",
            "GOG game ID (reference only)",
            "Numeric product ID; executable or URI is still required for launch",
            "Create a desktop shortcut in GOG Galaxy and inspect its target, or locate the installed game's executable on the cart. Galaxy metadata can show a product ID, but that ID alone is not a complete launch target in CLC.",
            "Prefer the cart-local executable. Use Launch URI when the game must pass through Galaxy.",
            "Product IDs are useful metadata, but a hand-maintained list is not enough to guarantee launch behavior or select the correct edition.",
            "https://support.gog.com/hc/en-us/categories/201400969",
            [
                new("Direct launch", "Executable: Games\\…\\Game.exe"),
                new("Galaxy launch", "Paste the complete URI from the installed shortcut")
            ]),
        new(
            "Ubisoft Connect",
            "Ubisoft shortcuts contain the numeric game ID CLC needs.",
            "Ubisoft game ID",
            "Digits used inside uplay://launch/{id}/0",
            "In Ubisoft Connect, create a desktop shortcut for the installed game. Open the shortcut's Properties and copy the digits between uplay://launch/ and /0.",
            "Use the numeric ID or paste the complete uplay URI into Launch URI. The complete URI is safest for unusual editions.",
            "IDs are edition-specific and Ubisoft does not provide a complete public consumer catalog. Read the installed shortcut rather than guessing.",
            "https://www.ubisoft.com/help/connectivity-and-performance",
            [
                new("Assassin's Creed IV: Black Flag", "565  →  uplay://launch/565/0"),
                new("Assassin's Creed Valhalla", "13504  →  uplay://launch/13504/0")
            ]),
        new(
            "Rockstar Games Launcher",
            "Rockstar does not expose a dependable public game-ID catalog for this use case. CLC does not launch from the Rockstar game ID alone.",
            "Executable or Launch URI",
            "Cart-local executable path or complete installed shortcut URI",
            "Create a shortcut from the installed game and inspect its target, or locate the game's launcher executable such as PlayGTAV.exe from the cart.",
            "Use the executable when the game files are on the cart. Use the exact URI from a known-working shortcut when Rockstar must broker launch.",
            "Names such as gta5 seen in third-party lists are not a supported universal contract. Avoid building carts around an unverified copied ID.",
            "https://support.rockstargames.com/categories/200013306",
            [
                new("Grand Theft Auto V", "Executable example: Games\\…\\PlayGTAV.exe"),
                new("Launcher shortcut", "Paste its complete target into Launch URI")
            ]),
        new(
            "Amazon Games",
            "Amazon does not publish a stable, complete consumer game-ID list. CLC launches these games through an executable or captured URI.",
            "Executable or Launch URI",
            "Cart-local executable path or complete installed shortcut URI",
            "Use Amazon Games to create or locate the installed game's shortcut, then inspect its target. If the title launches directly, locate its executable on the cart.",
            "Prefer a cart-local executable. Preserve the complete shortcut URI when Amazon Games is required for ownership checks.",
            "An Amazon catalog identifier may not be the same value used by the installed launch shortcut, so web lists are unsafe as the primary source.",
            "https://www.amazongames.com/en-us/support",
            [
                new("Direct launch", "Executable: Games\\…\\Game.exe"),
                new("Launcher-managed", "Paste the complete installed shortcut target")
            ]),
        new(
            "EA app",
            "EA is available as a launcher choice, but EA does not expose a simple public App-ID catalog comparable to Steam.",
            "Executable or Launch URI",
            "Cart-local executable path or complete EA-created shortcut URI",
            "Install the game through EA app, create a desktop shortcut when available, and inspect its target. Otherwise locate the game executable on the cart and let EA app perform any required ownership check.",
            "Use the executable or the exact URI from a working EA shortcut. Do not copy an Origin offer ID from an old list and assume the current EA app accepts it.",
            "EA offer IDs and legacy Origin URI forms are implementation details and can vary by edition. CLC intentionally does not ask users to guess one.",
            "https://help.ea.com/en/articles/platforms/download-and-play-ea-app-games/",
            [
                new("Recommended", "Executable: Games\\…\\Game.exe"),
                new("Launcher-managed", "Paste the complete target from an EA-created shortcut")
            ])
    ];

    private LauncherIdGuideEntry? _selected;

    public LauncherIdGuideDialog()
    {
        InitializeComponent();
        LauncherSelector.ItemsSource = Guides;
        LauncherSelector.SelectedIndex = 0;
    }

    private void LauncherSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selected = LauncherSelector.SelectedItem as LauncherIdGuideEntry;
        GuidePanel.DataContext = _selected;
        DocumentationButton.IsEnabled = _selected is not null;
        RefreshExamples();
    }

    private void ExampleSearchChanged(object? sender, TextChangedEventArgs e) => RefreshExamples();

    private void RefreshExamples()
    {
        var query = ExampleSearch.Text?.Trim() ?? "";
        var examples = _selected?.Examples
            .Where(example => query.Length == 0 ||
                              example.Game.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              example.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        ExamplesItems.ItemsSource = examples;
        NoExamplesText.IsVisible = examples.Length == 0;
    }

    private void OpenDocumentationClicked(object? sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        Process.Start(new ProcessStartInfo { FileName = _selected.HelpUrl, UseShellExecute = true });
    }

    private void DoneClicked(object? sender, RoutedEventArgs e) => Close();
}

public sealed record LauncherIdGuideExample(string Game, string Value);

public sealed record LauncherIdGuideEntry(
    string Name,
    string Summary,
    string Setting,
    string Format,
    string Discovery,
    string Recommendation,
    string Reliability,
    string HelpUrl,
    IReadOnlyList<LauncherIdGuideExample> Examples);
