using System.Runtime.InteropServices;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PpssppLaunchUpdatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-launch-updates-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private readonly Adapter _adapter = new();
    private readonly Installer _installer = new();
    private static EmulatorRelease Release(string version = "v1.20.4") => new()
    {
        EmulatorId = "ppsspp", ChannelId = "stable", Platform = PlatformKind.Windows,
        Architecture = Architecture.X64, Version = version, RevisionId = version, PublishedAt = DateTimeOffset.UtcNow,
        IsPrerelease = false, ReleasePage = new("https://github.com/hrydgard/ppsspp/releases/tag/" + version),
        AssetName = $"PPSSPP-{version}-Windows-x64.zip",
        DownloadUrl = new($"https://github.com/hrydgard/ppsspp/releases/download/{version}/PPSSPP-{version}-Windows-x64.zip"),
        SizeBytes = 100, PackageFormat = EmulatorPackageFormat.Zip, Sha256 = new string('a', 64)
    };
    private GameLaunchRequest Request => new("Game", _root,
        new(PlatformKind.Windows, LauncherKind.Local, true, "", "",
            Path.Combine(_root, "Emulators", "Windows", "PPSSPP", "PPSSPPWindows64.exe"), "", "", "", "", "", ""),
        new());
    private PpssppLaunchUpdates Service(TimeSpan? timeout = null) => new(
        _root, new EmulatorRegistryStore(_root), new Catalog(), _adapter, _installer,
        new() { CheckTimeout = timeout ?? TimeSpan.FromSeconds(2) }, _clock);
    private async Task SeedAsync(string version = "v1.20.3")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Request.Target.Executable)!);
        await File.WriteAllTextAsync(Request.Target.Executable, "fixture");
        await new EmulatorRegistryStore(_root).UpsertAsync(new()
        {
            EmulatorId = "ppsspp", Platform = PlatformKind.Windows,
            ExecutableRelativePath = "Emulators/Windows/PPSSPP/PPSSPPWindows64.exe",
            InstalledVersion = version, InstalledChannelId = "stable", InstalledAt = _clock.GetUtcNow()
        });
    }

    [Fact]
    public async Task UnrelatedLaunchDoesNotCheckOrRecover()
    {
        var request = Request with { Target = Request.Target with { Launcher = LauncherKind.Steam } };
        Assert.Null((await Service().CheckAsync(request)).Release);
        Assert.Equal(0, _adapter.Calls);
        Assert.Equal(0, _installer.Recoveries);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task NestedCartKeepsStateInCartAndUsesRootLevelEmulator()
    {
        var mediaRoot = Path.Combine(_root, "Media");
        var stateRoot = Path.Combine(mediaRoot, "Cart");
        var executable = Path.Combine(mediaRoot, "Emulators", "Windows", "PPSSPP", "PPSSPPWindows64.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "fixture");
        var registry = new EmulatorRegistryStore(stateRoot);
        await registry.UpsertAsync(new()
        {
            EmulatorId = "ppsspp", Platform = PlatformKind.Windows,
            ExecutableRelativePath = "Emulators/Windows/PPSSPP/PPSSPPWindows64.exe",
            InstalledVersion = "v1.20.3", InstalledChannelId = "stable", InstalledAt = _clock.GetUtcNow()
        });
        var request = Request with
        {
            GameFolder = stateRoot,
            Target = Request.Target with { Executable = executable }
        };
        var service = new PpssppLaunchUpdates(mediaRoot, stateRoot, registry, new Catalog(), _adapter, _installer,
            new EmulatorLaunchUpdateOptions(), _clock);

        Assert.NotNull((await service.CheckAsync(request)).Release);
        Assert.True(File.Exists(Path.Combine(stateRoot, "Config", "ppsspp-updates-Windows.json")));
        Assert.False(Directory.Exists(Path.Combine(mediaRoot, "Config")));
    }

    [Fact]
    public async Task UnregisteredCopyDoesNotCheckOnline()
    {
        Assert.Null((await Service().CheckAsync(Request)).Release);
        Assert.Equal(0, _adapter.Calls);
    }

    [Fact]
    public async Task CacheSurvivesServiceRestartAndExpiresAfter24Hours()
    {
        await SeedAsync();
        Assert.NotNull((await Service().CheckAsync(Request)).Release);
        Assert.NotNull((await Service().CheckAsync(Request)).Release);
        Assert.Equal(1, _adapter.Calls);
        _clock.Now += TimeSpan.FromHours(24);
        Assert.NotNull((await Service().CheckAsync(Request)).Release);
        Assert.Equal(2, _adapter.Calls);
    }

    [Fact]
    public async Task SkipVersionPersistsButNextVersionIsOffered()
    {
        await SeedAsync();
        var service = Service();
        var release = (await service.CheckAsync(Request)).Release!;
        await service.SkipVersionAsync(release);
        Assert.Null((await Service().CheckAsync(Request)).Release);
        _clock.Now += TimeSpan.FromHours(25);
        _adapter.Result = Release("v1.21.0");
        Assert.Equal("v1.21.0", (await Service().CheckAsync(Request)).Release!.Version);
    }

    [Theory]
    [InlineData("v1.20.4")]
    [InlineData("v1.21.0")]
    public async Task CurrentOrNewerInstalledDoesNotPrompt(string installed)
    {
        await SeedAsync(installed);
        Assert.Null((await Service().CheckAsync(Request)).Release);
    }

    [Fact]
    public async Task OfflineLaunchContinuesAndRetriesAfterBackoff()
    {
        await SeedAsync();
        _adapter.Error = new HttpRequestException("Offline");
        Assert.Null((await Service().CheckAsync(Request)).Release);
        Assert.Null((await Service().CheckAsync(Request)).Release);
        Assert.Equal(1, _adapter.Calls);
        _clock.Now += TimeSpan.FromMinutes(16);
        _adapter.Error = null;
        Assert.NotNull((await Service().CheckAsync(Request)).Release);
        Assert.Equal(2, _adapter.Calls);
    }

    [Fact]
    public async Task SlowServiceTimesOutWithoutBlockingLaunch()
    {
        await SeedAsync();
        _adapter.WaitForCancellation = true;
        var result = await Service(TimeSpan.FromMilliseconds(20)).CheckAsync(Request);
        Assert.Null(result.Release);
        Assert.Null(result.BlockingReason);
    }

    [Fact]
    public async Task ShutdownCancellationIsNotTreatedAsOffline()
    {
        await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().CheckAsync(Request, cancellation.Token));
    }

    [Fact]
    public async Task RecoveryFailureBlocksLaunchEvenWhenOffline()
    {
        await SeedAsync();
        _installer.RecoveryError = new IOException("Missing backup");
        var result = await Service().CheckAsync(Request);
        Assert.Contains("recovery", result.BlockingReason);
        Assert.Equal(0, _adapter.Calls);
        await Assert.ThrowsAsync<IOException>(() => Service().EnsureReadyAsync(Request));
    }

    [Fact]
    public async Task LaunchLeaseBlocksInstallerPublicationUntilProcessStarts()
    {
        await SeedAsync();
        using (await Service().AcquireLaunchLeaseAsync(Request))
        {
            using var installer = new PpssppInstaller(_root);
            await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(Release()));
        }
        using var lockFile = new FileStream(Path.Combine(_root, "Emulators", "Windows", ".ppsspp-install.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task FailedSkipWriteIsReportedRatherThanPretendingItWasSaved()
    {
        await SeedAsync();
        await Service().CheckAsync(Request);
        using var held = new FileStream(Path.Combine(_root, "Config", "ppsspp-updates-Windows.json.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => Service().SkipVersionAsync(Release()));
    }

    [Fact]
    public async Task InvalidCachedPackageIsNeverOffered()
    {
        await SeedAsync();
        await Service().CheckAsync(Request);
        var path = Path.Combine(_root, "Config", "ppsspp-updates-Windows.json");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("https://github.com/", "https://untrusted.example/"));
        _adapter.Error = new HttpRequestException("Offline");
        Assert.Null((await Service().CheckAsync(Request)).Release);
    }

    [Fact]
    public async Task FailedCacheWriteRetainsSessionBackoff()
    {
        await SeedAsync();
        var service = Service();
        await service.CheckAsync(Request);
        _clock.Now += TimeSpan.FromHours(25);
        _adapter.Error = new HttpRequestException("Offline");
        using var held = new FileStream(Path.Combine(_root, "Config", "ppsspp-updates-Windows.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Null((await service.CheckAsync(Request)).Release);
        Assert.Null((await service.CheckAsync(Request)).Release);
        Assert.Equal(2, _adapter.Calls);
    }

    [Fact]
    public async Task UnregisteredLaunchDoesNotCreateALock()
    {
        Assert.Null(await Service().AcquireLaunchLeaseAsync(Request));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task ExistingReadOnlyLockStillAllowsLaunch()
    {
        await SeedAsync();
        var path = Path.Combine(_root, "Emulators", "Windows", ".ppsspp-install.lock");
        await File.WriteAllTextAsync(path, "");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try { using var lease = await Service().AcquireLaunchLeaseAsync(Request); Assert.NotNull(lease); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact]
    public async Task MissingReleaseIsCachedWithoutRepeatedRequests()
    {
        await SeedAsync();
        _adapter.Result = null;
        Assert.Null((await Service().CheckAsync(Request)).Release);
        Assert.Null((await Service().CheckAsync(Request)).Release);
        Assert.Equal(1, _adapter.Calls);
    }
    [Fact]
    public async Task DisabledChecksStopRequestsButStillRequireRecovery()
    {
        await SeedAsync();
        await new EmulatorUpdateSettingsStore(_root).SaveAsync(new() { CheckBeforeLaunch = false });
        var result = await Service().CheckAsync(Request);
        Assert.Null(result.Release);
        Assert.Equal(0, _adapter.Calls);
        Assert.True(_installer.Recoveries > 0);
        _installer.RecoveryError = new IOException("Recovery required");
        Assert.NotNull((await Service().CheckAsync(Request)).BlockingReason);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(24)]
    [InlineData(168)]
    public async Task SavedIntervalControlsNextRefreshWithoutRestart(int hours)
    {
        await SeedAsync();
        var service = Service();
        await service.CheckAsync(Request);
        await new EmulatorUpdateSettingsStore(_root).SaveAsync(new() { CheckIntervalHours = hours });
        _clock.Now += TimeSpan.FromHours(hours - 1);
        await service.CheckAsync(Request);
        Assert.Equal(1, _adapter.Calls);
        _clock.Now += TimeSpan.FromHours(1);
        await service.CheckAsync(Request);
        Assert.Equal(2, _adapter.Calls);
    }

    [Fact]
    public async Task ClearingSkipAffectsRunningLauncherWithoutDiscardingCachedRelease()
    {
        await SeedAsync();
        var service = Service();
        var release = (await service.CheckAsync(Request)).Release!;
        await service.SkipVersionAsync(release);
        Assert.Null((await service.CheckAsync(Request)).Release);
        var settings = new EmulatorUpdateSettingsStore(_root);
        Assert.Equal(release.Version, await settings.GetSkippedVersionAsync(PlatformKind.Windows));
        await settings.ClearSkippedVersionAsync(PlatformKind.Windows);
        Assert.NotNull((await service.CheckAsync(Request)).Release);
        Assert.Equal(1, _adapter.Calls);
    }

    [Fact]
    public async Task DisabledPreferencesCanBeReenabledWithoutRestart()
    {
        await SeedAsync();
        var settings = new EmulatorUpdateSettingsStore(_root);
        var service = Service();
        await settings.SaveAsync(new() { CheckBeforeLaunch = false });
        Assert.Null((await service.CheckAsync(Request)).Release);
        await settings.SaveAsync(new() { CheckBeforeLaunch = true });
        Assert.NotNull((await service.CheckAsync(Request)).Release);
        Assert.Equal(1, _adapter.Calls);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Catalog : IEmulatorCatalogSource
    {
        public Task<EmulatorCatalog> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new EmulatorCatalog
        {
            SchemaVersion = EmulatorCatalog.CurrentSchemaVersion,
            Emulators = [new() { Id = "ppsspp", DisplayName = "PPSSPP", ReleaseChannels =
                [new() { Id = "stable", DisplayName = "Stable", IsStable = true, SupportedPlatforms = [PlatformKind.Windows] }] }]
        });
    }
    private sealed class Adapter : IEmulatorReleaseAdapter
    {
        public string EmulatorId => "ppsspp";
        public int Calls;
        public EmulatorRelease? Result = Release();
        public Exception? Error;
        public bool WaitForCancellation;
        public async Task<EmulatorRelease?> FindLatestAsync(EmulatorReleaseChannel channel, PlatformKind platform,
            Architecture architecture, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Error is not null) throw Error;
            return Result;
        }
    }
    private sealed class Installer : IManagedEmulatorInstaller
    {
        public int Recoveries;
        public Exception? RecoveryError;
        public Task RecoverAsync(PlatformKind platform, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recoveries++;
            if (RecoveryError is not null) throw RecoveryError;
            return Task.CompletedTask;
        }
        public Task<EmulatorInstallStatus> InspectAsync(EmulatorRelease release, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmulatorInstallStatus(EmulatorInstallAction.Update, "Update"));
        public Task<EmulatorInstallation> InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
