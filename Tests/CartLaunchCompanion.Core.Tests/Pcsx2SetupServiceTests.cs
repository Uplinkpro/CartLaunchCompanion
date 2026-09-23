using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class Pcsx2SetupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-pcsx2-setup-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ImportedBiosChangesStatusAndRemainsInsidePortableInstall()
    {
        var executable = await InstallRecordAsync();
        var source = Path.Combine(_root, "my-console-bios.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var service = new Pcsx2SetupService(_root, _root);

        var before = Assert.Single(await service.InspectAsync());
        Assert.Equal("BIOS needed", before.State);
        var after = await service.ImportBiosAsync(PlatformKind.Windows, [source]);

        Assert.True(after.BiosFound);
        Assert.Equal("Finish setup in PCSX2", after.State);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(Path.Combine(after.BiosFolder, "my-console-bios.bin")));
        Assert.True(File.Exists(executable));
    }

    [Fact]
    public async Task CompanionFileWithoutMainBiosIsRejectedWithoutCreatingBiosFolder()
    {
        await InstallRecordAsync();
        var source = Path.Combine(_root, "console.nvm");
        await File.WriteAllBytesAsync(source, [1]);
        var service = new Pcsx2SetupService(_root, _root);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportBiosAsync(PlatformKind.Windows, [source]));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "Emulators", "Shared", "BIOS", "PCSX2")));
    }

    [Fact]
    public async Task PortableIniSettingsAndBiosReportReadyToTest()
    {
        await InstallRecordAsync();
        var folder = Path.Combine(_root, "Emulators", "Windows", "PCSX2");
        Directory.CreateDirectory(Path.Combine(folder, "bios"));
        Directory.CreateDirectory(Path.Combine(folder, "inis"));
        await File.WriteAllBytesAsync(Path.Combine(folder, "bios", "console.rom"), [1]);
        await File.WriteAllTextAsync(Path.Combine(folder, "inis", "PCSX2.ini"), "configured");

        var status = Assert.Single(await new Pcsx2SetupService(_root, _root).InspectAsync());

        Assert.Equal("Ready to test", status.State);
        Assert.True(File.Exists(Path.Combine(_root, "Emulators", "Shared", "BIOS", "PCSX2", "console.rom")));
    }

    [Fact]
    public async Task BothTargetCopiesOneBiosSelectionToWindowsAndLinux()
    {
        await InstallRecordAsync();
        await InstallRecordAsync(PlatformKind.Linux);
        var source = Path.Combine(_root, "shared-bios.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var service = new Pcsx2SetupService(_root, _root);

        var results = await service.ImportBiosAsync([PlatformKind.Windows, PlatformKind.Linux], [source]);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.BiosFound));
        Assert.True(File.Exists(Path.Combine(_root, "Emulators", "Shared", "BIOS", "PCSX2", "shared-bios.bin")));
        Assert.All(results, result => Assert.Equal(results[0].BiosFolder, result.BiosFolder));
    }

    private async Task<string> InstallRecordAsync(PlatformKind platform = PlatformKind.Windows)
    {
        var platformName = platform.ToString();
        var executableName = platform == PlatformKind.Windows ? "pcsx2-qt.exe" : "PCSX2.AppImage";
        var executable = Path.Combine(_root, "Emulators", platformName, "PCSX2", executableName);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "fixture");
        await new EmulatorRegistryStore(_root).UpsertAsync(new()
        {
            EmulatorId = "pcsx2", Platform = platform,
            ExecutableRelativePath = $"Emulators/{platformName}/PCSX2/{executableName}",
            InstalledVersion = "v2.8.2", InstalledChannelId = "stable", InstalledAt = DateTimeOffset.UtcNow
        });
        return executable;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
