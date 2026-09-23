using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using Xunit;

namespace CartLaunchCompanion.EmulatorCompanion.Tests;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task RefreshTransitionsThroughLoadingAndPreventsOverlappingLoads()
    {
        var pending = new TaskCompletionSource<EmulatorLibrarySnapshot>();
        var calls = 0;
        var model = new LibraryViewModel(new StubService(_ => { calls++; return pending.Task; }), "cart");
        var refresh = model.RefreshAsync();
        Assert.True(model.IsLoading);
        Assert.False(model.CanRefresh);
        Assert.False(model.ShowEmpty);
        await model.RefreshAsync();
        Assert.Equal(1, calls);
        pending.SetResult(new([], false, false));
        await refresh;
        Assert.True(model.ShowEmpty);
        Assert.True(model.CanRefresh);
        Assert.False(model.ShowLoading);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LoadErrorsDoNotShowEmptySuccess(bool catalogError, bool registryError)
    {
        var model = new LibraryViewModel(new StubService(_ => Task.FromResult(
            new EmulatorLibrarySnapshot([], catalogError, registryError))), "cart");
        await model.RefreshAsync();
        Assert.True(model.HasError);
        Assert.False(model.ShowEmpty);
        Assert.True(model.CanRefresh);
    }

    [Fact]
    public async Task RecoveryClearsErrorsAndLargeRefreshReplacesRows()
    {
        var snapshot = new EmulatorLibrarySnapshot([], true, true);
        var model = new LibraryViewModel(new StubService(_ => Task.FromResult(snapshot)), "cart");
        await model.RefreshAsync();
        snapshot = new(Enumerable.Range(0, 2000).Select(i =>
            new EmulatorLibraryEntry("sample-" + i, new() { Id = "sample-" + i, DisplayName = "Sample " + i }, [])).ToArray(), false, false);
        await model.RefreshAsync();
        Assert.False(model.HasError);
        Assert.Equal(2000, model.Rows.Count);
        snapshot = new([], false, false);
        await model.RefreshAsync();
        Assert.Empty(model.Rows);
        Assert.True(model.ShowEmpty);
    }

    [Fact]
    public void RowsDistinguishUnknownFromNotInstalledAndUseChannelDisplayName()
    {
        var definition = new EmulatorDefinition
        {
            Id = "ppsspp", DisplayName = "Sample", DefaultChannelId = "dev",
            ReleaseChannels = [new()
            {
                Id = "dev", DisplayName = "Development",
                SupportedPlatforms = [PlatformKind.Windows, PlatformKind.Linux]
            }]
        };
        var entry = new EmulatorLibraryEntry("ppsspp", definition, []);
        var unknown = LibraryRow.From(entry, true);
        Assert.Equal("Installation unknown", unknown.Status);
        Assert.Equal("Unknown", unknown.Version);
        var available = LibraryRow.From(entry, false);
        Assert.Equal("Available to install", available.Status);
        Assert.Equal("Windows / Linux", available.Platform);
        Assert.Equal("Not installed", available.Version);
        Assert.Equal("Development", available.Channel);
        entry = entry with { Installations = [new()
        {
            EmulatorId = "ppsspp", Platform = PlatformKind.Linux,
            ExecutableRelativePath = "Emulators/Linux/Sample/app",
            InstalledChannelId = "dev"
        }]};
        Assert.Equal("Development", LibraryRow.From(entry, false).Channel);
        Assert.Equal("Installed on Linux", LibraryRow.From(entry, false).Status);
        Assert.Equal("Recorded · not in catalog", LibraryRow.From(entry with { Definition = null }, false).Status);
    }

    [Fact]
    public async Task ClosingCancelsPendingLoadAndRestoresBusyState()
    {
        using var cancellation = new CancellationTokenSource();
        var model = new LibraryViewModel(new StubService(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new([], false, false);
        }), "cart");
        var refresh = model.RefreshAsync(cancellation.Token);
        cancellation.Cancel();
        await refresh;
        Assert.False(model.IsLoading);
    }

    [Fact]
    public void DuckStationRowsOfferInstallationSupport()
    {
        var row = LibraryRow.From(new EmulatorLibraryEntry("duckstation", new()
        {
            Id = "duckstation", DisplayName = "DuckStation", DefaultChannelId = "stable",
            ReleaseChannels = [new()
            {
                Id = "stable", DisplayName = "Stable", IsStable = true,
                SupportedPlatforms = [PlatformKind.Windows]
            }]
        }, []), false);
        Assert.Equal("Not installed", row.Version);
        Assert.Equal("Available to install", row.Status);
    }

    [Fact]
    public void WindowsAndLinuxInstallationsShareOneCleanLibraryRow()
    {
        var row = LibraryRow.From(new EmulatorLibraryEntry("duckstation", new()
        {
            Id = "duckstation", DisplayName = "DuckStation", DefaultChannelId = "stable",
            ReleaseChannels = [new()
            {
                Id = "stable", DisplayName = "Stable", IsStable = true,
                SupportedPlatforms = [PlatformKind.Windows, PlatformKind.Linux]
            }]
        }, [
            new()
            {
                EmulatorId = "duckstation", Platform = PlatformKind.Windows,
                ExecutableRelativePath = "Emulators/Windows/DuckStation/duckstation.exe",
                InstalledVersion = "stable-2026.09.12.122020", InstalledChannelId = "stable"
            },
            new()
            {
                EmulatorId = "duckstation", Platform = PlatformKind.Linux,
                ExecutableRelativePath = "Emulators/Linux/DuckStation/DuckStation.AppImage",
                InstalledVersion = "stable-2026.09.12.122036", InstalledChannelId = "stable"
            }
        ]), false);

        Assert.Equal("Windows + Linux", row.Platform);
        Assert.Equal("Platform-specific builds", row.Version);
        Assert.Equal("Stable", row.Channel);
        Assert.Equal("Installed on Windows + Linux", row.Status);
    }

    [Fact]
    public async Task PrimaryActionExplainsWhatSelectedRowCanDo()
    {
        var snapshot = new EmulatorLibrarySnapshot([
            new("ppsspp", new() { Id = "ppsspp", DisplayName = "PPSSPP" }, []),
            new("duckstation", new() { Id = "duckstation", DisplayName = "DuckStation" }, []),
            new("pcsx2", new() { Id = "pcsx2", DisplayName = "PCSX2" }, []),
            new("rpcs3", new() { Id = "rpcs3", DisplayName = "RPCS3" }, []),
            new("shadps4", new() { Id = "shadps4", DisplayName = "shadPS4" }, [])
        ], false, false);
        var model = new LibraryViewModel(new StubService(_ => Task.FromResult(snapshot)), "cart");
        await model.RefreshAsync();
        Assert.Equal("Install PPSSPP", model.SelectedActionLabel);
        model.SelectedRow = model.Rows.Single(row => row.Entry.EmulatorId == "duckstation");
        Assert.Equal("Install DuckStation", model.SelectedActionLabel);
        model.SelectedRow = model.Rows.Single(row => row.Entry.EmulatorId == "pcsx2");
        Assert.Equal("Install PCSX2", model.SelectedActionLabel);
        model.SelectedRow = model.Rows.Single(row => row.Entry.EmulatorId == "rpcs3");
        Assert.Equal("Install RPCS3", model.SelectedActionLabel);
        model.SelectedRow = model.Rows.Single(row => row.Entry.EmulatorId == "shadps4");
        Assert.Equal("Install shadPS4", model.SelectedActionLabel);
    }

    [Fact]
    public async Task RecoveryRunsBeforeLibraryReadAndFailureCanBeRetried()
    {
        var recovered = false;
        var shouldFail = true;
        var loads = 0;
        var model = new LibraryViewModel(new StubService(_ =>
        {
            Assert.True(recovered);
            loads++;
            return Task.FromResult(new EmulatorLibrarySnapshot([], false, false));
        }), "cart", _ =>
        {
            if (shouldFail) throw new IOException("Close PPSSPP");
            recovered = true;
            return Task.CompletedTask;
        });
        await model.RefreshAsync();
        Assert.True(model.HasError);
        Assert.False(model.CanViewReleases);
        Assert.Equal(0, loads);
        shouldFail = false;
        await model.RefreshAsync();
        Assert.False(model.HasError);
        Assert.Equal(1, loads);
    }
    private sealed class StubService(Func<CancellationToken, Task<EmulatorLibrarySnapshot>> load) : IEmulatorLibraryService
    {
        public Task<EmulatorLibrarySnapshot> LoadAsync(CancellationToken cancellationToken = default) => load(cancellationToken);
    }
}
