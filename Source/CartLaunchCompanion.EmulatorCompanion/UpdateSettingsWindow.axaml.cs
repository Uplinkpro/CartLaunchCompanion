using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class UpdateSettingsWindow : Window
{
    private readonly UpdateSettingsViewModel? _model;
    public UpdateSettingsWindow() { InitializeComponent(); GamepadMenuNavigation.Attach(this); }
    public UpdateSettingsWindow(UpdateSettingsViewModel model) : this()
    {
        _model = model;
        DataContext = model;
        Opened += async (_, _) => await model.LoadAsync();
        Closed += (_, _) => model.Dispose();
    }
    private async void SaveClicked(object? sender, RoutedEventArgs args) { if (_model is { } model) await model.SaveAsync(); }
    private async void ClearWindowsClicked(object? sender, RoutedEventArgs args) { if (_model is { } model) await model.ClearSkippedAsync(PlatformKind.Windows); }
    private async void ClearLinuxClicked(object? sender, RoutedEventArgs args) { if (_model is { } model) await model.ClearSkippedAsync(PlatformKind.Linux); }
    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
