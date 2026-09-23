using System.Net;
using System.Runtime.InteropServices;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using Xunit;

namespace CartLaunchCompanion.EmulatorCompanion.Tests;

public sealed class ReleaseDetailsViewModelTests
{
    [Fact]
    public async Task BothChecksAndInstallsEachPlatformExplicitly()
    {
        var checkedPlatforms = new List<PlatformKind>();
        var installedPlatforms = new List<PlatformKind>();
        using var model = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, platform, _, _) =>
        {
            checkedPlatforms.Add(platform);
            return Task.FromResult<EmulatorRelease?>(Release(platform));
        }), new InstallerStub((release, _) =>
        {
            installedPlatforms.Add(release.Platform);
            return Task.FromResult(new EmulatorInstallation { EmulatorId = "ppsspp", Platform = release.Platform,
                ExecutableRelativePath = $"Emulators/{release.Platform}/PPSSPP/app" });
        }));
        model.SelectedPlatform = model.Platforms.Single(p => p.DisplayName == "Both");
        Assert.Empty(checkedPlatforms);
        await model.CheckAsync();
        Assert.Equal(new[] { PlatformKind.Windows, PlatformKind.Linux }, checkedPlatforms);
        Assert.Equal(2, model.Results.Count);
        Assert.Empty(installedPlatforms);
        await model.InstallAsync();
        Assert.Equal(checkedPlatforms, installedPlatforms);
        Assert.True(model.Installed);
        Assert.False(model.CanInstall);
    }

    [Fact]
    public async Task BothRetriesOnlyTheFailedInstallation()
    {
        var calls = new List<PlatformKind>();
        var failLinux = true;
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, platform, _, _) => Task.FromResult<EmulatorRelease?>(Release(platform))),
            new InstallerStub((release, _) =>
            {
                calls.Add(release.Platform);
                if (release.Platform == PlatformKind.Linux && failLinux) throw new IOException("Disk full");
                return Task.FromResult(new EmulatorInstallation { EmulatorId = "ppsspp", Platform = release.Platform,
                    ExecutableRelativePath = $"Emulators/{release.Platform}/PPSSPP/app" });
            }));
        model.SelectedPlatform = model.Platforms.Single(p => p.DisplayName == "Both");
        await model.CheckAsync();
        await model.InstallAsync();
        Assert.True(model.Results[0].Completed);
        Assert.False(model.Results[1].Completed);
        Assert.Contains("Disk full", model.Results[1].Message);
        Assert.False(model.Installed);
        Assert.True(model.CanInstall);
        failLinux = false;
        await model.InstallAsync();
        Assert.Equal(new[] { PlatformKind.Windows, PlatformKind.Linux, PlatformKind.Linux }, calls);
        Assert.True(model.Installed);
    }

    [Fact]
    public async Task BothKeepsSuccessfulDiscoveryWhenOtherPlatformFails()
    {
        using var model = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, platform, _, _) =>
            platform == PlatformKind.Windows ? Task.FromResult<EmulatorRelease?>(Release(platform)) :
            throw new HttpRequestException("Unavailable")));
        model.SelectedPlatform = model.Platforms.Single(p => p.DisplayName == "Both");
        await model.CheckAsync();
        Assert.True(model.Results[0].HasRelease);
        Assert.False(model.Results[1].HasRelease);
        Assert.Contains("could not be reached", model.Results[1].Message);
        Assert.True(model.HasRelease);
    }

    [Fact]
    public async Task ChangingBothSelectionDiscardsAllPendingResults()
    {
        var linux = new TaskCompletionSource<EmulatorRelease?>();
        using var model = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, platform, _, _) =>
            platform == PlatformKind.Windows ? Task.FromResult<EmulatorRelease?>(Release(platform)) : linux.Task));
        model.SelectedPlatform = model.Platforms.Single(p => p.DisplayName == "Both");
        var check = model.CheckAsync();
        Assert.Single(model.Results);
        model.SelectedPlatform = model.Platforms[0];
        linux.SetResult(Release(PlatformKind.Linux));
        await check;
        Assert.Empty(model.Results);
    }

    [Fact]
    public void BothRequiresChannelSupportForBothPlatforms()
    {
        using var model = new ReleaseDetailsViewModel(Definition with
        {
            ReleaseChannels = [Definition.ReleaseChannels[0] with { SupportedPlatforms = [PlatformKind.Windows] }]
        }, new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(null)));
        model.SelectedChannel = model.Channels[0];
        model.SelectedPlatform = model.Platforms.Single(p => p.DisplayName == "Both");
        Assert.True(model.IsBothSelected);
        Assert.False(model.CanCheck);
        Assert.Contains("Windows x64 only", model.Message);
    }

    [Fact]
    public void VisiblePlatformChoicesSelectWindowsLinuxAndBoth()
    {
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(null)));

        model.IsLinuxSelected = true;
        Assert.True(model.IsLinuxSelected);
        Assert.True(model.CanCheck);

        model.IsBothSelected = true;
        Assert.True(model.IsBothSelected);
        Assert.True(model.CanCheck);

        model.IsWindowsSelected = true;
        Assert.True(model.IsWindowsSelected);
        Assert.True(model.CanCheck);
    }

    private sealed class MixedInstaller : IManagedEmulatorInstaller
    {
        public List<PlatformKind> Installed = [];
        public Task RecoverAsync(PlatformKind platform, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EmulatorInstallStatus> InspectAsync(EmulatorRelease release, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmulatorInstallStatus(release.Platform == PlatformKind.Windows ? EmulatorInstallAction.Current : EmulatorInstallAction.Update, "Status"));
        public Task<EmulatorInstallation> InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
        {
            Installed.Add(release.Platform);
            return Task.FromResult(new EmulatorInstallation { EmulatorId = "ppsspp", Platform = release.Platform,
                ExecutableRelativePath = $"Emulators/{release.Platform}/PPSSPP/app" });
        }
    }
    [Fact]
    public async Task BothSkipsBuildsAlreadyCurrent()
    {
        var installer = new MixedInstaller();
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, platform, _, _) => Task.FromResult<EmulatorRelease?>(Release(platform))), installer);
        model.SelectedPlatform = model.Platforms.Single(p => p.DisplayName == "Both");
        await model.CheckAsync();
        await model.InstallAsync();
        Assert.Equal(new[] { PlatformKind.Linux }, installer.Installed);
        Assert.False(model.CanInstall);
    }
    [Fact]
    public async Task CancellingBothAfterFirstCommitKeepsItAndDoesNotStartSecond()
    {
        ReleaseDetailsViewModel? model = null;
        var calls = new List<PlatformKind>();
        using var owned = model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, platform, _, _) => Task.FromResult<EmulatorRelease?>(Release(platform))),
            new InstallerStub((release, _) =>
            {
                calls.Add(release.Platform);
                model!.CancelInstallation();
                return Task.FromResult(new EmulatorInstallation { EmulatorId = "ppsspp", Platform = release.Platform,
                    ExecutableRelativePath = $"Emulators/{release.Platform}/PPSSPP/app" });
            }));
        model.SelectedPlatform = model.Platforms.Single(p => p.DisplayName == "Both");
        await model.CheckAsync();
        await model.InstallAsync();
        Assert.Equal(new[] { PlatformKind.Windows }, calls);
        Assert.True(model.Results[0].Completed);
        Assert.False(model.Results[1].Completed);
        Assert.Contains("cancelled", model.Results[1].Message);
        Assert.True(model.CanInstall);
    }
    private sealed class InstallerStub(Func<EmulatorRelease, CancellationToken, Task<EmulatorInstallation>> install) : IEmulatorInstaller
    {
        public int Calls;
        public Task<EmulatorInstallation> InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
        { Calls++; return install(release, cancellationToken); }
    }

    [Fact]
    public async Task InstallIsExplicitAndSelectionIsLockedUntilCompletion()
    {
        var pending = new TaskCompletionSource<EmulatorInstallation>();
        var installer = new InstallerStub((_, _) => pending.Task);
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(Release())), installer);
        await model.CheckAsync();
        Assert.Equal(0, installer.Calls);
        var selected = model.SelectedPlatform;
        var task = model.InstallAsync();
        Assert.False(model.CanCheck);
        Assert.False(model.CanInstall);
        model.SelectedPlatform = model.Platforms.First(p => p != selected);
        Assert.Equal(selected, model.SelectedPlatform);
        await model.InstallAsync();
        Assert.Equal(1, installer.Calls);
        pending.SetResult(new() { EmulatorId = "ppsspp", Platform = PlatformKind.Windows, ExecutableRelativePath = "Emulators/Windows/PPSSPP/PPSSPPWindows64.exe" });
        await task;
        Assert.True(model.Installed);
        Assert.False(model.CanInstall);
    }

    [Fact]
    public async Task FailedInstallCanBeRetriedWithoutReportingInstalled()
    {
        var installer = new InstallerStub((_, _) => throw new IOException("Verification failed"));
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(Release())), installer);
        await model.CheckAsync();
        await model.InstallAsync();
        Assert.False(model.Installed);
        Assert.True(model.CanInstall);
        Assert.Contains("Verification failed", model.Message);
    }

    [Fact]
    public async Task CancelInstallRestoresControls()
    {
        var installer = new InstallerStub(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("Unreachable");
        });
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(Release())), installer);
        await model.CheckAsync();
        var task = model.InstallAsync();
        model.CancelInstallation();
        await task;
        Assert.False(model.IsInstalling);
        Assert.False(model.Installed);
        Assert.True(model.CanInstall);
    }
    private sealed class ManagedInstallerStub(EmulatorInstallAction action) : IManagedEmulatorInstaller
    {
        public int Calls;
        public Task<EmulatorInstallStatus> InspectAsync(EmulatorRelease release, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmulatorInstallStatus(action, "Installed-version status"));
        public Task RecoverAsync(PlatformKind platform, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EmulatorInstallation> InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new EmulatorInstallation { EmulatorId = "ppsspp", Platform = release.Platform,
                ExecutableRelativePath = "Emulators/Windows/PPSSPP/PPSSPPWindows64.exe" });
        }
    }

    [Theory]
    [InlineData(EmulatorInstallAction.Install, true, "2. Download and install")]
    [InlineData(EmulatorInstallAction.Update, true, "2. Download and update")]
    [InlineData(EmulatorInstallAction.Current, false, "Already up to date")]
    [InlineData(EmulatorInstallAction.NewerInstalled, false, "Installation unavailable")]
    [InlineData(EmulatorInstallAction.Blocked, false, "Installation unavailable")]
    public async Task InstalledVersionControlsAvailableAction(EmulatorInstallAction action, bool enabled, string label)
    {
        var installer = new ManagedInstallerStub(action);
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(Release())), installer);
        await model.CheckAsync();
        Assert.Equal(enabled, model.CanInstall);
        Assert.Equal(label, model.InstallActionLabel);
        Assert.Equal("Installed-version status", model.Message);
        Assert.Equal(0, installer.Calls);
        await model.InstallAsync();
        Assert.Equal(enabled ? 1 : 0, installer.Calls);
        if (action == EmulatorInstallAction.Update) Assert.Contains("updated", model.Message);
    }

    [Fact]
    public async Task ChangingPlatformClearsUpdateStatus()
    {
        using var model = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(Release())),
            new ManagedInstallerStub(EmulatorInstallAction.Update));
        await model.CheckAsync();
        model.SelectedPlatform = model.Platforms.First(p => p != model.SelectedPlatform);
        Assert.Null(model.InstallStatus);
        Assert.False(model.CanInstall);
    }
    private static EmulatorDefinition Definition => new()
    {
        Id = "ppsspp", DisplayName = "PPSSPP", DefaultChannelId = "stable",
        ReleaseChannels = [new()
        {
            Id = "stable", DisplayName = "Stable", IsStable = true,
            SupportedPlatforms = [PlatformKind.Windows, PlatformKind.Linux]
        }]
    };

    private static EmulatorRelease Release(PlatformKind platform = PlatformKind.Windows) => new()
    {
        EmulatorId = "ppsspp", ChannelId = "stable", Platform = platform, Architecture = Architecture.X64,
        Version = "v1.20.4", RevisionId = "revision", PublishedAt = DateTimeOffset.Parse("2026-05-16T12:35:08Z"),
        IsPrerelease = false, ReleasePage = new("https://github.com/hrydgard/ppsspp/releases/tag/v1.20.4"),
        AssetName = "PPSSPP.zip", DownloadUrl = new("https://github.com/hrydgard/ppsspp/releases/download/v1.20.4/PPSSPP.zip"),
        SizeBytes = 20 * 1048576, PackageFormat = EmulatorPackageFormat.Zip, Sha256 = new string('a', 64)
    };

    [Fact]
    public async Task OnlyExplicitCheckMakesARequestAndUsesChosenPlatform()
    {
        var adapter = new StubAdapter((_, platform, _, _) => Task.FromResult<EmulatorRelease?>(Release(platform)));
        using var model = new ReleaseDetailsViewModel(Definition, adapter);
        Assert.Equal(0, adapter.Calls);
        model.SelectedPlatform = model.Platforms.Single(p => p.Platform == PlatformKind.Linux);
        Assert.Equal(0, adapter.Calls);
        await model.CheckAsync();
        Assert.Equal(1, adapter.Calls);
        Assert.Equal(PlatformKind.Linux, model.Release!.Platform);
        Assert.Contains("v1.20.4", model.ReleaseSummary);
        Assert.Contains("20.0 MiB", model.PackageDetails);
        Assert.Contains("has not been downloaded", model.ChecksumStatus);
    }

    [Fact]
    public void InstallStepIsExplainedBeforeReleaseCheck()
    {
        using var installable = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(null)),
            new InstallerStub((_, _) => throw new InvalidOperationException()));
        Assert.Equal("2. Check for a release first", installable.InstallActionLabel);
        using var discoveryOnly = new ReleaseDetailsViewModel(Definition,
            new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(null)));
        Assert.Equal("Installation support is not available yet", discoveryOnly.InstallActionLabel);
    }

    [Fact]
    public async Task ConcurrentCheckIsSuppressed()
    {
        var pending = new TaskCompletionSource<EmulatorRelease?>();
        var adapter = new StubAdapter((_, _, _, _) => pending.Task);
        using var model = new ReleaseDetailsViewModel(Definition, adapter);
        var first = model.CheckAsync();
        Assert.True(model.IsChecking);
        Assert.False(model.CanCheck);
        await model.CheckAsync();
        Assert.Equal(1, adapter.Calls);
        pending.SetResult(Release());
        await first;
        Assert.True(model.HasRelease);
        Assert.True(model.CanCheck);
    }

    [Fact]
    public async Task SelectionChangeCancelsAndDiscardsLateResults()
    {
        var pending = new TaskCompletionSource<EmulatorRelease?>();
        CancellationToken requestToken = default;
        using var model = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, _, _, token) =>
        {
            requestToken = token;
            return pending.Task;
        }));
        model.SelectedPlatform = model.Platforms[0];
        var check = model.CheckAsync();
        model.SelectedPlatform = model.Platforms[1];
        Assert.True(requestToken.IsCancellationRequested);
        pending.SetResult(Release());
        await check;
        Assert.False(model.HasRelease);
        Assert.Contains("Selection changed", model.Message);
    }

    [Fact]
    public async Task ChangingSelectionClearsAlreadyDisplayedRelease()
    {
        using var model = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, _, _, _) =>
            Task.FromResult<EmulatorRelease?>(Release())));
        await model.CheckAsync();
        Assert.True(model.HasRelease);
        model.SelectedChannel = null;
        Assert.False(model.HasRelease);
        Assert.False(model.CanCheck);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "limiting requests")]
    [InlineData(HttpStatusCode.NotFound, "could not be reached")]
    public async Task HttpErrorsAreVisibleAndRetryable(HttpStatusCode status, string expected)
    {
        using var model = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, _, _, _) =>
            throw new HttpRequestException("upstream failed", null, status)));
        await model.CheckAsync();
        Assert.Contains(expected, model.Message);
        Assert.False(model.HasRelease);
        Assert.True(model.CanCheck);
    }

    [Fact]
    public async Task TimeoutAndInvalidMetadataDoNotLookLikeNoMatchingRelease()
    {
        using var timed = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, _, _, _) =>
            throw new TaskCanceledException()));
        await timed.CheckAsync();
        Assert.Contains("timed out", timed.Message);
        using var invalid = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, _, _, _) =>
            throw new InvalidDataException()));
        await invalid.CheckAsync();
        Assert.Contains("could not be validated", invalid.Message);
    }

    [Fact]
    public async Task NoPackageIsDistinctFromFailure()
    {
        using var model = new ReleaseDetailsViewModel(Definition, new StubAdapter((_, _, _, _) =>
            Task.FromResult<EmulatorRelease?>(null)));
        await model.CheckAsync();
        Assert.Contains("No matching package", model.Message);
        Assert.False(model.HasRelease);
    }

    [Fact]
    public async Task ClosingCancelsCheckAndDisposesAdapter()
    {
        CancellationToken requestToken = default;
        var adapter = new StubAdapter(async (_, _, _, token) =>
        {
            requestToken = token;
            await Task.Delay(Timeout.Infinite, token);
            return null;
        });
        var model = new ReleaseDetailsViewModel(Definition, adapter);
        var check = model.CheckAsync();
        model.Dispose();
        await check;
        Assert.True(requestToken.IsCancellationRequested);
        Assert.True(adapter.Disposed);
        Assert.False(model.HasRelease);
        Assert.False(model.CanCheck);
    }

    [Fact]
    public void NoSilentPreviewOrAmbiguousStableSelection()
    {
        var definition = Definition with
        {
            DefaultChannelId = "missing",
            ReleaseChannels = [
                Definition.ReleaseChannels[0],
                Definition.ReleaseChannels[0] with { Id = "alternate" }]
        };
        using var model = new ReleaseDetailsViewModel(definition, new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(null)));
        Assert.Null(model.SelectedChannel);
        using var preview = new ReleaseDetailsViewModel(Definition with
        {
            ReleaseChannels = [Definition.ReleaseChannels[0] with { IsStable = false }]
        }, new StubAdapter((_, _, _, _) => Task.FromResult<EmulatorRelease?>(null)));
        Assert.Null(preview.SelectedChannel);
    }

    private sealed class StubAdapter(
        Func<EmulatorReleaseChannel, PlatformKind, Architecture, CancellationToken, Task<EmulatorRelease?>> check)
        : IEmulatorReleaseAdapter, IDisposable
    {
        public string EmulatorId => "ppsspp";
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public Task<EmulatorRelease?> FindLatestAsync(EmulatorReleaseChannel channel, PlatformKind platform,
            Architecture architecture, CancellationToken cancellationToken = default)
        {
            Calls++;
            return check(channel, platform, architecture, cancellationToken);
        }
        public void Dispose() => Disposed = true;
    }
}
