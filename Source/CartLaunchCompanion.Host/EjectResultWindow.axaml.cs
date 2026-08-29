using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace CartLaunchCompanion.Host;

public enum EjectResultAction { Dismiss, Retry, Reopen }

public sealed partial class EjectResultWindow : Window
{
    private readonly TaskCompletionSource<EjectResultAction> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string CartName { get; }
    public string Heading { get; }
    public string Detail { get; }
    public string StatusGlyph { get; }
    public string AccentColor { get; }
    public string AccentBackground { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Bitmap? CollectionLogo { get; }
    public bool HasCollectionLogo => CollectionLogo is not null;
    public Task<EjectResultAction> Completion => _completion.Task;

    public EjectResultWindow() : this(true, "Game cart", "", null) { }

    public EjectResultWindow(bool success, string cartName, string detail, string? collectionLogoPath)
    {
        IsSuccess = success;
        CartName = string.IsNullOrWhiteSpace(cartName) ? "Game cart" : cartName;
        Heading = success ? "SAFE TO REMOVE" : "EJECT FAILED";
        Detail = detail;
        StatusGlyph = success ? "✓" : "!";
        AccentColor = success ? "#59D98E" : "#FF7272";
        AccentBackground = success ? "#2459D98E" : "#24FF7272";
        CollectionLogo = TryLoadLogo(collectionLogoPath);
        InitializeComponent();
        DataContext = this;
        Closed += (_, _) =>
        {
            CollectionLogo?.Dispose();
            _completion.TrySetResult(EjectResultAction.Dismiss);
        };
    }

    private void DismissClicked(object? sender, RoutedEventArgs e) => Complete(EjectResultAction.Dismiss);
    private void RetryClicked(object? sender, RoutedEventArgs e) => Complete(EjectResultAction.Retry);
    private void ReopenClicked(object? sender, RoutedEventArgs e) => Complete(EjectResultAction.Reopen);

    private void Complete(EjectResultAction action)
    {
        _completion.TrySetResult(action);
        Close();
    }

    private static Bitmap? TryLoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return new Bitmap(path); }
        catch { return null; }
    }
}
