using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Host;

public sealed partial class FirstInsertSetupWindow : Window
{
    public string MediaRoot { get; }
    public string SuggestedName { get; }
    public string RuntimeSummary { get; }
    public string MissingFolderSummary { get; }
    public string ConfirmedName => CartName.Text?.Trim() ?? "";

    public FirstInsertSetupWindow()
    {
        MediaRoot = RuntimeSummary = MissingFolderSummary = "";
        SuggestedName = "My Game Cart";
        InitializeComponent();
        DataContext = this;
    }

    public FirstInsertSetupWindow(UnpreparedCartCandidate candidate)
    {
        MediaRoot = candidate.MediaRoot;
        SuggestedName = candidate.SuggestedName;
        RuntimeSummary = string.Join(", ", candidate.Platforms);
        MissingFolderSummary = candidate.MissingFolders.Count == 0
            ? "None"
            : string.Join(", ", candidate.MissingFolders);
        InitializeComponent();
        DataContext = this;
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void ApproveClicked(object? sender, RoutedEventArgs e)
    {
        if (SetupConfirm.IsChecked != true)
        {
            ShowError("Check the confirmation before setup can begin.");
            return;
        }
        if (ConfirmedName.Length is < 1 or > 80 || ConfirmedName.Any(char.IsControl))
        {
            ShowError("Enter a printable cart name from 1 to 80 characters.");
            return;
        }
        Close(true);
    }

    private void ShowError(string message)
    {
        ConfirmationHelp.Text = message;
        ConfirmationHelp.Foreground = Avalonia.Media.Brushes.Orange;
    }
}
