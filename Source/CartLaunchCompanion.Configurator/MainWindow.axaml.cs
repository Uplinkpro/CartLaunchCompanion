using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
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
    private readonly HostLauncherDetectionService _hostLauncherDetector = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _downloadHttpClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    private string? _gameJsonPath;
    private bool _startupSetupShown;
    private PortablePaths? _portablePaths;
    private bool _loadingExistingGame;
    private CollectionGameEditor? _draggedCollectionGame;

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
            _viewModel.HeroPreview = null;
            _viewModel.LogoPreview = null;
            _viewModel.IconPreview = null;
            _httpClient.Dispose();
            _downloadHttpClient.Dispose();
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
        await LoadCollectionOrganizerAsync();
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
        if (match.AppId > 0)
            configuration.Artwork.SteamMetadataId = match.AppId.ToString();
        if (match.SteamGridDbGameId is not null)
            configuration.Artwork.SteamGridDbGameId = match.SteamGridDbGameId;
        _viewModel.ArtworkPreview = match.Artwork;
        _viewModel.ArtworkPreviewTitle = $"{match.Name} · Steam App ID {match.AppId}";
        if (match.AppId > 0 && configuration.Launch.Windows.Launcher == LauncherKind.Steam)
            configuration.Launch.Windows.SteamId = match.AppId.ToString();
        if (match.AppId > 0 && configuration.Launch.Linux.Enabled && configuration.Launch.Linux.Launcher == LauncherKind.Steam)
            configuration.Launch.Linux.SteamId = match.AppId.ToString();

        var downloadArtwork = configuration.Artwork.DownloadMissingArtwork;
        configuration.Artwork.DownloadMissingArtwork = false;
        try
        {
            var paths = new PortablePathService().Discover(AppContext.BaseDirectory);
            var service = new SteamMetadataService(_httpClient, new GamePathResolver());
            var scratchFolder = Path.Combine(paths.Cache, "ConfiguratorPreview", (match.SteamGridDbGameId ?? match.AppId).ToString());
            var result = await service.EnrichAsync(scratchFolder, configuration, paths);
            var openMetadata = new OpenGameMetadataResult();
            if (match.AppId > 0 && HasMissingTextMetadata(configuration))
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

        var validCategory = target.EndsWith("-game", StringComparison.Ordinal)
            ? CartContentPathConverter.IsGameContentCategory(result.Category)
            : target.EndsWith("-emulator", StringComparison.Ordinal)
                ? CartContentPathConverter.IsEmulatorCategory(result.Category)
                : target.EndsWith("-rom", StringComparison.Ordinal)
                    ? CartContentPathConverter.IsRomCategory(result.Category)
                    : result.Category is "Cart" or "Games" or "Emulators";
        if (!validCategory)
        {
            var expected = target.EndsWith("-game", StringComparison.Ordinal)
                ? "Games or a launcher-managed library on this cart"
                : target.EndsWith("-emulator", StringComparison.Ordinal)
                    ? "Emulators"
                    : target.EndsWith("-rom", StringComparison.Ordinal)
                        ? "Roms"
                        : "Cart, Games, or Emulators";
            _viewModel.PathStatus = $"NOT SAVED · Choose this file from the cart's {expected} folder.";
            _viewModel.Status = _viewModel.PathStatus;
            return;
        }

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

    private void VerifySelectedLauncherClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string platform })
            return;
        var isWindows = platform == "windows";
        var launcher = isWindows
            ? _viewModel.Configuration.Launch.Windows.Launcher
            : _viewModel.Configuration.Launch.Linux.Launcher;
        var result = _hostLauncherDetector.Detect(
            launcher,
            isWindows ? CartLaunchCompanion.Core.Platform.PlatformKind.Windows : CartLaunchCompanion.Core.Platform.PlatformKind.Linux);
        var message = result.Found
            ? $"✓ {result.Message}{(string.IsNullOrWhiteSpace(result.Location) ? "" : $" Found at {result.Location}")}"
            : $"✕ {result.Message}";
        if (isWindows) _viewModel.WindowsLauncherStatus = message;
        else _viewModel.LinuxLauncherStatus = message;
        _viewModel.Status = message;
    }

    private async void LocateLauncherFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string platform })
            return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Locate the selected launcher on this computer",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        var location = folders[0].Path.LocalPath;
        var message = $"✓ Launcher folder confirmed at {location}. This host-only location will not be written into game.json.";
        if (platform == "windows") _viewModel.WindowsLauncherStatus = message;
        else _viewModel.LinuxLauncherStatus = message;
        _viewModel.Status = message;
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

    private async Task LoadCollectionOrganizerAsync()
    {
        foreach (var game in _viewModel.UnassignedCollectionGames.Concat(_viewModel.CollectionShelves.SelectMany(shelf => shelf.Games)))
            game.Dispose();
        _viewModel.UnassignedCollectionGames.Clear();
        _viewModel.CollectionShelves.Clear();

        foreach (var shelf in _viewModel.Collection.Shelves
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                     .OrderBy(item => item.Order))
            _viewModel.CollectionShelves.Add(new CollectionShelfEditor { Name = shelf.Name.Trim() });

        foreach (var option in _viewModel.ExistingGames)
        {
            try
            {
                var configuration = await GameConfigurationJson.LoadAsync(option.ConfigurationPath);
                var editor = new CollectionGameEditor { Name = option.Name, ConfigurationPath = option.ConfigurationPath };
                var folder = Path.GetDirectoryName(option.ConfigurationPath)!;
                var cover = new GamePathResolver().ResolveExistingWithAnyExtension(folder, configuration.Artwork.Cover);
                if (cover is not null)
                {
                    try { editor.CoverPreview = new Bitmap(cover); } catch { }
                }

                var shelfName = configuration.Collection.Shelf.Trim();
                var shelf = _viewModel.CollectionShelves.FirstOrDefault(item =>
                    string.Equals(item.Name, shelfName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(shelfName) && shelf is null)
                {
                    shelf = new CollectionShelfEditor { Name = shelfName };
                    _viewModel.CollectionShelves.Add(shelf);
                }
                if (shelf is null)
                    _viewModel.UnassignedCollectionGames.Add(editor);
                else
                    shelf.Games.Add(editor);
            }
            catch { }
        }

        foreach (var shelf in _viewModel.CollectionShelves)
        {
            var ordered = new List<(CollectionGameEditor Editor, int Order)>();
            foreach (var editor in shelf.Games)
            {
                var configuration = await GameConfigurationJson.LoadAsync(editor.ConfigurationPath);
                ordered.Add((editor, configuration.Collection.Order));
            }
            shelf.Games.Clear();
            foreach (var item in ordered.OrderBy(item => item.Order).ThenBy(item => item.Editor.Name, StringComparer.OrdinalIgnoreCase))
                shelf.Games.Add(item.Editor);
        }
    }

    private async void CollectionGamePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CollectionGameEditor game } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _draggedCollectionGame = game;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(game.ConfigurationPath));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        _draggedCollectionGame = null;
    }

    private static void CollectionDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void CollectionShelfDrop(object? sender, DragEventArgs e)
    {
        if (_draggedCollectionGame is null || sender is not Control { DataContext: CollectionShelfEditor shelf }) return;
        MoveCollectionGame(_draggedCollectionGame, shelf.Games);
        e.Handled = true;
    }

    private void CollectionUnassignedDrop(object? sender, DragEventArgs e)
    {
        if (_draggedCollectionGame is null) return;
        MoveCollectionGame(_draggedCollectionGame, _viewModel.UnassignedCollectionGames);
        e.Handled = true;
    }

    private void CollectionGameDrop(object? sender, DragEventArgs e)
    {
        if (_draggedCollectionGame is null || sender is not Control { DataContext: CollectionGameEditor target } || ReferenceEquals(_draggedCollectionGame, target)) return;
        var destination = FindCollection(_viewModel.CollectionShelves.Select(shelf => shelf.Games).Append(_viewModel.UnassignedCollectionGames), target);
        if (destination is null) return;
        RemoveCollectionGame(_draggedCollectionGame);
        destination.Insert(destination.IndexOf(target), _draggedCollectionGame);
        _viewModel.CollectionLayoutStatus = $"Moved {_draggedCollectionGame.Name}. Save the layout when it looks right.";
        e.Handled = true;
    }

    private void MoveCollectionGame(CollectionGameEditor game, System.Collections.ObjectModel.ObservableCollection<CollectionGameEditor> destination)
    {
        RemoveCollectionGame(game);
        destination.Add(game);
        _viewModel.CollectionLayoutStatus = $"Moved {game.Name}. Save the layout when it looks right.";
    }

    private void RemoveCollectionGame(CollectionGameEditor game)
    {
        _viewModel.UnassignedCollectionGames.Remove(game);
        foreach (var shelf in _viewModel.CollectionShelves) shelf.Games.Remove(game);
    }

    private static System.Collections.ObjectModel.ObservableCollection<CollectionGameEditor>? FindCollection(
        IEnumerable<System.Collections.ObjectModel.ObservableCollection<CollectionGameEditor>> collections,
        CollectionGameEditor game) => collections.FirstOrDefault(items => items.Contains(game));

    private void AddCollectionShelfClicked(object? sender, RoutedEventArgs e)
    {
        var name = _viewModel.NewShelfName.Trim();
        if (string.IsNullOrWhiteSpace(name)) { _viewModel.CollectionLayoutStatus = "Enter a shelf name first."; return; }
        if (_viewModel.CollectionShelves.Any(shelf => string.Equals(shelf.Name, name, StringComparison.OrdinalIgnoreCase)))
        { _viewModel.CollectionLayoutStatus = "That shelf already exists."; return; }
        _viewModel.CollectionShelves.Add(new CollectionShelfEditor { Name = name });
        _viewModel.NewShelfName = "";
        _viewModel.CollectionLayoutStatus = $"Added {name}. Drag games onto it, then save.";
    }

    private void MoveCollectionShelfClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CollectionShelfEditor shelf, Tag: string direction }) return;
        var index = _viewModel.CollectionShelves.IndexOf(shelf);
        var destination = direction == "up" ? index - 1 : index + 1;
        if (destination < 0 || destination >= _viewModel.CollectionShelves.Count) return;
        _viewModel.CollectionShelves.Move(index, destination);
    }

    private void RemoveCollectionShelfClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CollectionShelfEditor shelf }) return;
        foreach (var game in shelf.Games.ToArray()) MoveCollectionGame(game, _viewModel.UnassignedCollectionGames);
        _viewModel.CollectionShelves.Remove(shelf);
        _viewModel.CollectionLayoutStatus = $"Removed {shelf.Name}; its games are now unassigned. Save to confirm.";
    }

    private async void SaveCollectionLayoutClicked(object? sender, RoutedEventArgs e)
    {
        if (_portablePaths is null) return;
        var names = _viewModel.CollectionShelves.Select(shelf => shelf.Name.Trim()).ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
        { _viewModel.CollectionLayoutStatus = "Every shelf needs a unique name before saving."; return; }
        try
        {
            _viewModel.Collection.Shelves = _viewModel.CollectionShelves.Select((shelf, index) =>
                new CollectionShelfConfiguration { Name = shelf.Name.Trim(), Order = (index + 1) * 10 }).ToList();
            var placements = _viewModel.CollectionShelves.SelectMany(shelf => shelf.Games.Select((game, index) =>
                    new CollectionGamePlacementUpdate(game.ConfigurationPath, shelf.Name.Trim(), (index + 1) * 10)))
                .Concat(_viewModel.UnassignedCollectionGames.Select((game, index) =>
                    new CollectionGamePlacementUpdate(game.ConfigurationPath, "", (index + 1) * 10))).ToArray();
            await new CollectionLayoutSaveService().SaveAsync(_portablePaths.Root, _viewModel.Collection, placements);
            if (_gameJsonPath is not null) await LoadGameConfigurationAsync(_gameJsonPath);
            _viewModel.CollectionLayoutStatus = $"Saved {placements.Length} games across {_viewModel.CollectionShelves.Count} shelves.";
        }
        catch (Exception ex) { _viewModel.CollectionLayoutStatus = $"Nothing was changed: {ex.Message}"; }
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

    private async void DeleteArtworkClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string asset } || string.IsNullOrWhiteSpace(_gameJsonPath))
        {
            _viewModel.Status = "Choose or save a game folder before deleting artwork.";
            return;
        }
        var artwork = _viewModel.Configuration.Artwork;
        var configuredPath = asset switch
        {
            "cover" => artwork.Cover,
            "hero" => artwork.Hero,
            "background" => artwork.Background,
            "logo" => artwork.Logo,
            "icon" => artwork.Icon,
            _ => ""
        };
        var label = asset == "background" ? "16:9 background" : asset;
        var folder = Path.GetDirectoryName(_gameJsonPath)!;
        var path = new GamePathResolver().ResolveExistingWithAnyExtension(folder, configuredPath);
        if (path is null)
        {
            _viewModel.Status = $"No local {label} file was found to delete.";
            return;
        }
        try
        {
            // Release the bitmap before deletion so Windows does not retain a file handle.
            switch (asset)
            {
                case "cover": _viewModel.CoverPreview = null; break;
                case "hero": _viewModel.HeroPreview = null; break;
                case "background": _viewModel.BackgroundPreview = null; break;
                case "logo": _viewModel.LogoPreview = null; break;
                case "icon": _viewModel.IconPreview = null; break;
            }
            File.Delete(path);
            await RefreshArtworkPreviewsAsync();
            _viewModel.Status = $"Deleted {label}: {Path.GetFileName(path)}. The configured path was kept for easy replacement.";
        }
        catch (Exception ex)
        {
            await RefreshArtworkPreviewsAsync();
            _viewModel.Status = $"Could not delete {label}: {ex.Message}";
        }
    }

    private async void RefreshApiArtworkClicked(object? sender, RoutedEventArgs e)
    {
        if (_portablePaths is null || string.IsNullOrWhiteSpace(_gameJsonPath))
        {
            _viewModel.Status = "Choose or save a game folder before refreshing API artwork.";
            return;
        }
        var steamId = _viewModel.Configuration.Artwork.SteamMetadataId.Trim();
        if (string.IsNullOrWhiteSpace(steamId))
        {
            _viewModel.Status = "A Steam metadata ID is needed to refresh downloaded artwork.";
            return;
        }

        var gameFolder = Path.GetDirectoryName(_gameJsonPath)!;
        var working = GameConfigurationJson.Deserialize(GameConfigurationJson.Serialize(_viewModel.Configuration));
        working.Artwork.DownloadMissingArtwork = true;
        var managedPaths = new[]
        {
            new GamePathResolver().Resolve(gameFolder, working.Artwork.Cover),
            new GamePathResolver().Resolve(gameFolder, working.Artwork.Hero),
            new GamePathResolver().Resolve(gameFolder, working.Artwork.Logo),
            new GamePathResolver().Resolve(gameFolder, working.Artwork.Icon)
        };
        var screenshotFolder = Path.Combine(gameFolder, "Artwork", "Screenshots");
        var backupFolder = Path.Combine(_portablePaths.Cache, "ArtworkRefresh", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupFolder);
        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            _viewModel.Status = $"Refreshing downloaded artwork for {_viewModel.Configuration.Game.Name}…";
            foreach (var path in managedPaths.Where(File.Exists))
            {
                var backup = Path.Combine(backupFolder, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                File.Move(path, backup);
                backups[path] = backup;
            }
            if (Directory.Exists(screenshotFolder))
            {
                foreach (var path in Directory.EnumerateFiles(screenshotFolder))
                {
                    var backup = Path.Combine(backupFolder, "screenshot-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                    File.Move(path, backup);
                    backups[path] = backup;
                }
            }

            var result = await new SteamMetadataService(_httpClient, new GamePathResolver())
                .EnrichAsync(gameFolder, working, _portablePaths);
            var refreshed = 0;
            foreach (var path in managedPaths.Concat(Directory.Exists(screenshotFolder)
                         ? Directory.EnumerateFiles(screenshotFolder)
                         : []))
            {
                if (IsReadableImage(path)) { refreshed++; continue; }
                if (File.Exists(path)) File.Delete(path);
                if (backups.Remove(path, out var backup)) File.Move(backup, path, true);
            }
            foreach (var (path, backup) in backups)
            {
                if (!File.Exists(path) && File.Exists(backup)) File.Move(backup, path, true);
            }

            await RefreshArtworkPreviewsAsync();
            await RunArtworkAuditAsync();
            _viewModel.Status = result.Warnings.Count == 0
                ? $"Updated {refreshed} API artwork files. Custom background and trailer were preserved."
                : $"Updated {refreshed} Steam artwork files. {string.Join(" ", result.Warnings)} Existing artwork was preserved.";
        }
        catch (Exception ex)
        {
            foreach (var path in managedPaths)
                if (File.Exists(path)) File.Delete(path);
            foreach (var (path, backup) in backups)
                if (File.Exists(backup)) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Move(backup, path, true); }
            _viewModel.Status = $"Artwork refresh failed; the previous files were restored: {ex.Message}";
        }
        finally
        {
            if (Directory.Exists(backupFolder)) Directory.Delete(backupFolder, recursive: true);
        }
    }

    private async void BrowseSteamGridDbArtworkClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gameJsonPath)) { _viewModel.Status = "Choose or save a game folder first."; return; }
        var settings = await ConfiguratorSettings.LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey)) { _viewModel.Status = "Add a SteamGridDB API key in Settings first."; return; }
        var gameId = _viewModel.Configuration.Artwork.SteamGridDbGameId;
        if (gameId is null) { _viewModel.Status = "Match this game by title first so its SteamGridDB game ID can be saved."; return; }
        var choice = await new SteamGridDbArtworkDialog(new SteamGridDbArtworkService(_httpClient), gameId.Value, settings.SteamGridDbApiKey)
            .ShowDialog<(SteamGridDbAssetKind Kind, SteamGridDbAsset Asset)?>(this);
        if (choice is null) return;
        var folder = Path.GetDirectoryName(_gameJsonPath)!;
        var artwork = _viewModel.Configuration.Artwork;
        var configured = choice.Value.Kind switch
        {
            SteamGridDbAssetKind.Cover => artwork.Cover,
            SteamGridDbAssetKind.Hero => artwork.Hero,
            SteamGridDbAssetKind.Logo => artwork.Logo,
            _ => artwork.Icon
        };
        try
        {
            var destination = new GamePathResolver().Resolve(folder, configured);
            await new SteamGridDbArtworkService(_httpClient).DownloadAsync(choice.Value.Asset, destination);
            var credit = $"SteamGridDB {choice.Value.Kind}: {choice.Value.Asset.Credit} (asset {choice.Value.Asset.Id})";
            if (!_viewModel.Configuration.Notes.Contains(credit, StringComparison.OrdinalIgnoreCase))
                _viewModel.Configuration.Notes = string.IsNullOrWhiteSpace(_viewModel.Configuration.Notes) ? credit : _viewModel.Configuration.Notes.TrimEnd() + Environment.NewLine + credit;
            await GameConfigurationJson.SaveAsync(_gameJsonPath, _viewModel.Configuration);
            await RefreshArtworkPreviewsAsync();
            _viewModel.Status = $"Saved the selected {choice.Value.Kind.ToString().ToLowerInvariant()} and recorded its attribution.";
        }
        catch (Exception ex) { _viewModel.Status = "Could not save selected artwork: " + ex.Message; }
        finally { choice.Value.Asset.Dispose(); }
    }

    private static bool IsReadableImage(string path)
    {
        if (!File.Exists(path)) return false;
        try { using var image = new Bitmap(path); return image.PixelSize.Width > 0 && image.PixelSize.Height > 0; }
        catch { return false; }
    }

    private async Task RefreshArtworkPreviewsAsync()
    {
        _viewModel.CoverPreview = null;
        _viewModel.BackgroundPreview = null;
        _viewModel.HeroPreview = null;
        _viewModel.LogoPreview = null;
        _viewModel.IconPreview = null;

        var configuration = _viewModel.Configuration;
        var folder = string.IsNullOrWhiteSpace(_gameJsonPath)
            ? null
            : Path.GetDirectoryName(_gameJsonPath);
        _viewModel.CoverPreview = await LoadArtworkPreviewAsync(folder, configuration.Artwork.Cover, configuration.Artwork.CoverUrl);
        _viewModel.BackgroundPreview = await LoadArtworkPreviewAsync(folder, configuration.Artwork.Background, configuration.Artwork.BackgroundUrl);
        _viewModel.HeroPreview = await LoadArtworkPreviewAsync(folder, configuration.Artwork.Hero, configuration.Artwork.HeroUrl);
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

    private async void DownloadArtworkAndSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gameJsonPath))
        {
            _viewModel.Status = "Choose a Cart/Games configuration folder before downloading artwork.";
            return;
        }

        var downloadButton = sender as Button;
        if (downloadButton is not null)
            downloadButton.IsEnabled = false;
        try
        {
            var gameFolder = Path.GetDirectoryName(_gameJsonPath)!;
            var artworkFolder = Path.Combine(gameFolder, "Artwork");
            var mediaFolder = Path.Combine(gameFolder, "Media");
            Directory.CreateDirectory(artworkFolder);
            Directory.CreateDirectory(mediaFolder);
            var artwork = _viewModel.Configuration.Artwork;
            var completed = new List<string>();
            var failures = new List<string>();

            await TryDownloadImageAsync("Cover", artwork.CoverUrl, artworkFolder, path => artwork.Cover = path, completed, failures);
            await TryDownloadImageAsync("Background", artwork.BackgroundUrl, artworkFolder, path => artwork.Background = path, completed, failures);
            await TryDownloadImageAsync("Hero", artwork.HeroUrl, artworkFolder, path => artwork.Hero = path, completed, failures);
            await TryDownloadImageAsync("Logo", artwork.LogoUrl, artworkFolder, path => artwork.Logo = path, completed, failures);
            await TryDownloadImageAsync("Icon", artwork.IconUrl, artworkFolder, path => artwork.Icon = path, completed, failures);
            await TryDownloadTrailerAsync(artwork.TrailerUrl, mediaFolder, path => artwork.Trailer = path, completed, failures);

            if (completed.Count == 0 && failures.Count == 0)
            {
                _viewModel.Status = "Add at least one direct artwork or video URL before downloading.";
                return;
            }

            await GameConfigurationJson.SaveAsync(_gameJsonPath, _viewModel.Configuration);
            _viewModel.RefreshPreview();
            await RefreshArtworkPreviewsAsync();
            await RunArtworkAuditAsync();
            _viewModel.Status = failures.Count == 0
                ? $"Downloaded and saved: {string.Join(", ", completed)}."
                : $"Saved {completed.Count} file(s). Could not download: {string.Join("; ", failures)}";
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Artwork download failed safely: {ex.Message}";
        }
        finally
        {
            if (downloadButton is not null)
                downloadButton.IsEnabled = true;
        }
    }

    private async Task TryDownloadImageAsync(
        string name,
        string url,
        string folder,
        Action<string> setConfiguredPath,
        List<string> completed,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        DownloadedAsset? result = null;
        try
        {
            result = await DownloadBoundedFileAsync(url, 25L * 1024 * 1024, isImage: true);
            var extension = ImageExtension(result.ContentType, result.FinalUri);
            var destination = Path.Combine(folder, name + extension);
            File.Move(result.TemporaryPath, destination, overwrite: true);
            setConfiguredPath($"Artwork/{name}{extension}");
            completed.Add(name);
        }
        catch (Exception ex)
        {
            failures.Add($"{name} ({ex.Message})");
        }
        finally
        {
            if (result is not null && File.Exists(result.TemporaryPath)) File.Delete(result.TemporaryPath);
        }
    }

    private async Task TryDownloadTrailerAsync(
        string url,
        string folder,
        Action<string> setConfiguredPath,
        List<string> completed,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return;
        DownloadedAsset? result = null;
        try
        {
            result = await DownloadBoundedFileAsync(url, 1024L * 1024 * 1024, isImage: false);
            var extension = Path.GetExtension(result.FinalUri.AbsolutePath).ToLowerInvariant();
            if (extension is not (".mp4" or ".webm" or ".mkv" or ".mov")) extension = ".mp4";
            var destination = Path.Combine(folder, "Trailer" + extension);
            File.Move(result.TemporaryPath, destination, overwrite: true);
            setConfiguredPath($"Media/Trailer{extension}");
            completed.Add("Trailer");
        }
        catch (Exception ex)
        {
            failures.Add($"Trailer ({ex.Message})");
        }
        finally
        {
            if (result is not null && File.Exists(result.TemporaryPath)) File.Delete(result.TemporaryPath);
        }
    }

    private async Task<DownloadedAsset> DownloadBoundedFileAsync(string url, long maximumBytes, bool isImage)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("URL must use HTTP or HTTPS");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/2.3 artwork-configurator");
        using var response = await _downloadHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("file exceeds the download limit");

        var temporary = Path.Combine(Path.GetTempPath(), "CLC-art-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    total = checked(total + read);
                    if (total > maximumBytes) throw new InvalidDataException("file exceeds the download limit");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
                await output.FlushAsync();
                output.Flush(true);
            }
            if (isImage)
            {
                using var image = new Avalonia.Media.Imaging.Bitmap(temporary);
                if (image.PixelSize.Width <= 0 || image.PixelSize.Height <= 0)
                    throw new InvalidDataException("download is not a readable image");
            }
            return new DownloadedAsset(
                temporary,
                response.Content.Headers.ContentType?.MediaType ?? "",
                response.RequestMessage?.RequestUri ?? uri);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static string ImageExtension(string contentType, Uri uri)
    {
        if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase)) return ".png";
        if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        if (contentType.Contains("gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
        if (contentType.Contains("bmp", StringComparison.OrdinalIgnoreCase)) return ".bmp";
        if (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension is ".png" or ".webp" or ".gif" or ".bmp" or ".jpg" or ".jpeg" ? extension : ".png";
    }

    private sealed record DownloadedAsset(string TemporaryPath, string ContentType, Uri FinalUri);

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
                AuditStageArtwork(option.Name, folder, configuration.Artwork, resolver);
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

    private void AuditStageArtwork(string game, string folder, ArtworkConfiguration artwork, GamePathResolver resolver)
    {
        var background = resolver.ResolveExistingWithAnyExtension(folder, artwork.Background);
        var hero = resolver.ResolveExistingWithAnyExtension(folder, artwork.Hero);
        var path = background ?? hero;
        var label = background is not null ? "16:9 background" : "Hero";
        if (path is null)
        {
            _viewModel.ArtworkAuditResults.Add(new ArtworkAuditItem(game, "Hero / background", "✕", "#FF6B72", "Both are missing"));
            return;
        }
        try
        {
            using var image = new Bitmap(path);
            _viewModel.ArtworkAuditResults.Add(new ArtworkAuditItem(game, label, "✓", "#69DB8A", $"{image.PixelSize.Width} × {image.PixelSize.Height}"));
        }
        catch
        {
            _viewModel.ArtworkAuditResults.Add(new ArtworkAuditItem(game, label, "✕", "#FF6B72", "Unreadable image"));
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
