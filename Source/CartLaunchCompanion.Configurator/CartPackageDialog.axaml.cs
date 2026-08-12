using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CartLaunchCompanion.Core.PhysicalCarts;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Configurator;

public sealed partial class CartPackageDialog : Window, INotifyPropertyChanged
{
    private string _sourceRoot = "";
    private string _sourceStatus = "";
    private string _destinationRoot = "";
    private string _cartName = "";
    private string _destinationStatus = "Choose the root of the removable drive or a new empty test folder.";
    private string _resultStatus = "Ready after a valid destination is selected.";
    private double _progress;
    public CartPackageDialog() { InitializeComponent(); DataContext = this; }
    public CartPackageDialog(string sourceRoot)
    { _sourceRoot = Path.GetFullPath(sourceRoot); InitializeComponent(); DataContext = this; ValidateSource(); }
    public string SourceRoot { get => _sourceRoot; set { _sourceRoot = value; Changed(); } }
    public string SourceStatus { get => _sourceStatus; set { _sourceStatus = value; Changed(); } }
    public string DestinationRoot { get => _destinationRoot; set { _destinationRoot = value; Changed(); } }
    public string CartName { get => _cartName; set { _cartName = value; Changed(); ValidateDestination(); } }
    public string DestinationStatus { get => _destinationStatus; set { _destinationStatus = value; Changed(); } }
    public string ResultStatus { get => _resultStatus; set { _resultStatus = value; Changed(); } }
    public double Progress { get => _progress; set { _progress = value; Changed(); } }
    public ObservableCollection<ReadinessCheckItem> ReadinessChecks { get; } = [];
    public new event PropertyChangedEventHandler? PropertyChanged;

    private async void ChooseDestinationClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose removable-media root", AllowMultiple = false });
        if (folders.Count == 0) return;
        DestinationRoot = folders[0].Path.LocalPath;
        ValidateDestination();
    }

    private async void ChooseSourceClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a published Cart Launch Companion folder", AllowMultiple = false });
        if (folders.Count == 0) return;
        SourceRoot = folders[0].Path.LocalPath;
        ValidateSource(); ValidateDestination();
    }

    private bool ValidateSource()
    {
        var system = Path.Combine(SourceRoot, "System");
        var valid = Directory.Exists(system) && (Directory.Exists(Path.Combine(system, "Windows-x64")) || Directory.Exists(Path.Combine(system, "Linux-x64")));
        SourceStatus = valid
            ? "Published portable layout detected. Source code and development files will still be excluded."
            : "This is not a published Cart layout yet. Choose a folder containing System/Windows-x64 or System/Linux-x64.";
        return valid;
    }

    private void ValidateDestination()
    {
        CreateButton.IsEnabled = false;
        CreateButton.Content = "Create portable cart";
        if (!ValidateSource() || string.IsNullOrWhiteSpace(DestinationRoot)) return;
        if (string.IsNullOrWhiteSpace(CartName) || CartName.Trim().Length > 80)
        { DestinationStatus = "Enter a cart name between 1 and 80 characters."; return; }
        try
        {
            var source = Path.GetFullPath(SourceRoot); var destination = Path.GetFullPath(DestinationRoot);
            if (destination.Equals(source, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { DestinationStatus = "Choose a destination outside the current Cart folder."; return; }
            var cart = Path.Combine(destination, "Cart");
            if (Directory.Exists(cart) && Directory.EnumerateFileSystemEntries(cart).Any())
            {
                DestinationStatus = "Existing Cart detected. Prepare mode preserves its files and identity, then checks readiness.";
                CreateButton.Content = "Inspect and prepare cart";
                CreateButton.IsEnabled = true;
                return;
            }
            var required = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
            var root = Path.GetPathRoot(destination);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < required * 1.1) { DestinationStatus = $"Not enough free space. Approximately {FormatBytes(required)} is required."; return; }
                DestinationStatus = $"Ready. Approximately {FormatBytes(required)} before development files are excluded; {FormatBytes(free)} free.";
            }
            else DestinationStatus = "Ready. The destination folder will be created.";
            CreateButton.IsEnabled = true;
        }
        catch (Exception ex) { DestinationStatus = "Destination is not usable: " + ex.Message; }
    }

    private async void CreateClicked(object? sender, RoutedEventArgs e)
    {
        CreateButton.IsEnabled = false; Progress = 0; ReadinessChecks.Clear();
        try
        {
            var existing = Directory.Exists(Path.Combine(DestinationRoot, "Cart")) &&
                           Directory.EnumerateFileSystemEntries(Path.Combine(DestinationRoot, "Cart")).Any();
            CartPackageResult? result = null;
            if (!existing)
            {
                ResultStatus = "Staging a clean portable cart…";
                var progress = new Progress<double>(value => Progress = value * 80);
                result = await new CartPackageCreator().CreateAsync(new(SourceRoot, DestinationRoot), progress);
            }
            else ResultStatus = "Inspecting the existing cart without replacing files…";

            var readiness = await new PhysicalCartReadinessService().PrepareAsync(DestinationRoot, CartName);
            foreach (var check in readiness.Checks)
                ReadinessChecks.Add(new(check.Name, check.Detail, check.Passed ? "✓" : "✕", check.Passed ? "#69DB8A" : "#FF6B72"));
            Progress = 100;
            var platforms = readiness.RuntimeApprovals.Count == 0 ? "no verified runtimes" : string.Join(", ", readiness.RuntimeApprovals.Select(item => item.Platform));
            ResultStatus = readiness.IsReady
                ? $"Ready for Host trust. Identity: {readiness.Identity!.Identity.DisplayName}. Verified: {platforms}." +
                  (result is null ? " Existing files and identity were preserved." : $" Copied {result.FilesCopied} files ({FormatBytes(result.BytesCopied)}).")
                : "Not ready for Host trust yet. Resolve the failed checks below; no existing identity was replaced.";
        }
        catch (Exception ex) { ResultStatus = "Nothing was overwritten. Package creation stopped: " + ex.Message; ValidateDestination(); }
    }
    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
    private static string FormatBytes(long value) => value >= 1_073_741_824 ? $"{value / 1_073_741_824d:0.00} GB" : value >= 1_048_576 ? $"{value / 1_048_576d:0.0} MB" : $"{value / 1024d:0.0} KB";
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ReadinessCheckItem(string Name, string Detail, string Symbol, string Color);
