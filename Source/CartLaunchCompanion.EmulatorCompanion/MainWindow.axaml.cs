using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Emulators.Adapters;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly LibraryViewModel _viewModel;

    public MainWindow() : this(CreateViewModel()) { }

    public MainWindow(LibraryViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        GamepadMenuNavigation.Attach(this, initialFocus: LibraryList,
            backAction: () => LibraryList.Focus(NavigationMethod.Directional));
        ControllerNavigationStatus.Text = GamepadMenuNavigation.Status;
        GamepadMenuNavigation.StatusChanged += ControllerNavigationStatusChanged;
        Opened += async (_, _) => await _viewModel.RefreshAsync(_lifetime.Token);
        Closed += (_, _) =>
        {
            GamepadMenuNavigation.StatusChanged -= ControllerNavigationStatusChanged;
            _lifetime.Cancel();
        };
    }

    private void ControllerNavigationStatusChanged(object? sender, string status) =>
        ControllerNavigationStatus.Text = status;

    private async void RefreshClicked(object? sender, RoutedEventArgs args) =>
        await _viewModel.RefreshAsync(_lifetime.Token);

    private void ExitClicked(object? sender, RoutedEventArgs args) => Close();

    private async void ReleasesClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel.SelectedRow?.Entry.Definition is not { } definition) return;
        IEmulatorReleaseAdapter adapter;
        IEmulatorInstaller? installer;
        string installLocation;
        switch (definition.Id)
        {
            case "ppsspp":
                adapter = new PpssppReleaseAdapter();
                installer = new PpssppInstaller(_viewModel.MediaRoot, _viewModel.StateRoot);
                installLocation = "Install folder: " + Path.Combine(_viewModel.MediaRoot, "Emulators");
                break;
            case "duckstation":
                adapter = new DuckStationReleaseAdapter();
                installer = new DuckStationInstaller(_viewModel.MediaRoot, _viewModel.StateRoot);
                installLocation = "Install folder: " + Path.Combine(_viewModel.MediaRoot, "Emulators");
                break;
            case "pcsx2":
                adapter = new Pcsx2ReleaseAdapter();
                installer = new Pcsx2Installer(_viewModel.MediaRoot, _viewModel.StateRoot);
                installLocation = "Install folder: " + Path.Combine(_viewModel.MediaRoot, "Emulators");
                break;
            case "rpcs3":
                adapter = new Rpcs3ReleaseAdapter();
                installer = new Rpcs3Installer(_viewModel.MediaRoot, _viewModel.StateRoot);
                installLocation = "Install folder: " + Path.Combine(_viewModel.MediaRoot, "Emulators");
                break;
            case "shadps4":
                adapter = new ShadPs4ReleaseAdapter();
                installer = new ShadPs4Installer(_viewModel.MediaRoot, _viewModel.StateRoot);
                installLocation = "Install folder: " + Path.Combine(_viewModel.MediaRoot, "Emulators");
                break;
            default:
                return;
        }
        using var details = new ReleaseDetailsViewModel(definition, adapter, installer)
            { InstallLocation = installLocation };
        await new ReleaseDetailsWindow(details).ShowDialog(this);
        await _viewModel.RefreshAsync(_lifetime.Token);
        if (details.Installed)
        {
            var preservedData = new EmulatorUninstallService(_viewModel.MediaRoot, _viewModel.StateRoot);
            var controllerProfiles = new LibraryControllerProfileApplicator(_viewModel.MediaRoot, _viewModel.StateRoot);
            foreach (var platform in details.Results.Where(result => result.Completed).Select(result => result.Platform))
            {
                await preservedData.RestorePreservedDataAsync(definition.Id, platform, _lifetime.Token);
                await controllerProfiles.ApplyAsync(definition.Id, platform, _lifetime.Token);
            }
        }
        if (details.Installed && ManagedSetupCatalog.All.Any(item => item.EmulatorId == definition.Id))
            await ShowSetupAsync(definition.Id);
    }

    private async void UpdateSettingsClicked(object? sender, RoutedEventArgs args)
    {
        using var model = new UpdateSettingsViewModel(new EmulatorUpdateSettingsStore(_viewModel.StateRoot));
        await new UpdateSettingsWindow(model).ShowDialog(this);
    }

    private async void ControllerSetupClicked(object? sender, RoutedEventArgs args)
    {
        var platform = OperatingSystem.IsLinux()
            ? CartLaunchCompanion.Core.Platform.PlatformKind.Linux
            : CartLaunchCompanion.Core.Platform.PlatformKind.Windows;
        var installed = _viewModel.Rows.SelectMany(row => row.Entry.Installations)
            .Where(installation => installation.Platform == platform)
            .Select(installation => installation.EmulatorId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await new ControllerSetupWindow(new ControllerSetupViewModel(
            platform, _viewModel.MediaRoot, _viewModel.StateRoot, installed)).ShowDialog(this);
        await _viewModel.RefreshControllerAsync(_lifetime.Token);
    }

    private async void SetupClicked(object? sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanSetUp || _viewModel.SelectedRow?.Entry.EmulatorId is not { } emulatorId) return;
        await ShowSetupAsync(emulatorId);
    }

    private async void UninstallClicked(object? sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanUninstall || _viewModel.SelectedRow is not { } row) return;
        var model = new UninstallViewModel(row,
            new EmulatorUninstallService(_viewModel.MediaRoot, _viewModel.StateRoot));
        if (await new UninstallWindow(model).ShowDialog<bool>(this))
            await _viewModel.RefreshAsync(_lifetime.Token);
    }

    private async Task ShowSetupAsync(string emulatorId)
    {
        if (emulatorId == "pcsx2") { await ShowPcsx2SetupAsync(); return; }
        var model = new PortableSetupViewModel(new PortableEmulatorSetupService(
            _viewModel.MediaRoot, _viewModel.StateRoot, emulatorId));
        await new PortableSetupWindow(model).ShowDialog(this);
        await _viewModel.RefreshAsync(_lifetime.Token);
    }

    private async Task ShowPcsx2SetupAsync()
    {
        var model = new Pcsx2SetupViewModel(
            new Pcsx2SetupService(_viewModel.MediaRoot, _viewModel.StateRoot),
            new Pcsx2ConfigurationAdapter(_viewModel.MediaRoot, _viewModel.StateRoot),
            _viewModel.MediaRoot, _viewModel.StateRoot);
        await new Pcsx2SetupWindow(model).ShowDialog(this);
        await _viewModel.RefreshAsync(_lifetime.Token);
    }

    private async void AboutClicked(object? sender, RoutedEventArgs args) =>
        await new AboutWindow(new AboutViewModel(_viewModel.Rows
            .Select(row => row.Entry.Definition)
            .Where(definition => definition is not null)
            .DistinctBy(definition => definition!.Id)
            .Select(definition => definition!)))
            .ShowDialog(this);

    private static LibraryViewModel CreateViewModel()
    {
        var applicationRoot = Program.TrustedCartRoot ??
            new PortablePathService().DiscoverReadOnly(AppContext.BaseDirectory).Root;
        var layout = EmulatorStorageLayout.FromApplicationRoot(applicationRoot);
        var catalog = new EmulatorCatalogSource(Path.Combine(AppContext.BaseDirectory, "Catalog", "emulators.json"));
        return new(new EmulatorLibraryService(catalog, new EmulatorRegistryStore(layout.StateRoot)), layout.MediaRoot, async token =>
        {
            using var installer = new PpssppInstaller(layout.MediaRoot, layout.StateRoot);
            await installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Windows, token);
            await installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Linux, token);
            using var duckStationInstaller = new DuckStationInstaller(layout.MediaRoot, layout.StateRoot);
            await duckStationInstaller.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Windows, token);
            await duckStationInstaller.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Linux, token);
            using var pcsx2Installer = new Pcsx2Installer(layout.MediaRoot, layout.StateRoot);
            await pcsx2Installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Windows, token);
            await pcsx2Installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Linux, token);
            using var rpcs3Installer = new Rpcs3Installer(layout.MediaRoot, layout.StateRoot);
            await rpcs3Installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Windows, token);
            await rpcs3Installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Linux, token);
            using var shadPs4Installer = new ShadPs4Installer(layout.MediaRoot, layout.StateRoot);
            await shadPs4Installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Windows, token);
            await shadPs4Installer.RecoverAsync(CartLaunchCompanion.Core.Platform.PlatformKind.Linux, token);
        }, layout.StateRoot);
    }
}
