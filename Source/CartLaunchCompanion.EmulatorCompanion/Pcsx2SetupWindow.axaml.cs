using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class Pcsx2SetupWindow : Window
{
    private Pcsx2SetupViewModel? _viewModel;
    private readonly CancellationTokenSource _lifetime = new();

    public Pcsx2SetupWindow()
    {
        InitializeComponent();
        GamepadMenuNavigation.Attach(this);
    }

    public Pcsx2SetupWindow(Pcsx2SetupViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _lifetime.Cancel();
    }

    private async void ImportBiosClicked(object? sender, RoutedEventArgs args)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose your dumped PlayStation 2 BIOS",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("PlayStation 2 BIOS files") { Patterns = ["*.bin", "*.rom", "*.nvm", "*.mec", "*.erom"] }]
        });
        var paths = files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Select(path => path!).ToArray();
        if (paths.Length == 0) return;
        if (_viewModel is null) return;
        try { await _viewModel.ImportAsync(paths, _lifetime.Token); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _viewModel.Report("The BIOS could not be imported: " + error.Message); }
    }

    private void LaunchClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null || !_viewModel.CanLaunch || _viewModel.CurrentPlatformInstallation is not { } setup) return;
        try
        {
            Process.Start(new ProcessStartInfo(setup.ExecutablePath)
            {
                WorkingDirectory = Path.GetDirectoryName(setup.ExecutablePath)!,
                UseShellExecute = true
            });
            _viewModel.Report("PCSX2 is open. Complete its first-run setup, close it, then refresh this status.");
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        { _viewModel.Report("PCSX2 could not be opened: " + error.Message); }
    }
    private async void RefreshClicked(object? sender, RoutedEventArgs args) => await RefreshAsync();
    private async void PreviewConfigurationClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null) return;
        try { await _viewModel.PreviewConfigurationAsync(_lifetime.Token); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _viewModel.Report("Settings could not be reviewed: " + error.Message); }
    }
    private async void ApplyConfigurationClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null) return;
        try { await _viewModel.ApplyConfigurationAsync(_lifetime.Token); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _viewModel.Report("Settings could not be applied: " + error.Message); }
    }
    private async void RestoreConfigurationClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null) return;
        try { await _viewModel.RestoreConfigurationAsync(_lifetime.Token); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _viewModel.Report("Settings could not be restored: " + error.Message); }
    }
    private async Task RefreshAsync()
    {
        if (_viewModel is null) return;
        try { await _viewModel.RefreshAsync(_lifetime.Token); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _viewModel.Report("Setup status could not be refreshed: " + error.Message); }
    }
    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
