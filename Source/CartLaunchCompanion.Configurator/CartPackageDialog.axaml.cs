using System.ComponentModel;
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
            { DestinationStatus = "This destination already has a non-empty Cart folder. Choose another location."; return; }
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
        CreateButton.IsEnabled = false; Progress = 0; ResultStatus = "Staging a clean portable cart…";
        try
        {
            var progress = new Progress<double>(value => Progress = value * 100);
            var result = await new CartPackageCreator().CreateAsync(new(SourceRoot, DestinationRoot), progress);
            var identityService = new CartIdentityService();
            var identity = await identityService.SaveNewAsync(DestinationRoot, identityService.Create(CartName));
            var requiredFolders = new[] { "Cart", "Games", "Emulators", "Roms" };
            if (requiredFolders.Any(name => !Directory.Exists(Path.Combine(DestinationRoot, name))))
                throw new InvalidDataException("Final folder verification failed.");
            Progress = 100;
            ResultStatus = $"Portable cart created and verified: {result.FilesCopied} files, {FormatBytes(result.BytesCopied)}. Identity {identity.Identity.CartId} was created at the media root. Trust is still granted separately on each computer.";
        }
        catch (Exception ex) { ResultStatus = "Nothing was overwritten. Package creation stopped: " + ex.Message; ValidateDestination(); }
    }
    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
    private static string FormatBytes(long value) => value >= 1_073_741_824 ? $"{value / 1_073_741_824d:0.00} GB" : value >= 1_048_576 ? $"{value / 1_048_576d:0.0} MB" : $"{value / 1024d:0.0} KB";
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
