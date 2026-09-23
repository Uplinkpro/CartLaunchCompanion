using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class ReleaseDetailsWindow : Window
{
    private readonly ReleaseDetailsViewModel? _viewModel;

    public ReleaseDetailsWindow() { InitializeComponent(); GamepadMenuNavigation.Attach(this); }

    public ReleaseDetailsWindow(ReleaseDetailsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += (_, args) =>
        {
            if (!viewModel.IsInstalling) return;
            args.Cancel = true;
            viewModel.CancelInstallation();
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    private async void InstallClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is not { } model) return;
        await model.InstallAsync();
        if (model.Installed) Close();
    }

    private async void CheckClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is { } model) await model.CheckAsync();
    }

    private async void ReleaseNotesClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null || sender is not Button { Tag: Uri uri }) return;
        try
        {
            if (!await Launcher.LaunchUriAsync(uri))
                _viewModel.ReportReleaseNotesFailure();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        {
            _viewModel.ReportReleaseNotesFailure();
        }
    }

    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
