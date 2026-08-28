using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CartLaunchCompanion.Host;

public sealed partial class VerificationProgressWindow : Window
{
    public string CartName { get; }
    public Bitmap? CollectionLogo { get; }
    public bool HasCollectionLogo => CollectionLogo is not null;

    public VerificationProgressWindow() : this("", null) { }

    public VerificationProgressWindow(string cartName, string? collectionLogoPath)
    {
        CartName = cartName;
        CollectionLogo = TryLoadLogo(collectionLogoPath);
        InitializeComponent();
        DataContext = this;
        Closed += (_, _) => CollectionLogo?.Dispose();
    }

    public void ShowFailure()
    {
        Spinner.IsVisible = false;
        Detail.Text = "The trusted runtime did not pass verification.";
        Detail.Foreground = Brush.Parse("#FF7272");
    }

    private static Bitmap? TryLoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return new Bitmap(path); }
        catch { return null; }
    }
}
