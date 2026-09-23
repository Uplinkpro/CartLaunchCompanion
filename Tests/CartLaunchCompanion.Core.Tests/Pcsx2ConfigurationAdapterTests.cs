using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class Pcsx2ConfigurationAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-pcsx2-profile-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyChangesOnlyOwnedKeysAndRestoreReproducesOriginalFile()
    {
        var configuration = await InstallAsync(PlatformKind.Windows);
        const string original = "; keep this comment\r\n[UI]\r\nStartFullscreen = false\r\nTheme = custom\r\n\r\n" +
            "[EmuCore/GS]\r\nRenderer = 14\r\nupscale_multiplier = 1\r\n\r\n[Pad1]\r\nCross = SDL-0/A\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
        await File.WriteAllTextAsync(configuration, original);
        var adapter = new Pcsx2ConfigurationAdapter(_root, _root);
        var balanced = adapter.Translate(adapter.Presets.Single(profile => profile.Id == "balanced"));

        var preview = await adapter.PreviewAsync(PlatformKind.Windows, balanced);
        Assert.Contains(preview.Changes, change => change.Key == "upscale_multiplier" && change.NewValue == "2");
        await adapter.ApplyAsync(PlatformKind.Windows, balanced);
        var applied = await File.ReadAllTextAsync(configuration);

        Assert.Contains("upscale_multiplier = 2", applied);
        Assert.Contains("Theme = custom", applied);
        Assert.Contains("Cross = SDL-0/A", applied);
        Assert.Contains("; keep this comment", applied);
        Assert.Contains("RecursivePaths = ../../../Roms/PlayStation 2", applied);
        Assert.Contains("Bios = ../../Shared/BIOS/PCSX2", applied);
        Assert.Contains("MemoryCards = ../../Shared/Saves/PCSX2", applied);
        Assert.Contains("Textures = ../../Shared/TexturePacks/PCSX2", applied);
        Assert.True(await adapter.RestoreLatestAsync(PlatformKind.Windows));
        Assert.Equal(original, await File.ReadAllTextAsync(configuration));
        Assert.False(await adapter.RestoreLatestAsync(PlatformKind.Windows));
    }

    [Fact]
    public async Task GameDirectoryIsAddedWithoutRemovingExistingSearchDirectories()
    {
        var configuration = await InstallAsync(PlatformKind.Windows);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
        await File.WriteAllTextAsync(configuration,
            "[GameList]\r\nRecursivePaths = D:/My Other Games\r\n");
        var adapter = new Pcsx2ConfigurationAdapter(_root, _root);
        var balanced = adapter.Translate(adapter.Presets.Single(profile => profile.Id == "balanced"));

        await adapter.ApplyAsync(PlatformKind.Windows, balanced);

        var applied = await File.ReadAllTextAsync(configuration);
        Assert.Contains("RecursivePaths = D:/My Other Games", applied);
        Assert.Contains("RecursivePaths = ../../../Roms/PlayStation 2", applied);
    }

    [Fact]
    public async Task OnlyImportedBiosIsSelectedAutomatically()
    {
        var configuration = await InstallAsync(PlatformKind.Windows);
        var biosFolder = Path.Combine(_root, "Emulators", "Shared", "BIOS", "PCSX2");
        Directory.CreateDirectory(biosFolder);
        await File.WriteAllBytesAsync(Path.Combine(biosFolder, "SCPH-TEST.bin"), [1, 2, 3, 4]);
        var adapter = new Pcsx2ConfigurationAdapter(_root, _root);
        var balanced = adapter.Translate(adapter.Presets.Single(profile => profile.Id == "balanced"));

        await adapter.ApplyAsync(PlatformKind.Windows, balanced);

        Assert.Contains("BIOS = SCPH-TEST.bin", await File.ReadAllTextAsync(configuration));
    }

    [Fact]
    public async Task MissingLinuxConfigurationGetsPortableRecommendedSettings()
    {
        var configuration = await InstallAsync(PlatformKind.Linux);
        var adapter = new Pcsx2ConfigurationAdapter(_root, _root);
        var quality = adapter.Translate(adapter.Presets.Single(profile => profile.Id == "quality"));

        await adapter.ApplyAsync(PlatformKind.Linux, quality);

        Assert.True(File.Exists(configuration));
        var contents = await File.ReadAllTextAsync(configuration);
        Assert.Contains("Bios = ../../Shared/BIOS/PCSX2", contents);
        Assert.Contains("upscale_multiplier = 3", contents);
        Assert.Contains("AspectRatio = 16:9", contents);
        Assert.Contains("EnableWideScreenPatches = true", contents);
        Assert.True(await adapter.RestoreLatestAsync(PlatformKind.Linux));
        Assert.False(File.Exists(configuration));
    }

    [Fact]
    public async Task CustomizedProfileProducesGranularPreview()
    {
        await InstallAsync(PlatformKind.Windows);
        var adapter = new Pcsx2ConfigurationAdapter(_root, _root);
        var custom = adapter.Customize(adapter.Presets.Single(profile => profile.Id == "balanced"),
            4, 14, "4:3", fullscreen: false, vsync: true, widescreenPatches: false);

        var preview = await adapter.PreviewAsync(PlatformKind.Windows, custom);

        Assert.Contains(preview.Changes, change => change.Key == "Renderer" && change.NewValue == "14");
        Assert.Contains(preview.Changes, change => change.Key == "upscale_multiplier" && change.NewValue == "4");
        Assert.Contains(preview.Changes, change => change.Key == "AspectRatio" && change.NewValue == "4:3");
        Assert.Contains(preview.Changes, change => change.Key == "StartFullscreen" && change.NewValue == "false");
        Assert.Contains(preview.Changes, change => change.Key == "VsyncEnable" && change.NewValue == "1");
    }

    [Fact]
    public async Task LinuxRejectsWindowsOnlyRenderer()
    {
        await InstallAsync(PlatformKind.Linux);
        var adapter = new Pcsx2ConfigurationAdapter(_root, _root);
        var direct3d = adapter.Customize(adapter.Presets.Single(profile => profile.Id == "balanced"),
            2, 15, "Auto 4:3/3:2", fullscreen: true, vsync: false, widescreenPatches: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.PreviewAsync(PlatformKind.Linux, direct3d));
    }

    private async Task<string> InstallAsync(PlatformKind platform)
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
        return platform == PlatformKind.Windows
            ? Path.Combine(Path.GetDirectoryName(executable)!, "inis", "PCSX2.ini")
            : executable + ".config" + Path.DirectorySeparatorChar + Path.Combine("PCSX2", "inis", "PCSX2.ini");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
