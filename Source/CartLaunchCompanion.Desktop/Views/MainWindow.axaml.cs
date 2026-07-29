using Avalonia.Controls;
using Avalonia.Input;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Desktop.Input;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(
            KeyDownEvent,
            OnPreviewKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private async void OnPreviewKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var input = AvaloniaInputMapper.Map(e);

        if (input.Action is LauncherAction.None)
            return;

        e.Handled = true;
        await viewModel.HandleInputAsync(input);
    }
}
