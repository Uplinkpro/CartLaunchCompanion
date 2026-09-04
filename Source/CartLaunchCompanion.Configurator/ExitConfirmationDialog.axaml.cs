using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public enum ConfiguratorExitChoice
{
    Cancel,
    ExitWithoutSaving,
    SaveAndExit
}

public sealed partial class ExitConfirmationDialog : Window
{
    public ExitConfirmationDialog() => InitializeComponent();

    private void CancelClicked(object? sender, RoutedEventArgs e) =>
        Close(ConfiguratorExitChoice.Cancel);

    private void ExitWithoutSavingClicked(object? sender, RoutedEventArgs e) =>
        Close(ConfiguratorExitChoice.ExitWithoutSaving);

    private void SaveAndExitClicked(object? sender, RoutedEventArgs e) =>
        Close(ConfiguratorExitChoice.SaveAndExit);
}
