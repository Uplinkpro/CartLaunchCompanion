using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Host;

public sealed partial class TrustConfirmationWindow : Window
{
    public string CartName { get; }
    public string CartId { get; }
    public int SecurityVersion { get; }
    public string MediaRoot { get; }
    public IReadOnlyList<TrustRuntimeItem> Runtimes { get; }

    public TrustConfirmationWindow()
    {
        CartName = CartId = MediaRoot = "";
        Runtimes = [];
        InitializeComponent();
        DataContext = this;
    }

    public TrustConfirmationWindow(PhysicalCartReadinessReport report, string mediaRoot)
    {
        var identity = report.Identity ?? throw new ArgumentException("A verified cart identity is required.", nameof(report));
        if (!report.IsReady || report.RuntimeApprovals.Count == 0)
            throw new ArgumentException("Only a ready cart can be reviewed for trust.", nameof(report));
        CartName = identity.Identity.DisplayName;
        CartId = identity.Identity.CartId;
        SecurityVersion = identity.Identity.SecurityVersion;
        MediaRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        Runtimes = report.RuntimeApprovals
            .OrderBy(item => item.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(item => new TrustRuntimeItem(item.Platform, $"{item.Files.Count} verified files"))
            .ToList();
        InitializeComponent();
        DataContext = this;
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void ApproveClicked(object? sender, RoutedEventArgs e)
    {
        if (TrustConfirm.IsChecked != true)
        {
            ConfirmationHelp.Text = "Check the confirmation before trust can be saved.";
            ConfirmationHelp.Foreground = Avalonia.Media.Brushes.Orange;
            return;
        }
        Close(true);
    }
}

public sealed record TrustRuntimeItem(string Platform, string FileSummary);
