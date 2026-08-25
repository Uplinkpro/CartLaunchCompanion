using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Configurator;

public sealed partial class EmulatorLibraryDialog : Window
{
    private readonly string _mediaRoot = "";
    private readonly EmulatorLibraryService _library = new();
    private readonly ObservableCollection<EmulatorLibraryRow> _rows = [];

    public EmulatorLibraryDialog() => InitializeComponent();

    public EmulatorLibraryDialog(string mediaRoot)
    {
        InitializeComponent();
        _mediaRoot = mediaRoot;
        EmulatorPortableLayout.Create(mediaRoot);
        RootText.Text = $"Cart emulator root: {Path.Combine(mediaRoot, "Emulators")}";
        EmulatorItems.ItemsSource = _rows;
        Reload();
    }

    private void Reload()
    {
        _rows.Clear();
        foreach (var item in _library.Scan(_mediaRoot, includeMissing: true))
            _rows.Add(new EmulatorLibraryRow(item));
        var installed = _rows.Count(row => row.Item.HasWindows || row.Item.HasLinux);
        StatusText.Text = $"{installed} of {_rows.Count} emulator families have at least one portable build installed.";
    }

    private async void AddExecutableClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string platform, DataContext: EmulatorLibraryRow row }) return;
        var windows = platform == "windows";
        var expected = _library.ExpectedFolder(_mediaRoot, row.Item.Definition, windows);
        Directory.CreateDirectory(expected);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose {row.Item.Definition.DisplayName} from {expected}", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(windows ? "Windows executable" : "Linux executable or AppImage")
            { Patterns = windows ? ["*.exe"] : ["*.AppImage", "*"] }]
        });
        if (files.Count == 0) return;
        var selected = files[0].Path.LocalPath;
        if (!_library.IsInExpectedFolder(_mediaRoot, row.Item.Definition, windows, selected))
        {
            StatusText.Text = $"Not added. Put the complete portable {row.Item.Definition.DisplayName} build inside {expected}, then select its executable. CLC does not copy only one file because emulators usually require companion files.";
            return;
        }
        StatusText.Text = $"Added {row.Item.Definition.DisplayName} for {(windows ? "Windows" : "Linux")}.";
        Reload();
    }

    private void RescanClicked(object? sender, RoutedEventArgs e) => Reload();
    private void DoneClicked(object? sender, RoutedEventArgs e) => Close(true);
}

public sealed class EmulatorLibraryRow
{
    public EmulatorLibraryRow(InstalledEmulatorOption item) => Item = item;
    public InstalledEmulatorOption Item { get; }
    public EmulatorDefinition Definition => Item.Definition;
    public string WindowsStatus => Item.WindowsExecutable is null ? "Missing" : Path.GetFileName(Item.WindowsExecutable);
    public string LinuxStatus => Item.LinuxExecutable is null ? "Missing" : Path.GetFileName(Item.LinuxExecutable);
    public IBrush WindowsColor => Item.HasWindows ? Brushes.LightGreen : Brushes.Gray;
    public IBrush LinuxColor => Item.HasLinux ? Brushes.LightGreen : Brushes.Gray;
}
