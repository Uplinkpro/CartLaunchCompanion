using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Metadata;
using CartLaunchCompanion.Core.Portable;
using System.Text;

namespace CartLaunchCompanion.Configurator;

public sealed partial class MainWindow : Window
{
    private readonly EditorViewModel _viewModel = new();
    private readonly GameConfigurationValidator _validator = new();
    private readonly CartContentPathConverter _cartPathConverter = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string? _gameJsonPath;
    private bool _startupSetupShown;
    private PortablePaths? _portablePaths;
    private bool _loadingExistingGame;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.RefreshPreview();
        Closed += (_, _) =>
        {
            _viewModel.CollectionLogoPreview = null;
            _viewModel.CoverPreview = null;
            _viewModel.BackgroundPreview = null;
            _viewModel.LogoPreview = null;
            _viewModel.IconPreview = null;
            _httpClient.Dispose();
        };
        Opened += StartupOpened;
    }

    private async void StartupOpened(object? sender, EventArgs e)
    {
        if (_startupSetupShown) return;
        _startupSetupShown = true;
        _portablePaths = new PortablePathService().Discover(AppContext.BaseDirectory);
        _ = await MetadataProviderSettings.LoadAsync(_portablePaths);
        await LoadCollectionBrandingAsync();
        await LoadExistingGamesAsync();
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
            await RefreshArtworkPreviewsAsync();
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
        _viewModel.SelectedExistingGame = null;
        _viewModel.Reset();
    }

    private async void ExistingGameChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingExistingGame || _viewModel.SelectedExistingGame is null)
            return;
        await LoadGameConfigurationAsync(_viewModel.SelectedExistingGame.ConfigurationPath);
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
            await LoadGameConfigurationAsync(files[0].Path.LocalPath);
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

    private async void LocatePortableFileClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target })
            return;

        if (string.IsNullOrWhiteSpace(_gameJsonPath))
        {
            _viewModel.PathStatus = "Choose the Cart/Games configuration folder first, then locate the file.";
            _viewModel.Status = _viewModel.PathStatus;
            return;
        }

        var isRom = target.EndsWith("-rom", StringComparison.Ordinal);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = isRom ? "Choose a ROM, disc image, or game file" : "Choose a game, emulator, or companion executable",
            AllowMultiple = false,
            FileTypeFilter = isRom
                ? [new FilePickerFileType("ROMs and disc images") { Patterns = ["*.chd", "*.iso", "*.cue", "*.bin", "*.rvz", "*.rom", "*.zip", "*.7z", "*.nes", "*.sfc", "*.gba", "*.n64", "*.z64", "*.cso", "*.pbp", "*"] }]
                : [new FilePickerFileType("Applications and executables") { Patterns = ["*.exe", "*.bat", "*.cmd", "*.com", "*.sh", "*.AppImage", "*"] }]
        });
        if (files.Count == 0)
            return;

        var gameFolder = Path.GetDirectoryName(_gameJsonPath)!;
        var result = _cartPathConverter.Convert(gameFolder, files[0].Path.LocalPath);
        _viewModel.PathStatus = result.IsPortable
            ? $"PORTABLE · {result.DisplayPath} · {result.Message}"
            : $"NOT SAVED · {result.Message}";
        _viewModel.Status = _viewModel.PathStatus;
        if (!result.IsPortable)
            return;

        var configuration = _viewModel.Configuration;
        var workingDirectory = Path.GetDirectoryName(result.ConfiguredPath)?.Replace('\\', '/') ?? "";
        var processName = Path.GetFileNameWithoutExtension(result.ConfiguredPath);
        var argumentPath = result.ConfiguredPath;
        if (isRom)
        {
            var configuredWorkingDirectory = target.StartsWith("windows-", StringComparison.Ordinal)
                ? configuration.Launch.Windows.WorkingDirectory
                : configuration.Launch.Linux.WorkingDirectory;
            var argumentBase = string.IsNullOrWhiteSpace(configuredWorkingDirectory)
                ? gameFolder
                : new GamePathResolver().Resolve(gameFolder, configuredWorkingDirectory);
            argumentPath = Path.GetRelativePath(argumentBase, files[0].Path.LocalPath).Replace('\\', '/');
        }
        switch (target)
        {
            case "windows-game":
                configuration.Launch.Windows.Launcher = LauncherKind.Local;
                configuration.Launch.Windows.Executable = result.ConfiguredPath;
                configuration.Launch.Windows.WorkingDirectory = workingDirectory;
                configuration.Launch.Windows.ProcessName = processName;
                break;
            case "windows-emulator":
                configuration.Launch.Windows.Launcher = LauncherKind.Custom;
                configuration.Launch.Windows.Executable = result.ConfiguredPath;
                configuration.Launch.Windows.WorkingDirectory = workingDirectory;
                configuration.Launch.Windows.ProcessName = processName;
                break;
            case "windows-rom":
                configuration.Launch.Windows.Arguments = AppendQuotedArgument(
                    configuration.Launch.Windows.Arguments, argumentPath);
                break;
            case "windows-companion":
                configuration.Launch.Windows.CompanionApplication.Enabled = true;
                configuration.Launch.Windows.CompanionApplication.Executable = result.ConfiguredPath;
                configuration.Launch.Windows.CompanionApplication.WorkingDirectory = workingDirectory;
                break;
            case "linux-game":
                configuration.Launch.Linux.Enabled = true;
                configuration.Launch.Linux.Launcher = LauncherKind.Local;
                configuration.Launch.Linux.Executable = result.ConfiguredPath;
                configuration.Launch.Linux.WorkingDirectory = workingDirectory;
                configuration.Launch.Linux.ProcessName = processName;
                break;
            case "linux-emulator":
                configuration.Launch.Linux.Enabled = true;
                configuration.Launch.Linux.Launcher = LauncherKind.Custom;
                configuration.Launch.Linux.Executable = result.ConfiguredPath;
                configuration.Launch.Linux.WorkingDirectory = workingDirectory;
                configuration.Launch.Linux.ProcessName = processName;
                break;
            case "linux-rom":
                configuration.Launch.Linux.Arguments = AppendQuotedArgument(
                    configuration.Launch.Linux.Arguments, argumentPath);
                break;
            case "linux-companion":
                configuration.Launch.Linux.Enabled = true;
                configuration.Launch.Linux.CompanionApplication.Enabled = true;
                configuration.Launch.Linux.CompanionApplication.Executable = result.ConfiguredPath;
                configuration.Launch.Linux.CompanionApplication.WorkingDirectory = workingDirectory;
                break;
        }

        _viewModel.Configuration = GameConfigurationJson.Deserialize(
            GameConfigurationJson.Serialize(configuration));
        _viewModel.RefreshPreview();
    }

    private async void ChooseCollectionLogoClicked(object? sender, RoutedEventArgs e)
    {
        _portablePaths ??= new PortablePathService().Discover(AppContext.BaseDirectory);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a transparent collection header logo",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Collection logo")
                {
                    Patterns = ["*.png", "*.webp"]
                }
            ]
        });
        if (files.Count == 0)
            return;

        try
        {
            var source = files[0].Path.LocalPath;
            await using (var probe = File.OpenRead(source))
            {
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(probe);
                if (bitmap.PixelSize.Width < 360 || bitmap.PixelSize.Height < 112)
                    throw new InvalidDataException("The logo must be at least 360 × 112 pixels.");
            }

            var folderName = MakeSafeFolderName(_viewModel.Collection.Name);
            var destinationFolder = Path.Combine(_portablePaths.Assets, "Collections", folderName);
            Directory.CreateDirectory(destinationFolder);
            var extension = Path.GetExtension(source).ToLowerInvariant();
            var destination = GetAvailableLogoPath(destinationFolder, extension, source);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, destination, overwrite: false);

            _viewModel.Collection.Logo = Path.GetRelativePath(_portablePaths.Root, destination).Replace('\\', '/');
            await CollectionConfigurationJson.SaveAsync(_portablePaths.Config, _viewModel.Collection);
            LoadCollectionLogoPreview(destination);
            _viewModel.Status = "Collection logo copied into Cart/Assets and saved to Config/collection.json.";
        }
        catch (Exception ex)
        {
            _viewModel.CollectionLogoStatus = $"Logo not saved: {ex.Message}";
            _viewModel.Status = _viewModel.CollectionLogoStatus;
        }
    }

    private async Task LoadCollectionBrandingAsync()
    {
        if (_portablePaths is null)
            return;
        _viewModel.Collection = await CollectionConfigurationJson.LoadAsync(_portablePaths.Config);
        if (string.IsNullOrWhiteSpace(_viewModel.Collection.Logo))
            return;
        var path = Path.Combine(
            _portablePaths.Root,
            _viewModel.Collection.Logo.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path))
            LoadCollectionLogoPreview(path);
        else
            _viewModel.CollectionLogoStatus = $"Configured logo is missing: {_viewModel.Collection.Logo}";
    }

    private void LoadCollectionLogoPreview(string path)
    {
        var bitmap = new Avalonia.Media.Imaging.Bitmap(path);
        _viewModel.CollectionLogoPreview = bitmap;
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var ratio = width / (double)height;
        var shape = Math.Abs(ratio - (360d / 112d)) <= 0.08
            ? "Recommended header shape"
            : "Will fit, but may leave extra space";
        _viewModel.CollectionLogoStatus = $"{width} × {height} px · {shape} · {_viewModel.Collection.Logo}";
    }

    private static string MakeSafeFolderName(string name)
    {
        var source = string.IsNullOrWhiteSpace(name) ? "MySeries" : name;
        var builder = new StringBuilder();
        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }
        return builder.Length == 0 ? "MySeries" : builder.ToString();
    }

    private static string GetAvailableLogoPath(string folder, string extension, string source)
    {
        var first = Path.Combine(folder, "Logo" + extension);
        if (!File.Exists(first) || string.Equals(Path.GetFullPath(first), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            return first;
        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(folder, $"Logo-{index}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException("Too many logo versions exist in this collection folder.");
    }

    private static string AppendQuotedArgument(string existing, string path)
    {
        if (path.Contains('"', StringComparison.Ordinal))
            throw new InvalidDataException("Portable paths containing quotation marks are unsupported.");
        var quoted = $"\"{path}\"";
        return string.IsNullOrWhiteSpace(existing) ? quoted : $"{existing.Trim()} {quoted}";
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
            await RefreshExistingGamesAsync(_gameJsonPath);
            await RunArtworkAuditAsync();
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

    private async Task LoadExistingGamesAsync()
    {
        if (_portablePaths is null)
            return;
        await RefreshExistingGamesAsync();
        if (_viewModel.ExistingGames.Count == 0)
        {
            _viewModel.Status = "This cart has no saved games yet. A blank configuration is ready.";
            return;
        }

        _loadingExistingGame = true;
        try
        {
            _viewModel.SelectedExistingGame = _viewModel.ExistingGames[0];
            await LoadGameConfigurationAsync(_viewModel.ExistingGames[0].ConfigurationPath);
            _viewModel.Status = $"Loaded {_viewModel.ExistingGames[0].Name}. Choose another saved game from the top menu to edit it.";
        }
        finally
        {
            _loadingExistingGame = false;
        }
    }

    private async Task RefreshExistingGamesAsync(string? selectPath = null)
    {
        if (_portablePaths is null)
            return;
        var options = new List<ExistingGameOption>();
        if (Directory.Exists(_portablePaths.Games))
        {
            foreach (var folder in Directory.EnumerateDirectories(_portablePaths.Games).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var path = new[] { Path.Combine(folder, "game.json"), Path.Combine(folder, "Game.json") }
                    .FirstOrDefault(File.Exists);
                if (path is null || string.Equals(Path.GetFileName(folder), "Examples", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var configuration = await GameConfigurationJson.LoadAsync(path);
                    options.Add(new ExistingGameOption(
                        string.IsNullOrWhiteSpace(configuration.Game.Name) ? Path.GetFileName(folder) : configuration.Game.Name,
                        path));
                }
                catch
                {
                    options.Add(new ExistingGameOption(Path.GetFileName(folder) + " (invalid configuration)", path));
                }
            }
        }

        _loadingExistingGame = true;
        try
        {
            _viewModel.ExistingGames.Clear();
            foreach (var option in options)
                _viewModel.ExistingGames.Add(option);
            _viewModel.SelectedExistingGame = options.FirstOrDefault(option =>
                selectPath is not null && string.Equals(Path.GetFullPath(option.ConfigurationPath), Path.GetFullPath(selectPath), StringComparison.OrdinalIgnoreCase));
            _viewModel.NotifyExistingGamesChanged();
        }
        finally
        {
            _loadingExistingGame = false;
        }
    }

    private async Task LoadGameConfigurationAsync(string path)
    {
        _viewModel.Configuration = await GameConfigurationJson.LoadAsync(path);
        _gameJsonPath = path;
        _viewModel.FilePath = path;
        _viewModel.RefreshPreview();
        await RefreshArtworkPreviewsAsync();
        SetValidationStatus("Configuration opened.");
    }

    private async void RefreshArtworkClicked(object? sender, RoutedEventArgs e) =>
        await RefreshArtworkPreviewsAsync();

    private async Task RefreshArtworkPreviewsAsync()
    {
        _viewModel.CoverPreview = null;
        _viewModel.BackgroundPreview = null;
        _viewModel.LogoPreview = null;
        _viewModel.IconPreview = null;

        var configuration = _viewModel.Configuration;
        var folder = string.IsNullOrWhiteSpace(_gameJsonPath)
            ? null
            : Path.GetDirectoryName(_gameJsonPath);
        _viewModel.CoverPreview = await LoadArtworkPreviewAsync(folder, configuration.Artwork.Cover, configuration.Artwork.CoverUrl);
        _viewModel.BackgroundPreview = await LoadArtworkPreviewAsync(folder, configuration.Artwork.Background, configuration.Artwork.BackgroundUrl);
        _viewModel.LogoPreview = await LoadArtworkPreviewAsync(folder, configuration.Artwork.Logo, configuration.Artwork.LogoUrl);
        _viewModel.IconPreview = await LoadArtworkPreviewAsync(folder, configuration.Artwork.Icon, configuration.Artwork.IconUrl);
    }

    private async Task<Avalonia.Media.Imaging.Bitmap?> LoadArtworkPreviewAsync(
        string? gameFolder,
        string localPath,
        string remoteUrl)
    {
        if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            var resolved = new GamePathResolver().ResolveExistingWithAnyExtension(gameFolder, localPath);
            if (resolved is not null)
            {
                try { return new Avalonia.Media.Imaging.Bitmap(resolved); }
                catch { }
            }
        }

        return string.IsNullOrWhiteSpace(remoteUrl)
            ? null
            : await DownloadPreviewAsync(remoteUrl);
    }

    private async void EditorTabsChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedIndex: 6 })
            await RunArtworkAuditAsync();
    }

    private async void AuditArtworkClicked(object? sender, RoutedEventArgs e) =>
        await RunArtworkAuditAsync();

    private async Task RunArtworkAuditAsync()
    {
        _viewModel.ArtworkAuditResults.Clear();
        if (_portablePaths is null || !Directory.Exists(_portablePaths.Games))
        {
            _viewModel.ArtworkAuditSummary = "No Cart/Games folder is available to check.";
            return;
        }

        var resolver = new GamePathResolver();
        foreach (var option in _viewModel.ExistingGames)
        {
            try
            {
                var configuration = _gameJsonPath is not null &&
                                    string.Equals(Path.GetFullPath(option.ConfigurationPath), Path.GetFullPath(_gameJsonPath), StringComparison.OrdinalIgnoreCase)
                    ? _viewModel.Configuration
                    : await GameConfigurationJson.LoadAsync(option.ConfigurationPath);
                var folder = Path.GetDirectoryName(option.ConfigurationPath)!;
                AuditImage(option.Name, "Cover", folder, configuration.Artwork.Cover, resolver);
                AuditImage(option.Name, "Background", folder, configuration.Artwork.Background, resolver);
                AuditImage(option.Name, "Logo", folder, configuration.Artwork.Logo, resolver);
                AuditImage(option.Name, "Icon", folder, configuration.Artwork.Icon, resolver);
            }
            catch (Exception ex)
            {
                _viewModel.ArtworkAuditResults.Add(new ArtworkAuditItem(option.Name, "Configuration", "✕", "#FF6B72", ex.Message));
            }
        }

        var failures = _viewModel.ArtworkAuditResults.Count(item => item.Symbol == "✕");
        _viewModel.ArtworkAuditSummary = failures == 0
            ? $"✓ All {_viewModel.ArtworkAuditResults.Count} artwork files are present and readable."
            : $"✕ {failures} of {_viewModel.ArtworkAuditResults.Count} artwork checks need attention.";
    }

    private void AuditImage(string game, string asset, string folder, string configuredPath, GamePathResolver resolver)
    {
        var path = resolver.ResolveExistingWithAnyExtension(folder, configuredPath);
        if (path is null)
        {
            _viewModel.ArtworkAuditResults.Add(new ArtworkAuditItem(game, asset, "✕", "#FF6B72", "Missing"));
            return;
        }
        try
        {
            using var image = new Avalonia.Media.Imaging.Bitmap(path);
            if (image.PixelSize.Width <= 0 || image.PixelSize.Height <= 0)
                throw new InvalidDataException("Image dimensions are invalid.");
            _viewModel.ArtworkAuditResults.Add(new ArtworkAuditItem(
                game, asset, "✓", "#69DB8A", $"{image.PixelSize.Width} × {image.PixelSize.Height}"));
        }
        catch
        {
            _viewModel.ArtworkAuditResults.Add(new ArtworkAuditItem(game, asset, "✕", "#FF6B72", "Unreadable image"));
        }
    }

    private void SetGameFolder(string folderPath)
    {
        _gameJsonPath = Path.Combine(folderPath, "game.json");
        _viewModel.FilePath = _gameJsonPath;
        _viewModel.PathStatus = "Configuration folder selected. Locator buttons will save portable cart-relative paths.";
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
