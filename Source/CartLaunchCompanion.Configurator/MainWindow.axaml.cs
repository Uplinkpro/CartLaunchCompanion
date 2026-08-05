using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Metadata;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Configurator;

public sealed partial class MainWindow : Window
{
    private readonly EditorViewModel _viewModel = new();
    private readonly GameConfigurationValidator _validator = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string? _gameJsonPath;
    private bool _startupSetupShown;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.RefreshPreview();
        Closed += (_, _) => _httpClient.Dispose();
        Opened += StartupOpened;
    }

    private async void StartupOpened(object? sender, EventArgs e)
    {
        if (_startupSetupShown) return;
        _startupSetupShown = true;
        var portablePaths = new PortablePathService().Discover(AppContext.BaseDirectory);
        _ = await MetadataProviderSettings.LoadAsync(portablePaths);
        var settings = await ConfiguratorSettings.LoadAsync();
        if (!settings.SetupCompleted)
            await new ApiSetupDialog(settings).ShowDialog<bool>(this);
    }

    private async void SettingsClicked(object? sender, RoutedEventArgs e)
    {
        var settings = await ConfiguratorSettings.LoadAsync();
        await new ApiSetupDialog(settings).ShowDialog<bool>(this);
    }

    private async void FindOnSteamClicked(object? sender, RoutedEventArgs e)
    {
        var settings = await ConfiguratorSettings.LoadAsync();
        var dialog = new SteamSearchDialog(
            new SteamCatalogService(_httpClient),
            settings,
            _viewModel.Configuration.Game.Name);
        var match = await dialog.ShowDialog<SteamCatalogMatch?>(this);
        if (match is null) return;

        _viewModel.Status = $"Loading metadata for {match.Name}…";
        var configuration = _viewModel.Configuration;
        configuration.Game.Name = match.Name;
        configuration.Artwork.SteamMetadataId = match.AppId.ToString();
        _viewModel.ArtworkPreview = match.Artwork;
        _viewModel.ArtworkPreviewTitle = $"{match.Name} · Steam App ID {match.AppId}";
        if (configuration.Launch.Windows.Launcher == LauncherKind.Steam)
            configuration.Launch.Windows.SteamId = match.AppId.ToString();
        if (configuration.Launch.Linux.Enabled && configuration.Launch.Linux.Launcher == LauncherKind.Steam)
            configuration.Launch.Linux.SteamId = match.AppId.ToString();

        var downloadArtwork = configuration.Artwork.DownloadMissingArtwork;
        configuration.Artwork.DownloadMissingArtwork = false;
        try
        {
            var paths = new PortablePathService().Discover(AppContext.BaseDirectory);
            var service = new SteamMetadataService(_httpClient, new GamePathResolver());
            var scratchFolder = Path.Combine(paths.Cache, "ConfiguratorPreview", match.AppId.ToString());
            var result = await service.EnrichAsync(scratchFolder, configuration, paths);
            var openMetadata = new OpenGameMetadataResult();
            if (HasMissingTextMetadata(configuration))
                openMetadata = await new OpenGameMetadataService(_httpClient)
                    .FillMissingAsync(configuration, match.AppId.ToString());
            if (_viewModel.ArtworkPreview is null && !string.IsNullOrWhiteSpace(openMetadata.CoverUrl))
                _viewModel.ArtworkPreview = await DownloadPreviewAsync(openMetadata.CoverUrl);
            configuration.Artwork.DownloadMissingArtwork = downloadArtwork;
            // Replace the bound object so Avalonia refreshes fields changed by metadata services.
            _viewModel.Configuration = GameConfigurationJson.Deserialize(
                GameConfigurationJson.Serialize(configuration));
            _viewModel.RefreshPreview();
            _viewModel.Status = openMetadata.UsedAny
                ? $"Matched {match.Name}. Missing details were filled from PCGamingWiki and Wikipedia."
                : HasMissingTextMetadata(configuration)
                ? $"Matched {match.Name}, but some descriptive metadata was unavailable."
                : result.Warnings.Count == 0
                ? $"Matched {match.Name}. Available Steam metadata was filled in."
                : $"Matched {match.Name}. Some metadata was unavailable: {string.Join(" ", result.Warnings)}";
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"The Steam match was selected, but metadata could not be loaded: {ex.Message}";
        }
        finally
        {
            configuration.Artwork.DownloadMissingArtwork = downloadArtwork;
        }
    }

    private static bool HasMissingTextMetadata(GameConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration.Game.Description) ||
        string.IsNullOrWhiteSpace(configuration.Game.Developer) ||
        string.IsNullOrWhiteSpace(configuration.Game.Publisher) ||
        string.IsNullOrWhiteSpace(configuration.Game.Genre) ||
        string.IsNullOrWhiteSpace(configuration.Game.ReleaseDate);

    private async Task<Avalonia.Media.Imaging.Bitmap?> DownloadPreviewAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/1.0 metadata-configurator");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch { return null; }
    }

    private void NewClicked(object? sender, RoutedEventArgs e)
    {
        _gameJsonPath = null;
        _viewModel.Reset();
    }

    private async void OpenClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open an existing game.json",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Cart Launch game configuration")
                {
                    Patterns = ["game.json", "*.json"]
                }
            ]
        });

        if (files.Count == 0)
            return;

        try
        {
            var path = files[0].Path.LocalPath;
            _viewModel.Configuration = await GameConfigurationJson.LoadAsync(path);
            _gameJsonPath = path;
            _viewModel.FilePath = path;
            _viewModel.RefreshPreview();
            SetValidationStatus("Configuration opened.");
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Could not open this file: {ex.Message}";
            _viewModel.HasErrors = true;
        }
    }

    private async void ChooseFolderClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the game folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        SetGameFolder(folders[0].Path.LocalPath);
    }

    private void ValidateClicked(object? sender, RoutedEventArgs e) => SetValidationStatus();

    private void PreviewClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.RefreshPreview();
        _viewModel.Status = "JSON preview refreshed.";
    }

    private async void SaveClicked(object? sender, RoutedEventArgs e)
    {
        var validation = _validator.Validate(_viewModel.Configuration);
        if (!validation.IsValid)
        {
            ShowErrors(validation);
            return;
        }

        if (string.IsNullOrWhiteSpace(_gameJsonPath))
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose where to create this game folder",
                AllowMultiple = false
            });

            if (folders.Count == 0)
            {
                _viewModel.Status = "Save cancelled. Choose a game folder when you are ready.";
                return;
            }

            SetGameFolder(folders[0].Path.LocalPath);
        }

        try
        {
            var gameFolder = Path.GetDirectoryName(_gameJsonPath!)!;
            Directory.CreateDirectory(Path.Combine(gameFolder, "Artwork"));
            Directory.CreateDirectory(Path.Combine(gameFolder, "Media"));
            await GameConfigurationJson.SaveAsync(_gameJsonPath!, _viewModel.Configuration);
            _viewModel.RefreshPreview();
            _viewModel.Status = "Saved successfully. This game folder is ready for Cart Launch Companion.";
            _viewModel.HasErrors = false;
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Could not save: {ex.Message}";
            _viewModel.HasErrors = true;
        }
    }

    private void SetGameFolder(string folderPath)
    {
        _gameJsonPath = Path.Combine(folderPath, "game.json");
        _viewModel.FilePath = _gameJsonPath;
        _viewModel.Status = File.Exists(_gameJsonPath)
            ? "This folder already has a game.json. Use Open if you want to edit it first."
            : "Game folder selected. Complete the required fields, then save.";
    }

    private void SetValidationStatus(string? successPrefix = null)
    {
        var validation = _validator.Validate(_viewModel.Configuration);
        _viewModel.RefreshPreview();
        if (!validation.IsValid)
        {
            ShowErrors(validation);
            return;
        }

        _viewModel.HasErrors = false;
        _viewModel.Status = string.IsNullOrWhiteSpace(successPrefix)
            ? "Everything required is present. This configuration is ready to save."
            : $"{successPrefix} Everything required is present.";
    }

    private void ShowErrors(ConfigurationValidationResult validation)
    {
        _viewModel.HasErrors = true;
        _viewModel.Status = "Please fix: " + string.Join("  •  ", validation.Errors.Select(issue => issue.Message));
    }
}
