using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed partial class LauncherDiscoveryDialog : Window
{
    public LauncherDiscoveryDialog() => InitializeComponent();

    public LauncherDiscoveryDialog(string gameName, IReadOnlyList<InstalledLauncherMatch> matches)
    {
        InitializeComponent();
        SummaryText.Text = $"CLC found {matches.Count} possible local match{(matches.Count == 1 ? "" : "es")} for {gameName}. Results come from installed manifests, Windows applications, and launcher-created shortcuts.";
        MatchesList.ItemsSource = matches;
        MatchesList.SelectedIndex = matches.Count == 1 ? 0 : -1;
    }

    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var match = MatchesList.SelectedItem as InstalledLauncherMatch;
        UseButton.IsEnabled = match is not null;
        SelectionHelp.Text = match is null
            ? "Nothing is changed until you confirm the selected result."
            : match.ValueKind == LauncherDiscoveryValueKind.Executable
                ? "Executables are accepted only when the selected file is stored on this cart."
                : $"This will apply the {match.Method} shown above; other game metadata will be preserved.";
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
    private void UseClicked(object? sender, RoutedEventArgs e) => Close(MatchesList.SelectedItem as InstalledLauncherMatch);
}
