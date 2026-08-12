using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed partial class SteamGridDbArtworkDialog : Window
{
    private readonly SteamGridDbArtworkService _service = null!;
    private readonly long _gameId;
    private readonly string _apiKey = "";
    private readonly List<SteamGridDbAsset> _loaded = [];
    public SteamGridDbArtworkDialog() => InitializeComponent();
    public SteamGridDbArtworkDialog(SteamGridDbArtworkService service, long gameId, string apiKey)
    { _service = service; _gameId = gameId; _apiKey = apiKey; InitializeComponent(); Opened += async (_, _) => await LoadAsync(); Closed += (_, _) => { foreach (var item in _loaded) if (!ReferenceEquals(item, AssetsList.SelectedItem)) item.Dispose(); }; }
    private async void KindChanged(object? sender, SelectionChangedEventArgs e) { if (IsLoaded) await LoadAsync(); }
    private async Task LoadAsync()
    {
        StatusText.Text = "Loading safe artwork choices…"; AssetsList.ItemsSource = null; UseButton.IsEnabled = false;
        try { var items = await _service.GetAssetsAsync(_gameId, (SteamGridDbAssetKind)Math.Max(0, KindTabs.SelectedIndex), _apiKey); _loaded.AddRange(items); AssetsList.ItemsSource = items; StatusText.Text = items.Count == 0 ? "No safe static artwork was found for this type." : $"Found {items.Count} choices."; }
        catch (Exception ex) { StatusText.Text = "Could not load SteamGridDB artwork: " + ex.Message; }
    }
    private void AssetSelected(object? sender, SelectionChangedEventArgs e) => UseButton.IsEnabled = AssetsList.SelectedItem is SteamGridDbAsset;
    private void UseClicked(object? sender, RoutedEventArgs e) { if (AssetsList.SelectedItem is SteamGridDbAsset asset) Close(((SteamGridDbAssetKind)Math.Max(0, KindTabs.SelectedIndex), asset)); }
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
