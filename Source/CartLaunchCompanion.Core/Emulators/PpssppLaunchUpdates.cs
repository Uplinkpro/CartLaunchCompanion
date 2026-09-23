using System.Runtime.InteropServices;
using System.Text.Json;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public sealed record EmulatorLaunchCheck(EmulatorRelease? Release = null, string? BlockingReason = null);
public interface IEmulatorLaunchUpdates
{
    Task<EmulatorLaunchCheck> CheckAsync(GameLaunchRequest request, CancellationToken cancellationToken = default);
    Task SkipVersionAsync(EmulatorRelease release, CancellationToken cancellationToken = default);
    Task InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default);
    Task EnsureReadyAsync(GameLaunchRequest request, CancellationToken cancellationToken = default);
    Task<IDisposable?> AcquireLaunchLeaseAsync(GameLaunchRequest request, CancellationToken cancellationToken = default);
}

public sealed record EmulatorLaunchUpdateOptions
{
    public TimeSpan FailureBackoff { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>Optional release checks for exact managed executable targets; recovery is mandatory before launch.</summary>
public sealed class PpssppLaunchUpdates(
    string mediaRoot,
    string stateRoot,
    IEmulatorRegistryStore registry,
    IEmulatorCatalogSource catalog,
    IEmulatorReleaseAdapter adapter,
    IManagedEmulatorInstaller installer,
    EmulatorLaunchUpdateOptions? options = null,
    TimeProvider? timeProvider = null) : IEmulatorLaunchUpdates
{
    private readonly EmulatorLaunchUpdateOptions _options = options ?? new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<PlatformKind, PpssppUpdateState> _memory = [];
    private readonly PpssppUpdateStateStore _stateStore = new(stateRoot);
    private readonly EmulatorUpdateSettingsStore _settings = new(stateRoot);

    public PpssppLaunchUpdates(string root, IEmulatorRegistryStore registry, IEmulatorCatalogSource catalog,
        IEmulatorReleaseAdapter adapter, IManagedEmulatorInstaller installer,
        EmulatorLaunchUpdateOptions? options = null, TimeProvider? timeProvider = null)
        : this(root, root, registry, catalog, adapter, installer, options, timeProvider) { }

    private bool Applies(GameLaunchRequest request)
    {
        if (!request.Target.Enabled || request.Target.Platform is not (PlatformKind.Windows or PlatformKind.Linux) ||
            request.Target.Launcher is not (LauncherKind.Local or LauncherKind.Custom) ||
            !Path.IsPathRooted(request.Target.Executable)) return false;
        var executable = request.Target.Platform == PlatformKind.Windows ? "PPSSPPWindows64.exe" : "PPSSPP.AppImage";
        var expected = Path.GetFullPath(Path.Combine(mediaRoot, "Emulators", request.Target.Platform.ToString(), "PPSSPP", executable));
        return Path.GetFullPath(request.Target.Executable).Equals(expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public async Task EnsureReadyAsync(GameLaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (!Applies(request)) return;
        await installer.RecoverAsync(request.Target.Platform, cancellationToken);
        var relative = Path.GetRelativePath(mediaRoot, request.Target.Executable).Replace(Path.DirectorySeparatorChar, '/');
        EmulatorPathContract.Resolve(mediaRoot, relative);
    }

    public async Task<IDisposable?> AcquireLaunchLeaseAsync(GameLaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (!Applies(request)) return null;
        await EnsureReadyAsync(request, cancellationToken);
        var platform = request.Target.Platform;
        try
        {
            if (!(await registry.LoadAsync(cancellationToken)).Installations.Any(i => i.EmulatorId == "ppsspp" && i.Platform == platform))
                return null;
        }
        catch (Exception error) when (IsUnavailable(error)) { return null; }
        var lockPath = EmulatorPathContract.Resolve(mediaRoot, $"Emulators/{platform}/.ppsspp-install.lock");
        var lease = File.Exists(lockPath)
            ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None)
            : new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(EmulatorPathContract.Resolve(mediaRoot, $"Emulators/{platform}/.ppsspp-transaction.json")))
                throw new IOException("PPSSPP has an interrupted update. Refresh Emulator Companion before launching.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    public async Task<EmulatorLaunchCheck> CheckAsync(GameLaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (!Applies(request)) return new();
        try { await EnsureReadyAsync(request, cancellationToken); }
        catch (Exception error) when (IsUnavailable(error))
        { return new(BlockingReason: "PPSSPP needs recovery before this game can launch. Open Emulator Companion. " + error.Message); }

        var platform = request.Target.Platform;
        try
        {
            var preferences = await _settings.LoadAsync(cancellationToken);
            if (!preferences.CheckBeforeLaunch) return new();
            var freshness = TimeSpan.FromHours(preferences.CheckIntervalHours);
            var installed = (await registry.LoadAsync(cancellationToken)).Installations
                .SingleOrDefault(i => i.EmulatorId == "ppsspp" && i.Platform == platform && i.InstalledChannelId == "stable");
            if (installed?.InstalledVersion is null) return new();
            var installedPath = EmulatorPathContract.Resolve(mediaRoot, installed.ExecutableRelativePath);
            if (!installedPath.Equals(Path.GetFullPath(request.Target.Executable),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return new();
            var state = await ReadStateAsync(platform, cancellationToken);
            var now = _time.GetUtcNow();
            if (state.CheckedAt is not { } checkedAt || checkedAt > now || now - checkedAt >= freshness)
            {
                if (state.RetryAfter > now) return new();
                // Take the cross-process cache lock only for refreshes and skip writes.
                using var cacheLock = _stateStore.Lock(platform);
                state = await ReadStateAsync(platform, cancellationToken, useMemory: false);
                if (state.CheckedAt is not { } refreshed || refreshed > now || now - refreshed >= freshness)
                {
                    if (state.RetryAfter > now) return new();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(_options.CheckTimeout);
                    try
                    {
                        var definition = (await catalog.LoadAsync(deadline.Token)).Emulators.Single(e => e.Id == "ppsspp");
                        var channel = definition.ReleaseChannels.Single(c => c.Id == installed.InstalledChannelId && c.IsStable);
                        var release = await adapter.FindLatestAsync(channel, platform, Architecture.X64, deadline.Token);
                        deadline.Token.ThrowIfCancellationRequested();
                        if (release is not null) ValidateRelease(release, platform);
                        state = state with { CheckedAt = now, RetryAfter = null, Release = release };
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    { state = state with { RetryAfter = now + _options.FailureBackoff, Release = null, CheckedAt = null }; }
                    catch (Exception error) when (IsUnavailable(error) || error is InvalidOperationException)
                    { state = state with { RetryAfter = now + _options.FailureBackoff, Release = null, CheckedAt = null }; }
                    _memory[platform] = state;
                    try { await _stateStore.SaveAsync(platform, state, cancellationToken); }
                    catch (Exception error) when (IsUnavailable(error)) { /* Read-only media: retain session cache. */ }
                }
            }
            if (state.Release is not { } candidate || state.SkippedVersion == candidate.Version ||
                PpssppInstaller.CompareVersions(candidate.Version, installed.InstalledVersion) <= 0) return new();
            var status = await installer.InspectAsync(candidate, cancellationToken);
            return status.Action == EmulatorInstallAction.Update ? new(candidate) : new();
        }
        catch (Exception error) when (IsUnavailable(error))
        {
            // Release checks must not make an otherwise valid existing installation unlaunchable.
            return new();
        }
    }

    public Task InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default) =>
        installer.InstallAsync(release, cancellationToken);

    public async Task SkipVersionAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
    {
        ValidateRelease(release, release.Platform);
        using var cacheLock = _stateStore.Lock(release.Platform);
        var state = await ReadStateAsync(release.Platform, cancellationToken, useMemory: false);
        state = state with { SkippedVersion = release.Version };
        await _stateStore.SaveAsync(release.Platform, state, cancellationToken);
        _memory[release.Platform] = state;
    }

    private static void ValidateRelease(EmulatorRelease release, PlatformKind platform)
    {
        PpssppInstaller.Validate(release);
        if (release.Platform != platform) throw new InvalidDataException("Cached package platform mismatch.");
    }

    private async Task<PpssppUpdateState> ReadStateAsync(PlatformKind platform, CancellationToken token, bool useMemory = true)
    {
        // Disk first so skip choices from another Launcher instance take effect.
        try
        {
            var state = await _stateStore.LoadAsync(platform, token);
            if (useMemory && _memory.TryGetValue(platform, out var recent) &&
                (recent.CheckedAt ?? recent.RetryAfter - _options.FailureBackoff ?? DateTimeOffset.MinValue) >
                (state.CheckedAt ?? state.RetryAfter - _options.FailureBackoff ?? DateTimeOffset.MinValue))
                return recent with { SkippedVersion = state.SkippedVersion };
            return state;
        }
        catch (Exception error) when (IsUnavailable(error))
        { return useMemory && _memory.TryGetValue(platform, out var memory) ? memory : new(); }
    }

    private static bool IsUnavailable(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or HttpRequestException or NotSupportedException;
}
