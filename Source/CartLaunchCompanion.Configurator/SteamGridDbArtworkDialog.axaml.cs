using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed record SteamGridDbArtworkChoice(
    SteamGridDbAssetKind Kind,
    SteamGridDbAsset Asset);

public sealed partial class SteamGridDbArtworkDialog : Window
{
    private readonly SteamGridDbArtworkService _service = null!;
    private readonly long _gameId;
    private readonly string _apiKey = "";
    private readonly Dictionary<SteamGridDbAssetKind, IReadOnlyList<SteamGridDbAsset>> _loaded = [];
    private readonly Dictionary<SteamGridDbAssetKind, SteamGridDbAsset> _selected = [];
    private bool _returningSelections;

    public SteamGridDbArtworkDialog() => InitializeComponent();

    public SteamGridDbArtworkDialog(
        SteamGridDbArtworkService service,
        long gameId,
        string apiKey)
    {
        _service = service;
        _gameId = gameId;
        _apiKey = apiKey;
        InitializeComponent();
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => DisposeUnusedAssets();
    }

    private async void KindChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var kind = CurrentKind;
        UseButton.IsEnabled = false;
        AssetsList.ItemsSource = null;

        try
        {
            if (!_loaded.TryGetValue(kind, out var items))
            {
                StatusText.Text = $"Loading safe {KindName(kind)} choices…";
                items = await _service.GetAssetsAsync(_gameId, kind, _apiKey);
                _loaded[kind] = items;
            }

            AssetsList.ItemsSource = items;
            if (_selected.TryGetValue(kind, out var selected))
                AssetsList.SelectedItem = selected;

            StatusText.Text = items.Count == 0
                ? $"No safe static {KindName(kind)} artwork was found. You can continue to the next type."
                : $"Found {items.Count} {KindName(kind)} choices.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not load SteamGridDB artwork: " + ex.Message;
        }
    }

    private SteamGridDbAssetKind CurrentKind =>
        (SteamGridDbAssetKind)Math.Max(0, KindTabs.SelectedIndex);

    private void AssetSelected(object? sender, SelectionChangedEventArgs e) =>
        UseButton.IsEnabled = AssetsList.SelectedItem is SteamGridDbAsset;

    private void UseClicked(object? sender, RoutedEventArgs e)
    {
        if (AssetsList.SelectedItem is not SteamGridDbAsset asset)
            return;

        var kind = CurrentKind;
        _selected[kind] = asset;
        SaveButton.IsEnabled = true;

        if (KindTabs.SelectedIndex < KindTabs.ItemCount - 1)
        {
            KindTabs.SelectedIndex++;
            return;
        }

        StatusText.Text = "Icon selected. Save and exit to apply all selected artwork.";
        UseButton.IsEnabled = false;
    }

    private void SaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0)
            return;

        _returningSelections = true;
        Close(_selected
            .OrderBy(item => item.Key)
            .Select(item => new SteamGridDbArtworkChoice(item.Key, item.Value))
            .ToArray());
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void DisposeUnusedAssets()
    {
        var retained = _returningSelections
            ? _selected.Values.ToHashSet(ReferenceEqualityComparer.Instance)
            : [];

        foreach (var asset in _loaded.Values.SelectMany(items => items))
            if (!retained.Contains(asset))
                asset.Dispose();
    }

    private static string KindName(SteamGridDbAssetKind kind) =>
        kind.ToString().ToLowerInvariant();
}
