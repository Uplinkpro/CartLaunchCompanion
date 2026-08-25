using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed partial class RetroArchCoreDialog : Window
{
    public RetroArchCoreDialog() => InitializeComponent();

    public RetroArchCoreDialog(IReadOnlyList<string> corePaths, string romPath, bool ambiguous)
    {
        InitializeComponent();
        CoreBox.ItemsSource = corePaths;
        CoreBox.SelectedIndex = corePaths.Count > 0 ? 0 : -1;
        ExplanationText.Text = ambiguous
            ? $"{Path.GetExtension(romPath)} can belong to several systems. Choose the core that matches this game."
            : $"Several installed cores support {Path.GetExtension(romPath)} files. Choose the one you prefer.";
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
    private void UseClicked(object? sender, RoutedEventArgs e) => Close(CoreBox.SelectedItem as string);
}
