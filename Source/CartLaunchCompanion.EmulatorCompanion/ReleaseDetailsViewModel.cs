using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed record ReleasePlatformOption(PlatformKind? Platform, string DisplayName);

public sealed record ReleasePlatformResult(PlatformKind Platform, EmulatorRelease? Release,
    EmulatorInstallStatus? InstallStatus, string Message, bool Completed = false)
{
    public bool HasRelease => Release is not null;
    public string Heading => Release is { } r ? $"{Platform} · {r.Version} · {(r.IsPrerelease ? "Prerelease" : "Stable")}" : Platform.ToString();
    public string PackageName => Release?.AssetName ?? "";
    public string PackageDetails => Release is { } r ? $"{Platform} x64 · {r.SizeBytes / 1048576d:0.0} MiB · Published {r.PublishedAt:yyyy-MM-dd}" : "";
    public string ChecksumStatus => Release is null ? "" : Completed
        ? "Download checksum verified. Installation completed."
        : Release.Sha256 is null ? "The publisher has not provided a checksum for this package."
        : "Publisher checksum available. The package has not been downloaded.";
    public Uri? ReleasePage => Release?.ReleasePage;
}

/// <summary>One user operation at a time, with independent results and commits for each target platform.</summary>
public sealed class ReleaseDetailsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEmulatorReleaseAdapter _adapter;
    private readonly IEmulatorInstaller? _installer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _disposed;
    private ReleasePlatformOption? _selectedPlatform;
    private EmulatorReleaseChannel? _selectedChannel;

    public ReleaseDetailsViewModel(EmulatorDefinition definition, IEmulatorReleaseAdapter adapter, IEmulatorInstaller? installer = null)
    {
        Definition = definition;
        _adapter = adapter;
        _installer = installer;
        _selectedPlatform = Platforms.First(p => p.Platform ==
            (OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Windows));
        var stable = Channels.Where(c => c.IsStable && c.SupportedPlatforms.Contains(_selectedPlatform.Platform!.Value)).ToArray();
        _selectedChannel = stable.FirstOrDefault(c => c.Id == definition.DefaultChannelId)
            ?? (stable.Length == 1 ? stable[0] : null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public EmulatorDefinition Definition { get; }
    public string Title => Definition.DisplayName + " releases";
    public IReadOnlyList<EmulatorReleaseChannel> Channels => Definition.ReleaseChannels;
    public IReadOnlyList<ReleasePlatformOption> Platforms { get; } =
        [new(PlatformKind.Windows, "Windows x64"), new(PlatformKind.Linux, "Linux x64"), new(null, "Both")];

    public ReleasePlatformOption? SelectedPlatform
    {
        get => _selectedPlatform;
        set { if (IsInstalling || _selectedPlatform == value) return; _selectedPlatform = value; Invalidate(); }
    }
    public bool IsWindowsSelected
    {
        get => SelectedPlatform?.Platform == PlatformKind.Windows;
        set { if (value) SelectedPlatform = Platforms[0]; }
    }
    public bool IsLinuxSelected
    {
        get => SelectedPlatform?.Platform == PlatformKind.Linux;
        set { if (value) SelectedPlatform = Platforms[1]; }
    }
    public bool IsBothSelected
    {
        get => SelectedPlatform?.Platform is null;
        set { if (value) SelectedPlatform = Platforms[2]; }
    }
    public EmulatorReleaseChannel? SelectedChannel
    {
        get => _selectedChannel;
        set { if (IsInstalling || _selectedChannel == value) return; _selectedChannel = value; Invalidate(); }
    }
    private IReadOnlyList<PlatformKind> Targets => SelectedPlatform?.Platform is { } platform
        ? [platform] : [PlatformKind.Windows, PlatformKind.Linux];
    public string ChannelDescription => SelectedChannel?.Description ?? "";
    public bool IsChecking { get; private set; }
    public bool IsInstalling { get; private set; }
    public bool IsBusy => IsChecking || IsInstalling;
    public string BusyHeading => IsInstalling ? "Installing and preparing the emulator" : "Checking available releases";
    public bool CanSelect => !IsInstalling;
    public bool CanCheck => !_disposed && !IsChecking && !IsInstalling && Definition.Id == _adapter.EmulatorId &&
        SelectedPlatform is not null && Platforms.Contains(SelectedPlatform) &&
        SelectedChannel is not null && Channels.Contains(SelectedChannel) &&
        Targets.All(SelectedChannel.SupportedPlatforms.Contains);

    public IReadOnlyList<ReleasePlatformResult> Results { get; private set; } = [];
    public bool HasResults => Results.Count > 0;
    public bool HasRelease => Results.Any(row => row.HasRelease);
    public EmulatorRelease? Release => Results.FirstOrDefault(row => row.HasRelease)?.Release;
    public EmulatorInstallStatus? InstallStatus => Results.FirstOrDefault()?.InstallStatus;
    public bool Installed => HasResults && Results.All(row => row.Completed);
    private bool IsEligible(ReleasePlatformResult row) => !row.Completed && row.Release?.Sha256 is { Length: 64 } &&
        (_installer is not IManagedEmulatorInstaller || row.InstallStatus?.Action is EmulatorInstallAction.Install or EmulatorInstallAction.Update);
    public bool CanInstall => !_disposed && !IsInstalling && !IsChecking && _installer is not null && Results.Any(IsEligible);
    public string InstallActionLabel => _installer is null ? "Installation support is not available yet" :
        !HasResults ? "2. Check for a release first" :
        SelectedPlatform?.Platform is null ? "2. Download available builds" :
        InstallStatus?.Action == EmulatorInstallAction.Update ? "2. Download and update" :
        InstallStatus?.Action == EmulatorInstallAction.Current ? "Already up to date" :
        InstallStatus?.Action is EmulatorInstallAction.NewerInstalled or EmulatorInstallAction.Blocked ? "Installation unavailable" :
        "2. Download and install";
    public string InstallLocation { get; init; } = "";
    public string Message { get; private set; } = "Choose a platform and channel, then complete step 1. The install action will become available when a supported release is ready.";
    public string ReleaseSummary => Results.FirstOrDefault(row => row.HasRelease)?.Heading ?? "";
    public string PackageName => Results.FirstOrDefault(row => row.HasRelease)?.PackageName ?? "";
    public string PackageDetails => Results.FirstOrDefault(row => row.HasRelease)?.PackageDetails ?? "";
    public string ChecksumStatus => Results.FirstOrDefault(row => row.HasRelease)?.ChecksumStatus ?? "";
    public Uri? ReleasePage => Release?.ReleasePage;

    public async Task InstallAsync()
    {
        if (!CanInstall) return;
        var candidates = Results.Where(IsEligible).ToArray();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _pending = pending;
        IsInstalling = true;
        NotifyAll();
        try
        {
            foreach (var row in candidates)
            {
                if (pending.IsCancellationRequested)
                {
                    Replace(row with { Message = "Not installed: the operation was cancelled." });
                    continue;
                }
                Message = $"Downloading, verifying, and installing {Definition.DisplayName} for {row.Platform}…";
                NotifyAll();
                try
                {
                    await _installer!.InstallAsync(row.Release!, pending.Token);
                    var message = row.Platform == PlatformKind.Linux && !OperatingSystem.IsLinux()
                        ? "Installed. On Linux, enable executable permission before the first launch."
                        : row.InstallStatus?.Action == EmulatorInstallAction.Update
                            ? $"{Definition.DisplayName} updated. Settings and saves were preserved."
                            : $"{Definition.DisplayName} installed and recorded in the library.";
                    Replace(row with { Completed = true, Message = message });
                }
                catch (OperationCanceledException)
                {
                    Replace(row with { Message = "Installation cancelled or timed out. This build was not completed." });
                    break;
                }
                catch (Exception error) when (IsDataError(error) || error is HttpRequestException)
                {
                    Replace(row with { Message = "Installation could not complete: " + error.Message });
                    // Keep successful builds and allow an independent platform to complete.
                }
            }
            Message = Results.Count == 1 ? Results[0].Message :
                string.Join(" ", Results.Select(row => $"{row.Platform}: {row.Message}"));
        }
        finally
        {
            _pending = null;
            IsInstalling = false;
            NotifyAll();
        }
    }

    private void Replace(ReleasePlatformResult result)
    {
        Results = Results.Select(row => row.Platform == result.Platform ? result : row).ToArray();
        NotifyAll();
    }
    public void CancelInstallation() => _pending?.Cancel();

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!CanCheck) return;
        var generation = _generation;
        var targets = Targets.ToArray();
        var channel = SelectedChannel!;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _pending = pending;
        IsChecking = true;
        Results = [];
        Message = "Checking official releases…";
        NotifyAll();
        var results = new List<ReleasePlatformResult>();
        try
        {
            foreach (var platform in targets)
            {
                pending.Token.ThrowIfCancellationRequested();
                ReleasePlatformResult result;
                try
                {
                    var release = await _adapter.FindLatestAsync(channel, platform, Architecture.X64, pending.Token);
                    var status = release is not null && _installer is IManagedEmulatorInstaller managed
                        ? await managed.InspectAsync(release, pending.Token) : null;
                    pending.Token.ThrowIfCancellationRequested();
                    result = new(platform, release, status,
                        release is null ? "No matching package was found for this platform and channel." : status?.Message ?? "Release information loaded.");
                }
                catch (OperationCanceledException) when (!pending.IsCancellationRequested)
                { result = new(platform, null, null, "The release check timed out. Try again."); }
                catch (HttpRequestException error)
                {
                    result = new(platform, null, null,
                        error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                        ? "The release service is limiting requests. Please try again later."
                        : "The release service could not be reached. Check your connection and try again.");
                }
                catch (Exception error) when (IsDataError(error))
                { result = new(platform, null, null, "Release information could not be validated for this selection. Please try again later."); }
                if (generation != _generation || _disposed) return;
                results.Add(result);
                Results = results.ToArray();
                NotifyAll();
            }
            Message = results.Count == 1 ? results[0].Message : "Release checks complete. Review each platform below.";
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation && !_disposed) Message = "Release check cancelled.";
        }
        finally
        {
            if (ReferenceEquals(_pending, pending)) _pending = null;
            IsChecking = false;
            NotifyAll();
        }
    }

    public void ReportReleaseNotesFailure()
    {
        Message = "The release page could not be opened. Check your default browser.";
        NotifyAll();
    }
    private void Invalidate()
    {
        _generation++;
        _pending?.Cancel();
        Results = [];
        var unsupported = SelectedChannel is { } channel && Targets.Any(platform => !channel.SupportedPlatforms.Contains(platform));
        Message = unsupported
            ? $"{SelectedChannel!.DisplayName} currently supports {string.Join(" and ", SelectedChannel.SupportedPlatforms.Select(DisplayPlatform))} only for {Definition.DisplayName}. Choose a supported platform."
            : "Selection changed. Complete step 1 to see matching packages.";
        NotifyAll();
    }
    private static string DisplayPlatform(PlatformKind platform) => platform switch
    {
        PlatformKind.Windows => "Windows x64",
        PlatformKind.Linux => "Linux x64",
        _ => platform.ToString()
    };
    private static bool IsDataError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException;
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_adapter is IDisposable disposable) disposable.Dispose();
        if (_installer is IDisposable installer) installer.Dispose();
    }
}
