using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Host;

public sealed partial class AutoLaunchApprovalWindow : Window
{
    public string CartName { get; }
    public string CartId { get; }
    public AutoLaunchApprovalWindow() { CartName = CartId = ""; InitializeComponent(); DataContext = this; }
    public AutoLaunchApprovalWindow(string name, string id) { CartName = name; CartId = id; InitializeComponent(); DataContext = this; }
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void ApproveClicked(object? sender, RoutedEventArgs e) => Close(true);
}
