using System.ComponentModel;
using System.Text.Json;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed record UpdateIntervalOption(int Hours, string Label);

public sealed class UpdateSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IEmulatorUpdateSettings store;
    private readonly CancellationToken _token;
    public UpdateSettingsViewModel(IEmulatorUpdateSettings store)
    {
        this.store = store;
        _token = _lifetime.Token;
    }
    private bool _loaded;
    private bool _disposed;
    private EmulatorUpdatePreferences? _saved;
    private bool _checkBeforeLaunch = true;
    private UpdateIntervalOption? _interval;
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<UpdateIntervalOption> Intervals { get; } =
        [new(6, "Every 6 hours"), new(24, "Once a day"), new(168, "Once a week")];
    public bool IsBusy { get; private set; }
    public bool CanEdit => _loaded && !IsBusy && !_disposed;
    public bool CanSave => CanEdit && SelectedInterval is not null && Intervals.Contains(SelectedInterval) &&
        (_saved?.CheckBeforeLaunch != CheckBeforeLaunch || _saved?.CheckIntervalHours != SelectedInterval.Hours);
    public bool CheckBeforeLaunch
    {
        get => _checkBeforeLaunch;
        set { if (!CanEdit || _checkBeforeLaunch == value) return; _checkBeforeLaunch = value; Notify(); }
    }
    public UpdateIntervalOption? SelectedInterval
    {
        get => _interval;
        set { if (!CanEdit || _interval == value) return; _interval = value; Notify(); }
    }
    public string? WindowsSkippedVersion { get; private set; }
    public string? LinuxSkippedVersion { get; private set; }
    public string WindowsStatus { get; private set; } = "Loading…";
    public string LinuxStatus { get; private set; } = "Loading…";
    public bool CanClearWindows => !IsBusy && !_disposed && WindowsSkippedVersion is not null;
    public bool CanClearLinux => !IsBusy && !_disposed && LinuxSkippedVersion is not null;
    public string Message { get; private set; } = "Loading update settings…";

    public async Task LoadAsync()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        Notify();
        try
        {
            try
            {
                _saved = await store.LoadAsync(_token);
                _checkBeforeLaunch = _saved.CheckBeforeLaunch;
                _interval = Intervals.Single(item => item.Hours == _saved.CheckIntervalHours);
                _loaded = true;
                Message = "Changes apply to the next game launch. Manual release checks remain available.";
            }
            catch (Exception error) when (IsDataError(error))
            {
                _loaded = false;
                Message = "Update preferences could not be loaded: " + error.Message;
            }
            await LoadSkippedAsync(PlatformKind.Windows);
            await LoadSkippedAsync(PlatformKind.Linux);
        }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; Notify(); }
    }

    private async Task LoadSkippedAsync(PlatformKind platform)
    {
        string? version = null;
        string status;
        try
        {
            version = await store.GetSkippedVersionAsync(platform, _token);
            status = version is null ? "No version skipped" : "Skipped " + version;
        }
        catch (Exception error) when (IsDataError(error)) { status = "Could not load the skipped version. Reopen settings to retry."; }
        if (platform == PlatformKind.Windows) { WindowsSkippedVersion = version; WindowsStatus = status; }
        else { LinuxSkippedVersion = version; LinuxStatus = status; }
    }

    public async Task SaveAsync()
    {
        if (!CanSave) return;
        var preferences = new EmulatorUpdatePreferences { CheckBeforeLaunch = CheckBeforeLaunch, CheckIntervalHours = SelectedInterval!.Hours };
        IsBusy = true;
        Notify();
        try
        {
            await store.SaveAsync(preferences, _token);
            _saved = preferences;
            Message = "Update preferences saved.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (IsDataError(error)) { Message = "Preferences were not saved: " + error.Message; }
        finally { IsBusy = false; Notify(); }
    }

    public async Task ClearSkippedAsync(PlatformKind platform)
    {
        if (platform == PlatformKind.Windows ? !CanClearWindows : platform != PlatformKind.Linux || !CanClearLinux) return;
        IsBusy = true;
        Notify();
        try
        {
            await store.ClearSkippedVersionAsync(platform, _token);
            await LoadSkippedAsync(platform);
            Message = "Skipped version cleared for " + platform + ". It can be offered again when launch checks are enabled.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (IsDataError(error)) { Message = "The skipped version was not cleared: " + error.Message; }
        finally { IsBusy = false; Notify(); }
    }

    private static bool IsDataError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}