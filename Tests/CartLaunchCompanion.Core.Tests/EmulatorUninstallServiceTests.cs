using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorUninstallServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-uninstall-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UninstallPreservesAndRestoresPortableUserData()
    {
        var folder = Path.Combine(_root, "Emulators", "Windows", "PPSSPP");
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        Directory.CreateDirectory(Path.Combine(folder, "memstick", "PSP", "SAVEDATA"));
        await File.WriteAllTextAsync(Path.Combine(folder, "PPSSPPWindows64.exe"), "program");
        await File.WriteAllTextAsync(Path.Combine(folder, "memstick", "PSP", "SAVEDATA", "save.bin"), "save");
        var store = new EmulatorRegistryStore(_root);
        await store.UpsertAsync(new()
        {
            EmulatorId = "ppsspp", Platform = PlatformKind.Windows,
            ExecutableRelativePath = "Emulators/Windows/PPSSPP/PPSSPPWindows64.exe",
            InstalledVersion = "v1.0", InstalledChannelId = "stable", InstalledAt = DateTimeOffset.UtcNow
        });
        var service = new EmulatorUninstallService(_root, _root);

        var result = await service.UninstallAsync("ppsspp", PlatformKind.Windows);

        Assert.True(result.PersonalDataPreserved);
        Assert.False(Directory.Exists(folder));
        Assert.Empty((await store.LoadAsync()).Installations);
        var preserved = Path.Combine(_root, "Config", "EmulatorCompanion", "PreservedData", "ppsspp", "Windows",
            "memstick", "PSP", "SAVEDATA", "save.bin");
        Assert.Equal("save", await File.ReadAllTextAsync(preserved));

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "PPSSPPWindows64.exe"), "new program");
        await store.UpsertAsync(new()
        {
            EmulatorId = "ppsspp", Platform = PlatformKind.Windows,
            ExecutableRelativePath = "Emulators/Windows/PPSSPP/PPSSPPWindows64.exe",
            InstalledVersion = "v2.0", InstalledChannelId = "stable", InstalledAt = DateTimeOffset.UtcNow
        });
        await service.RestorePreservedDataAsync("ppsspp", PlatformKind.Windows);
        Assert.Equal("save", await File.ReadAllTextAsync(Path.Combine(folder, "memstick", "PSP", "SAVEDATA", "save.bin")));
        Assert.False(File.Exists(preserved));
    }

    [Fact]
    public async Task RefusesToDeleteARecordedPathOutsideTheExpectedManagedFolder()
    {
        var store = new EmulatorRegistryStore(_root);
        await store.UpsertAsync(new()
        {
            EmulatorId = "ppsspp", Platform = PlatformKind.Windows,
            ExecutableRelativePath = "Emulators/Windows/NotPPSSPP/PPSSPPWindows64.exe"
        });
        await Assert.ThrowsAsync<IOException>(() =>
            new EmulatorUninstallService(_root, _root).UninstallAsync("ppsspp", PlatformKind.Windows));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
