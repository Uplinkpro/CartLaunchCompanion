using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Host;

public sealed partial class LaunchConfirmationWindow : Window
{
    public string CartName { get; }
    public string MediaRoot { get; }
    public string ExecutablePath { get; }
    public LaunchConfirmationWindow() { CartName = MediaRoot = ExecutablePath = ""; InitializeComponent(); DataContext = this; }
    public LaunchConfirmationWindow(string cartName, string mediaRoot, string executablePath)
    { CartName = cartName; MediaRoot = mediaRoot; ExecutablePath = executablePath; InitializeComponent(); DataContext = this; }
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void LaunchClicked(object? sender, RoutedEventArgs e) => Close(true);
}
