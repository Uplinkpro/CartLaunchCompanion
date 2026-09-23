using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class UninstallWindow : Window
{
    private readonly UninstallViewModel? _model;
    public UninstallWindow() { InitializeComponent(); GamepadMenuNavigation.Attach(this); }
    public UninstallWindow(UninstallViewModel model) : this() { _model = model; DataContext = model; }
    private async void UninstallClicked(object? sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        try
        {
            await _model.UninstallAsync();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _model.Report("Uninstall could not complete: " + error.Message); }
    }
    private void CancelClicked(object? sender, RoutedEventArgs args) => Close(false);
    private void ReturnHomeClicked(object? sender, RoutedEventArgs args) => Close(true);
}
