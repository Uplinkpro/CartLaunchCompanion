using System.Runtime.InteropServices;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Desktop.Tests;

public sealed partial class MetadataNavigationTests
{
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Expected UI state was not reached.");
            await Task.Delay(10);
        }
    }
    private sealed class Updates : IEmulatorLaunchUpdates
    {
        public int Installs, Skips, Ready, Leases;
        public bool Offer = true;
        public Exception? InstallError, SkipError, ReadyError;
        public string? Block;
        public Func<CancellationToken, Task>? Install;
        public Task<EmulatorLaunchCheck> CheckAsync(GameLaunchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmulatorLaunchCheck(Offer ? new EmulatorRelease
            {
                EmulatorId = "ppsspp", ChannelId = "stable", Platform = PlatformKind.Windows,
                Architecture = Architecture.X64, Version = "v1.20.4", RevisionId = "fixture",
                PublishedAt = DateTimeOffset.UtcNow, IsPrerelease = false,
                ReleasePage = new("https://github.com/hrydgard/ppsspp/releases/tag/v1.20.4"),
                AssetName = "package.zip", DownloadUrl = new("https://github.com/package.zip"),
                SizeBytes = 100, PackageFormat = EmulatorPackageFormat.Zip, Sha256 = new string('a', 64)
            } : null, Block));
        public async Task InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
        {
            Installs++;
            if (InstallError is not null) throw InstallError;
            if (Install is not null) await Install(cancellationToken);
        }
        public Task SkipVersionAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
        {
            Skips++;
            if (SkipError is not null) throw SkipError;
            return Task.CompletedTask;
        }
        public Task EnsureReadyAsync(GameLaunchRequest request, CancellationToken cancellationToken = default)
        {
            Ready++;
            if (ReadyError is not null) throw ReadyError;
            return Task.CompletedTask;
        }
        public Task<IDisposable?> AcquireLaunchLeaseAsync(GameLaunchRequest request, CancellationToken cancellationToken = default)
        { Leases++; return Task.FromResult<IDisposable?>(null); }
    }

    [Theory]
    [InlineData(LauncherAction.Confirm, 1, 0)]
    [InlineData(LauncherAction.Back, 0, 0)]
    [InlineData(LauncherAction.Trailer, 0, 1)]
    public async Task ControllerUpdateChoicesResumeExactlyOneLaunch(LauncherAction action, int installs, int skips)
    {
        var updates = new Updates();
        var launcher = new StubLaunchService();
        using var model = CreateViewModel(launcher, emulatorUpdates: updates);
        await model.LoadAsync();
        var launch = model.ConfirmLaunchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => model.IsEmulatorUpdateVisible);
        Assert.Equal(0, launcher.CallCount);
        await model.HandleInputAsync(new(action, InputDeviceKind.Controller, DateTimeOffset.UtcNow));
        await launch;
        Assert.Equal(installs, updates.Installs);
        Assert.Equal(skips, updates.Skips);
        Assert.Equal(1, launcher.CallCount);
        Assert.Equal(1, updates.Leases);
        Assert.False(model.IsEmulatorUpdateVisible);
        Assert.False(model.IsLaunching);
    }

    [Fact]
    public async Task FailedUpdateStaysAtPromptUntilUserSkips()
    {
        var updates = new Updates { InstallError = new IOException("Download verification failed") };
        var launcher = new StubLaunchService();
        using var model = CreateViewModel(launcher, emulatorUpdates: updates);
        await model.LoadAsync();
        var launch = model.ConfirmLaunchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => model.IsEmulatorUpdateVisible);
        model.UpdateEmulatorNowCommand.Execute(null);
        await WaitUntilAsync(() => model.EmulatorUpdateMessage.Contains("could not complete"));
        Assert.Equal(0, launcher.CallCount);
        model.SkipEmulatorNowCommand.Execute(null);
        await launch;
        Assert.Equal(1, launcher.CallCount);
    }

    [Fact]
    public async Task FailedSkipPersistenceDoesNotPretendChoiceWasSaved()
    {
        var updates = new Updates { SkipError = new IOException("Read-only media") };
        var launcher = new StubLaunchService();
        using var model = CreateViewModel(launcher, emulatorUpdates: updates);
        await model.LoadAsync();
        var launch = model.ConfirmLaunchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => model.IsEmulatorUpdateVisible);
        model.SkipEmulatorVersionCommand.Execute(null);
        await WaitUntilAsync(() => model.EmulatorUpdateMessage.Contains("Read-only media"));
        Assert.Equal(0, launcher.CallCount);
        model.SkipEmulatorNowCommand.Execute(null);
        await launch;
        Assert.Equal(1, launcher.CallCount);
    }

    [Fact]
    public async Task CancelDuringDownloadReturnsToPromptWithoutLaunching()
    {
        var updates = new Updates { Install = token => Task.Delay(Timeout.Infinite, token) };
        var launcher = new StubLaunchService();
        using var model = CreateViewModel(launcher, emulatorUpdates: updates);
        await model.LoadAsync();
        var launch = model.ConfirmLaunchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => model.IsEmulatorUpdateVisible);
        model.UpdateEmulatorNowCommand.Execute(null);
        await WaitUntilAsync(() => model.IsEmulatorUpdateBusy);
        model.CancelEmulatorUpdateCommand.Execute(null);
        await WaitUntilAsync(() => model.EmulatorUpdateMessage.Contains("cancelled"));
        Assert.Equal(0, launcher.CallCount);
        model.SkipEmulatorNowCommand.Execute(null);
        await launch;
        Assert.Equal(1, launcher.CallCount);
    }

    [Fact]
    public async Task NoUpdateContinuesWithoutPrompt()
    {
        var launcher = new StubLaunchService();
        using var model = CreateViewModel(launcher, emulatorUpdates: new Updates { Offer = false });
        await model.LoadAsync();
        await model.ConfirmLaunchCommand.ExecuteAsync(null);
        Assert.Equal(1, launcher.CallCount);
        Assert.False(model.IsEmulatorUpdateVisible);
    }

    [Fact]
    public async Task UnresolvedRecoveryBlocksEvenAfterSkip()
    {
        var updates = new Updates { ReadyError = new IOException("Recovery required") };
        var launcher = new StubLaunchService();
        using var model = CreateViewModel(launcher, emulatorUpdates: updates);
        await model.LoadAsync();
        var launch = model.ConfirmLaunchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => model.IsEmulatorUpdateVisible);
        model.SkipEmulatorNowCommand.Execute(null);
        await launch;
        Assert.Equal(0, launcher.CallCount);
        Assert.Contains("Recovery required", model.MetadataStatus);
    }

    [Fact]
    public async Task InitialRecoveryFailureDoesNotOfferUpdateOrLaunch()
    {
        var launcher = new StubLaunchService();
        using var model = CreateViewModel(launcher, emulatorUpdates: new Updates { Block = "Recovery required" });
        await model.LoadAsync();
        await model.ConfirmLaunchCommand.ExecuteAsync(null);
        Assert.Equal(0, launcher.CallCount);
        Assert.False(model.IsEmulatorUpdateVisible);
        Assert.Contains("Recovery required", model.MetadataStatus);
    }
    [Fact]
    public async Task ClosingWhilePromptedCannotStartGame()
    {
        var launcher = new StubLaunchService();
        var model = CreateViewModel(launcher, emulatorUpdates: new Updates());
        await model.LoadAsync();
        var launch = model.ConfirmLaunchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => model.IsEmulatorUpdateVisible);
        model.Dispose();
        await launch;
        Assert.Equal(0, launcher.CallCount);
        Assert.False(model.IsEmulatorUpdateVisible);
    }
}