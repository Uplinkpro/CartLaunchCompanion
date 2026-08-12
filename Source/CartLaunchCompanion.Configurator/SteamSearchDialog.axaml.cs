using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed partial class SteamSearchDialog : Window
{
    private readonly SteamCatalogService _catalogService;
    private readonly ConfiguratorSettings _settings;
    private bool _autoSearchStarted;

    public SteamCatalogMatch? SelectedMatch => ResultsList.SelectedItem as SteamCatalogMatch;

    public SteamSearchDialog()
    {
        _catalogService = null!;
        _settings = new ConfiguratorSettings();
        InitializeComponent();
    }

    public SteamSearchDialog(SteamCatalogService catalogService, ConfiguratorSettings settings, string initialQuery)
    {
        _catalogService = catalogService;
        _settings = settings;
        InitializeComponent();
        SearchBox.Text = initialQuery;
        Opened += DialogOpened;
    }

    private async void DialogOpened(object? sender, EventArgs e)
    {
        if (_autoSearchStarted) return;
        _autoSearchStarted = true;

        if (!string.IsNullOrWhiteSpace(SearchBox.Text) &&
            SearchBox.Text.Trim().Length >= 2 &&
            (!string.IsNullOrWhiteSpace(_settings.SteamWebApiKey) ||
             uint.TryParse(SearchBox.Text.Trim(), out _)))
        {
            await SearchAsync();
        }
        else
        {
            SearchBox.Focus();
        }
    }

    private async void SearchClicked(object? sender, RoutedEventArgs e) => await SearchAsync();
    private async void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var key = _settings.SteamWebApiKey.Trim();
        if (query.Length < 2) { StatusText.Text = "Enter at least two characters."; return; }
        if (key.Length == 0 && !uint.TryParse(query, out _)) { StatusText.Text = "Add a Steam Web API key in Settings before searching by name."; return; }

        StatusText.Text = "Searching Steam…";
        ResultsList.ItemsSource = null;
        UseButton.IsEnabled = false;
        try
        {
            var matches = await _catalogService.SearchAsync(query, key, _settings.SteamGridDbApiKey);
            ResultsList.ItemsSource = matches;
            StatusText.Text = matches.Count == 0 ? "No close matches found." : $"Found {matches.Count} likely matches. Choose the correct edition.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Steam search failed: " + ex.Message;
        }
    }

    private void ResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        var match = SelectedMatch;
        RefreshUseButton();
    }

    private void RefreshUseButton()
    {
        UseButton.IsEnabled = SelectedMatch is not null;
    }

    private void UseClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedMatch is not { } match) return;
        Close(match);
    }
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
