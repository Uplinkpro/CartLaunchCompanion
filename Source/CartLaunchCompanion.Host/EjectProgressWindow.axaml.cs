using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace CartLaunchCompanion.Host;

public sealed partial class EjectProgressWindow : Window
{
    public string CartName { get; }
    public Bitmap? CollectionLogo { get; }
    public bool HasCollectionLogo => CollectionLogo is not null;

    public EjectProgressWindow() : this("Game cart", null) { }

    public EjectProgressWindow(string cartName, string? collectionLogoPath)
    {
        CartName = string.IsNullOrWhiteSpace(cartName) ? "Game cart" : cartName;
        CollectionLogo = TryLoadLogo(collectionLogoPath);
        InitializeComponent();
        DataContext = this;
        Closed += (_, _) => CollectionLogo?.Dispose();
    }

    public void UpdateDetail(string detail) => DetailText.Text = detail;

    private static Bitmap? TryLoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return new Bitmap(path); }
        catch { return null; }
    }
}
