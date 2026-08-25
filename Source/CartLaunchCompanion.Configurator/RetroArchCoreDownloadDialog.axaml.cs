using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed partial class RetroArchCoreDownloadDialog : Window
{
    private readonly string _coresFolder = "";
    private readonly RetroArchCoreDownloadService _downloadService = new();

    public RetroArchCoreDownloadDialog() => InitializeComponent();

    public RetroArchCoreDownloadDialog(IReadOnlyList<string> coreNames, string romPath, string coresFolder)
    {
        InitializeComponent();
        _coresFolder = coresFolder;
        CoreBox.ItemsSource = coreNames;
        CoreBox.SelectedIndex = coreNames.Count > 0 ? 0 : -1;
        ExplanationText.Text = $"No installed core can open {Path.GetExtension(romPath)} files. CLC can download one directly from Libretro's official buildbot into this portable RetroArch copy.";
        UpdateDetails();
    }

    private void CoreSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateDetails();

    private void UpdateDetails()
    {
        if (CoreBox.SelectedItem is not string coreName) return;
        SourceText.Text = RetroArchCoreDownloadService.GetDownloadUri(coreName).ToString();
        DestinationText.Text = Path.Combine(_coresFolder, RetroArchCoreDownloadService.GetBinaryName(coreName));
    }

    private async void DownloadClicked(object? sender, RoutedEventArgs e)
    {
        if (CoreBox.SelectedItem is not string coreName) return;
        DownloadButton.IsEnabled = false;
        CoreBox.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusText.Text = $"Downloading {coreName} from Libretro…";
        try
        {
            var installedPath = await _downloadService.DownloadAsync(coreName, _coresFolder);
            Close(installedPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = "The core was not installed: " + ex.Message;
            DownloadButton.IsEnabled = true;
            CoreBox.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
