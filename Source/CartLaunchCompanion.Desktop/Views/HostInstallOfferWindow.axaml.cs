using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Desktop.Views;

public sealed partial class HostInstallOfferWindow : Window
{
    private readonly string _hostExecutable;
    public bool IsWindows { get; }
    public HostInstallOfferWindow() { _hostExecutable = ""; InitializeComponent(); DataContext = this; }
    public HostInstallOfferWindow(string cartRoot, PlatformKind platform)
    {
        IsWindows = platform == PlatformKind.Windows;
        var folder = IsWindows ? "Windows-x64" : "Linux-x64";
        var executable = IsWindows ? "CartLaunchCompanion.Host.exe" : "CartLaunchCompanion.Host";
        _hostExecutable = Path.Combine(cartRoot, "Host", folder, executable);
        InitializeComponent(); DataContext = this;
    }
    private void NotNowClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void InstallClicked(object? sender, RoutedEventArgs e)
    {
        if (!File.Exists(_hostExecutable)) { Close(false); return; }
        Process.Start(new ProcessStartInfo { FileName = _hostExecutable, WorkingDirectory = Path.GetDirectoryName(_hostExecutable)!, UseShellExecute = true });
        Close(true);
    }
}
