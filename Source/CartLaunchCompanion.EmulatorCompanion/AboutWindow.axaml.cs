using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class AboutWindow : Window
{
    private AboutViewModel? _viewModel;

    public AboutWindow() { InitializeComponent(); GamepadMenuNavigation.Attach(this); }

    public AboutWindow(AboutViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void OpenLinkClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null || sender is not Button { Tag: Uri uri }) return;
        try
        {
            if (!await Launcher.LaunchUriAsync(uri)) _viewModel.ReportLinkFailure();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        {
            _viewModel.ReportLinkFailure();
        }
    }

    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
