using System.Collections.ObjectModel;
using System.ComponentModel;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed class UninstallViewModel : INotifyPropertyChanged
{
    private readonly EmulatorUninstallService _service;
    private readonly string _emulatorId;
    private ReleasePlatformOption _selectedPlatform;

    public UninstallViewModel(LibraryRow row, EmulatorUninstallService service)
    {
        _service = service;
        _emulatorId = row.Entry.EmulatorId;
        DisplayName = row.Name;
        var installed = row.Entry.Installations.Select(item => item.Platform).Distinct().ToArray();
        var options = installed.Select(platform => new ReleasePlatformOption(platform, platform + " x64")).ToList();
        if (installed.Length > 1) options.Add(new(null, "Both installed builds"));
        Platforms = options;
        var host = OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Windows;
        _selectedPlatform = options.FirstOrDefault(option => option.Platform == host) ?? options[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string DisplayName { get; }
    public string Title => "Uninstall " + DisplayName;
    public IReadOnlyList<ReleasePlatformOption> Platforms { get; }
    public ReleasePlatformOption SelectedPlatform
    {
        get => _selectedPlatform;
        set { if (IsBusy || _selectedPlatform == value) return; _selectedPlatform = value; Notify(); }
    }
    public bool IsWindowsSelected { get => SelectedPlatform.Platform == PlatformKind.Windows; set { if (value) Select(PlatformKind.Windows); } }
    public bool IsLinuxSelected { get => SelectedPlatform.Platform == PlatformKind.Linux; set { if (value) Select(PlatformKind.Linux); } }
    public bool IsBothSelected { get => SelectedPlatform.Platform is null; set { if (value) Select(null); } }
    public bool ShowWindows => Platforms.Any(item => item.Platform == PlatformKind.Windows);
    public bool ShowLinux => Platforms.Any(item => item.Platform == PlatformKind.Linux);
    public bool ShowBoth => Platforms.Any(item => item.Platform is null);
    public bool PreservePersonalData { get; set; } = true;
    public bool IsBusy { get; private set; }
    public bool CanUninstall => !IsBusy;
    public bool Completed { get; private set; }
    public bool ShowUninstallAction => !Completed;
    public ObservableCollection<string> Activity { get; } = [];
    public bool HasActivity => Activity.Count > 0;
    public string Message { get; private set; } = "ROM folders are never removed.";

    public async Task UninstallAsync(CancellationToken token = default)
    {
        if (!CanUninstall) return;
        IsBusy = true; Message = "Uninstalling…"; Activity.Clear(); Notify();
        try
        {
            var targets = SelectedPlatform.Platform is { } platform
                ? [platform] : Platforms.Where(item => item.Platform is not null).Select(item => item.Platform!.Value).ToArray();
            var messages = new List<string>();
            var progress = new Progress<string>(message => { Activity.Add(message); Notify(); });
            foreach (var target in targets)
                messages.Add((await _service.UninstallAsync(_emulatorId, target, PreservePersonalData, token, progress)).Message);
            Message = string.Join(" ", messages.Distinct(StringComparer.Ordinal));
            Completed = true;
        }
        finally { IsBusy = false; Notify(); }
    }

    public void Report(string message) { Message = message; Notify(); }
    private void Select(PlatformKind? platform)
    {
        var option = Platforms.FirstOrDefault(item => item.Platform == platform);
        if (option is not null) SelectedPlatform = option;
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
