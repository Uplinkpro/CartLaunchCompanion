using System.Net;
using System.Text.Json;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed partial class PpssppInstallerTests
{
    [Fact]
    public async Task SavesChangedDuringPreparationBlockPublication()
    {
        await SeedAsync();
        var bytes = Zip(contents: "updated");
        using var installer = Installer(bytes, checkpoint: step =>
        {
            if (step == "preserved") File.WriteAllText(Path.Combine(Destination, "new-save.bin"), "new save");
        });
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(NewRelease(bytes)));
        await AssertVersionAsync("v1.20.4", "fixture");
        Assert.Equal("new save", await File.ReadAllTextAsync(Path.Combine(Destination, "new-save.bin")));
        Assert.False(File.Exists(Journal));
    }
    [Fact]
    public async Task SavesCreatedAfterInterruptionAreNeverDiscardedByRecovery()
    {
        await SeedAsync();
        var bytes = Zip(contents: "updated");
        using (var installer = Installer(bytes, checkpoint: step => { if (step == "published") throw new SimulatedInterruption(); }))
            await Assert.ThrowsAsync<SimulatedInterruption>(() => installer.InstallAsync(NewRelease(bytes)));
        await File.WriteAllTextAsync(Path.Combine(Destination, "memstick", "new-save.bin"), "new progress");
        using var recovery = Installer(bytes);
        await Assert.ThrowsAsync<IOException>(() => recovery.RecoverAsync(PlatformKind.Windows));
        Assert.Equal("new progress", await File.ReadAllTextAsync(Path.Combine(Destination, "memstick", "new-save.bin")));
        Assert.True(File.Exists(Journal));
        Assert.True(Directory.Exists(Path.Combine(Directory.GetDirectories(Path.GetDirectoryName(Destination)!, ".ppsspp-*").Single(), "backup")));
    }
    private sealed class SimulatedInterruption : Exception;
    private static EmulatorRelease NewRelease(byte[] bytes, PlatformKind platform = PlatformKind.Windows) =>
        Release(bytes, platform) with
        {
            Version = "v1.21.0",
            AssetName = Release(bytes, platform).AssetName.Replace("v1.20.4", "v1.21.0"),
            DownloadUrl = new(Release(bytes, platform).DownloadUrl.AbsoluteUri.Replace("v1.20.4", "v1.21.0"))
        };
    private async Task<EmulatorInstallation> SeedAsync()
    {
        var bytes = Zip();
        using var installer = Installer(bytes);
        var installed = await installer.InstallAsync(Release(bytes));
        Directory.CreateDirectory(Path.Combine(Destination, "memstick", "PSP", "SYSTEM"));
        await File.WriteAllTextAsync(Path.Combine(Destination, "memstick", "PSP", "SYSTEM", "ppsspp.ini"), "my settings");
        await File.WriteAllTextAsync(Path.Combine(Destination, "custom.txt"), "keep custom");
        return installed;
    }
    private string Journal => Path.Combine(_root, "Emulators", "Windows", ".ppsspp-transaction.json");
    private async Task AssertVersionAsync(string version, string executable)
    {
        Assert.Equal(version, Assert.Single((await new EmulatorRegistryStore(_root).LoadAsync()).Installations).InstalledVersion);
        Assert.Equal(executable, await File.ReadAllTextAsync(Path.Combine(Destination, "PPSSPPWindows64.exe")));
        Assert.Equal("my settings", await File.ReadAllTextAsync(Path.Combine(Destination, "memstick", "PSP", "SYSTEM", "ppsspp.ini")));
        Assert.Equal("keep custom", await File.ReadAllTextAsync(Path.Combine(Destination, "custom.txt")));
    }

    [Fact]
    public async Task UpdateReplacesExecutableAndPreservesSettingsAndCustomFiles()
    {
        await SeedAsync();
        var bytes = Zip(contents: "updated executable");
        using var installer = Installer(bytes);
        Assert.Equal(EmulatorInstallAction.Update, (await installer.InspectAsync(NewRelease(bytes))).Action);
        await installer.InstallAsync(NewRelease(bytes));
        await AssertVersionAsync("v1.21.0", "updated executable");
        Assert.False(File.Exists(Journal));
    }

    [Theory]
    [InlineData("prepared", false)]
    [InlineData("backed-up", false)]
    [InlineData("published", false)]
    [InlineData("committed", true)]
    public async Task InterruptedUpdateRecoversConsistentFilesAndRegistry(string boundary, bool committed)
    {
        await SeedAsync();
        var bytes = Zip(contents: "updated");
        using (var installer = Installer(bytes, checkpoint: step => { if (step == boundary) throw new SimulatedInterruption(); }))
            await Assert.ThrowsAsync<SimulatedInterruption>(() => installer.InstallAsync(NewRelease(bytes)));
        Assert.True(File.Exists(Journal));
        using var recovery = Installer(bytes);
        await recovery.RecoverAsync(PlatformKind.Windows);
        await recovery.RecoverAsync(PlatformKind.Windows);
        await AssertVersionAsync(committed ? "v1.21.0" : "v1.20.4", committed ? "updated" : "fixture");
        Assert.False(File.Exists(Journal));
    }

    [Theory]
    [InlineData("prepared", false)]
    [InlineData("published", false)]
    [InlineData("committed", true)]
    public async Task InterruptedFirstInstallAlsoRecovers(string boundary, bool committed)
    {
        var bytes = Zip();
        using (var installer = Installer(bytes, checkpoint: step => { if (step == boundary) throw new SimulatedInterruption(); }))
            await Assert.ThrowsAsync<SimulatedInterruption>(() => installer.InstallAsync(Release(bytes)));
        using var recovery = Installer(bytes);
        await recovery.RecoverAsync(PlatformKind.Windows);
        Assert.Equal(committed, Directory.Exists(Destination));
        Assert.Equal(committed ? 1 : 0, (await new EmulatorRegistryStore(_root).LoadAsync()).Installations.Count);
        Assert.False(File.Exists(Journal));
    }

    [Theory]
    [InlineData("recovery-displaced")]
    [InlineData("recovery-restored")]
    public async Task RecoveryItselfCanBeInterruptedAndRepeated(string boundary)
    {
        await SeedAsync();
        var bytes = Zip(contents: "updated");
        using (var installer = Installer(bytes, checkpoint: step => { if (step == "published") throw new SimulatedInterruption(); }))
            await Assert.ThrowsAsync<SimulatedInterruption>(() => installer.InstallAsync(NewRelease(bytes)));
        using (var recovery = Installer(bytes, checkpoint: step => { if (step == boundary) throw new SimulatedInterruption(); }))
            await Assert.ThrowsAsync<SimulatedInterruption>(() => recovery.RecoverAsync(PlatformKind.Windows));
        using var retry = Installer(bytes);
        await retry.RecoverAsync(PlatformKind.Windows);
        await AssertVersionAsync("v1.20.4", "fixture");
    }

    private sealed class FailingUpdateRegistry(IEmulatorRegistryStore store) : IEmulatorRegistryStore
    {
        public Task<EmulatorRegistry> LoadAsync(CancellationToken cancellationToken = default) => store.LoadAsync(cancellationToken);
        public Task UpsertAsync(EmulatorInstallation installation, CancellationToken cancellationToken = default) => throw new IOException("Registry failure");
        public Task RemoveAsync(string id, PlatformKind platform, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task UpdateRegistryFailureRestoresOldInstallation()
    {
        await SeedAsync();
        var bytes = Zip(contents: "updated");
        using var installer = Installer(bytes, new FailingUpdateRegistry(new EmulatorRegistryStore(_root)));
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(NewRelease(bytes)));
        await AssertVersionAsync("v1.20.4", "fixture");
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public async Task CancelBeforeCommitLeavesOriginalAndNoJournal()
    {
        await SeedAsync();
        using var cancelled = new CancellationTokenSource();
        var bytes = Zip(contents: "updated");
        var handler = new Handler(() => { cancelled.Cancel(); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; });
        using var installer = new PpssppInstaller(_root, new EmulatorRegistryStore(_root), new HttpClient(handler));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(NewRelease(bytes), cancelled.Token));
        await AssertVersionAsync("v1.20.4", "fixture");
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public async Task CancellationAfterPublicationFinishesConsistentCommit()
    {
        await SeedAsync();
        using var cancelled = new CancellationTokenSource();
        var bytes = Zip(contents: "updated");
        using var installer = Installer(bytes, checkpoint: step => { if (step == "published") cancelled.Cancel(); });
        await installer.InstallAsync(NewRelease(bytes), cancelled.Token);
        await AssertVersionAsync("v1.21.0", "updated");
    }

    [Fact]
    public async Task SameVersionDoesNotDownloadAndDowngradeIsBlocked()
    {
        await SeedAsync();
        var handler = new Handler(() => throw new Exception("No request expected."));
        using var installer = new PpssppInstaller(_root, new EmulatorRegistryStore(_root), new HttpClient(handler));
        Assert.Equal(EmulatorInstallAction.Current, (await installer.InspectAsync(Release(Zip()))).Action);
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(Release(Zip())));
        var older = Release(Zip()) with { Version = "v1.19.0", AssetName = "PPSSPP-v1.19.0-Windows-x64.zip",
            DownloadUrl = new("https://github.com/hrydgard/ppsspp/releases/download/v1.19.0/PPSSPP-v1.19.0-Windows-x64.zip") };
        Assert.Equal(EmulatorInstallAction.NewerInstalled, (await installer.InspectAsync(older)).Action);
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(older));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("memstick/PSP/SYSTEM/ppsspp.ini")]
    [InlineData("MEMSTICK/PSP/savedata.bin")]
    [InlineData("PPSSPP.AppImage.home/settings")]
    [InlineData("PPSSPP.AppImage.config/settings")]
    public async Task PackageCannotOverwriteUserData(string name)
    {
        await SeedAsync();
        var bytes = Zip(name);
        using var installer = Installer(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(NewRelease(bytes)));
        await AssertVersionAsync("v1.20.4", "fixture");
    }

    [Fact]
    public async Task ChangedRegistryStopsRecoveryAndRetainsBackup()
    {
        var old = await SeedAsync();
        var bytes = Zip(contents: "updated");
        using (var installer = Installer(bytes, checkpoint: step => { if (step == "published") throw new SimulatedInterruption(); }))
            await Assert.ThrowsAsync<SimulatedInterruption>(() => installer.InstallAsync(NewRelease(bytes)));
        await new EmulatorRegistryStore(_root).UpsertAsync(old with { InstalledVersion = "v9.0" });
        using var recovery = Installer(bytes);
        await Assert.ThrowsAsync<IOException>(() => recovery.RecoverAsync(PlatformKind.Windows));
        Assert.True(File.Exists(Journal));
        Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(Destination)!, ".ppsspp-*"));
    }

    [Fact]
    public async Task CorruptJournalDoesNotMoveExistingFiles()
    {
        await SeedAsync();
        await File.WriteAllTextAsync(Journal, "{}");
        using var recovery = Installer(Zip());
        await Assert.ThrowsAsync<InvalidDataException>(() => recovery.RecoverAsync(PlatformKind.Windows));
        await AssertVersionAsync("v1.20.4", "fixture");
    }

    [Theory]
    [InlineData("v1.20", "v1.20.0", 0)]
    [InlineData("v1.10", "v1.9", 1)]
    [InlineData("v1.20.3", "v1.20.4", -1)]
    public void VersionComparisonIsNumeric(string candidate, string installed, int sign) =>
        Assert.Equal(sign, Math.Sign(PpssppInstaller.CompareVersions(candidate, installed)));

    [Fact]
    public async Task LinuxUpdatePreservesBothPortableDataDirectories()
    {
        byte[] old = [1, 2, 3], updated = [4, 5, 6];
        using (var installer = Installer(old)) await installer.InstallAsync(Release(old, PlatformKind.Linux));
        var exe = Path.Combine(_root, "Emulators", "Linux", "PPSSPP", "PPSSPP.AppImage");
        await File.WriteAllTextAsync(Path.Combine(exe + ".home", "save.bin"), "save");
        await File.WriteAllTextAsync(Path.Combine(exe + ".config", "ppsspp.ini"), "config");
        using (var installer = Installer(updated)) await installer.InstallAsync(NewRelease(updated, PlatformKind.Linux));
        Assert.Equal(updated, await File.ReadAllBytesAsync(exe));
        Assert.Equal("save", await File.ReadAllTextAsync(Path.Combine(exe + ".home", "save.bin")));
        Assert.Equal("config", await File.ReadAllTextAsync(Path.Combine(exe + ".config", "ppsspp.ini")));
    }
}