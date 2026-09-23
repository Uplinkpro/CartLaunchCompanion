using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class ControllerSetupWindow : Window
{
    private readonly ControllerSetupViewModel _model = null!;
    public ControllerSetupWindow() => InitializeComponent();
    public ControllerSetupWindow(ControllerSetupViewModel model) : this()
    {
        _model = model; DataContext = model; Opened += (_, _) => model.Start();
        Closed += async (_, _) => await model.DisposeAsync();
        GamepadMenuNavigation.Attach(this, () => model.IsTesting);
    }
    private void StartClicked(object? sender, RoutedEventArgs args) => _model.StartTest();
    private async void SaveClicked(object? sender, RoutedEventArgs args)
    {
        try { await _model.SaveAsync(); Close(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _model.Report(error.Message); }
    }
    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
