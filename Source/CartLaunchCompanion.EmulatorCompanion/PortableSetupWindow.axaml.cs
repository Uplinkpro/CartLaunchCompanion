using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed partial class PortableSetupWindow : Window
{
    private readonly PortableSetupViewModel _model = null!;
    public PortableSetupWindow() { InitializeComponent(); GamepadMenuNavigation.Attach(this); }
    public PortableSetupWindow(PortableSetupViewModel model) : this()
    {
        _model = model; DataContext = model;
        Opened += async (_, _) => await SafeAsync(() => model.RefreshAsync());
    }
    private async void ApplyClicked(object? sender, RoutedEventArgs args) => await SafeAsync(() => _model.ApplyAsync());
    private async void ImportContentClicked(object? sender, RoutedEventArgs args) =>
        await SafeAsync(() => _model.ImportContentAsync());
    private async void OpenClicked(object? sender, RoutedEventArgs args)
    {
        if (_model.CurrentHostTarget is not { } target) return;
        try
        {
            var start = new ProcessStartInfo(target.ExecutablePath)
            {
                WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath)!, UseShellExecute = true
            };
            if (_model.Definition.EmulatorId == "ppsspp")
                foreach (var argument in PpssppPortablePaths.AddLaunchArguments(target.ExecutablePath,
                    target.Platform, [])) start.ArgumentList.Add(argument);
            var process = Process.Start(start);
            if (process is null) { _model.Report("Could not open the emulator."); return; }
            using (process)
            {
                _model.SetExternalAppRunning(true);
                _model.Report($"{_model.Definition.DisplayName} is open. Complete the step shown there, then close it to continue here.");
                await process.WaitForExitAsync();
            }
            _model.SetExternalAppRunning(false);
            await SafeAsync(() => _model.RefreshAsync());
            if (_model.CanApply) await SafeAsync(() => _model.ApplyAsync());
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { _model.Report("Could not open the emulator: " + error.Message); }
        finally { _model.SetExternalAppRunning(false); }
    }
    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { _model.Report(error.Message); }
    }
    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
