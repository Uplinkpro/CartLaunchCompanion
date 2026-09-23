using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PortableEmulatorSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-portable-setup-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PpssppReplacesPersonalGameFolderAndPreservesUnownedSettings()
    {
        var executable = await InstallAsync("ppsspp", "PPSSPP", "PPSSPPWindows64.exe");
        var config = Path.Combine(Path.GetDirectoryName(executable)!, "memstick", "PSP", "SYSTEM", "ppsspp.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        var savedata = Path.Combine(Path.GetDirectoryName(executable)!, "memstick", "PSP", "SAVEDATA");
        Directory.CreateDirectory(savedata);
        await File.WriteAllTextAsync(Path.Combine(savedata, "ULUS12345.bin"), "save data");
        await File.WriteAllTextAsync(config, "[General]\nCurrentDirectory = C:/Users/Someone/Documents\nNickname = Player\n\n[Graphics]\nInternalResolution = 0\n");
        var resources = new RecordingResourceLinkManager();
        var service = new PortableEmulatorSetupService(_root, _root, "ppsspp", resources);
        var target = Assert.Single(await service.InspectAsync());

        var preview = await service.PreviewAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));
        Assert.Contains(preview.Changes, change => change.Contains("PPSSPP saved games", StringComparison.Ordinal));

        await service.ApplyAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));

        var applied = await File.ReadAllTextAsync(config);
        Assert.Contains("CurrentDirectory = ../../../Roms/PSP", applied);
        Assert.Contains("InternalResolution = 2", applied);
        Assert.Contains("Nickname = Player", applied);
        Assert.True(File.Exists(Path.Combine(_root, "Config", "EmulatorCompanion", "Backups", "ppsspp", "Windows", "latest.ini")));
        Assert.Collection(resources.Migrations,
            migration => AssertMigration(migration, "SAVEDATA"),
            migration => AssertMigration(migration, "PPSSPP_STATE"),
            migration => AssertMigration(migration, "TEXTURES"));
    }

    [Fact]
    public async Task DuckStationAddsPortableGameFolderWithoutRemovingCustomFolder()
    {
        var executable = await InstallAsync("duckstation", "DuckStation", "duckstation-qt-x64-ReleaseLTCG.exe");
        var config = Path.Combine(Path.GetDirectoryName(executable)!, "settings.ini");
        var legacyCards = Path.Combine(Path.GetDirectoryName(executable)!, "memcards");
        Directory.CreateDirectory(legacyCards);
        await File.WriteAllTextAsync(Path.Combine(legacyCards, "SCUS-12345.mcd"), "save data");
        await File.WriteAllTextAsync(config, "[GameList]\nRecursivePaths = D:/Other Games\n\n[Main]\nStartFullscreen = false\n");
        var service = new PortableEmulatorSetupService(_root, _root, "duckstation");
        var target = Assert.Single(await service.InspectAsync());

        await service.ApplyAsync(target, SimpleEmulationPresetCatalog.Get("quality"));

        var applied = await File.ReadAllTextAsync(config);
        Assert.Contains("RecursivePaths = D:/Other Games", applied);
        Assert.Contains("RecursivePaths = ../../../Roms/PlayStation", applied);
        Assert.Contains("ResolutionScale = 5", applied);
        Assert.Contains("WidescreenHack = true", applied);
        Assert.Contains("SearchDirectory = ../../Shared/BIOS/DuckStation", applied);
        Assert.Contains("Directory = ../../Shared/Saves/DuckStation", applied);
        Assert.Contains("SaveStates = ../../Shared/States/DuckStation", applied);
        Assert.Contains("Screenshots = ../../Shared/Screenshots/DuckStation", applied);
        Assert.Contains("Cheats = ../../Shared/Cheats/DuckStation", applied);
        Assert.Contains("Textures = ../../Shared/TexturePacks/DuckStation", applied);
        Assert.Equal("save data", await File.ReadAllTextAsync(Path.Combine(_root, "Emulators", "Shared", "Saves", "DuckStation", "SCUS-12345.mcd")));
        Assert.False(Directory.Exists(legacyCards));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "Config", "EmulatorCompanion", "Backups", "SharedResources"),
            "SCUS-12345.mcd", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DuckStationLinuxUsesPortableSettingsBesideAppImage()
    {
        var executable = await InstallAsync("duckstation", "DuckStation", "DuckStation.AppImage", PlatformKind.Linux);
        var service = new PortableEmulatorSetupService(_root, _root, "duckstation");

        var target = Assert.Single(await service.InspectAsync());
        await service.ApplyAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));

        Assert.Equal(Path.Combine(Path.GetDirectoryName(executable)!, "settings.ini"), target.ConfigurationPath);
        Assert.True(File.Exists(target.ConfigurationPath));
        var applied = await File.ReadAllTextAsync(target.ConfigurationPath);
        Assert.Contains("RecursivePaths = ../../../Roms/PlayStation", applied);
        Assert.Contains("Directory = ../../Shared/Saves/DuckStation", applied);
    }

    [Fact]
    public async Task Rpcs3AdvancesFromFirstRunToFirmwareToReady()
    {
        var executable = await InstallAsync("rpcs3", "RPCS3", "rpcs3.exe");
        var service = new PortableEmulatorSetupService(_root, _root, "rpcs3");
        var target = Assert.Single(await service.InspectAsync());

        var generated = await service.PreviewAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));
        Assert.True(generated.CanApply);
        Assert.Contains("Apply this setup first", generated.Guidance);

        var config = Path.Combine(Path.GetDirectoryName(executable)!, "config", "config.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        await File.WriteAllTextAsync(config, "Core:\n  PPU Decoder: Recompiler (LLVM)\n");
        target = Assert.Single(await service.InspectAsync());
        var firmware = await service.PreviewAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));
        Assert.Contains("Install Firmware", firmware.Guidance);

        var firmwareMarker = Path.Combine(Path.GetDirectoryName(executable)!, "dev_flash", "vsh", "module", "vsh.self");
        Directory.CreateDirectory(Path.GetDirectoryName(firmwareMarker)!);
        await File.WriteAllTextAsync(firmwareMarker, "fixture");
        var ready = await service.PreviewAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));
        Assert.True(ready.CanApply);
        Assert.Contains("remaining portable-data", ready.Guidance);

        await service.ApplyAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));

        var applied = await File.ReadAllTextAsync(config);
        Assert.Contains("Core:\n  PPU Decoder: Recompiler (LLVM)", applied.Replace("\r\n", "\n"));
        Assert.Contains("Video:", applied);
        Assert.Contains("  Renderer: Vulkan", applied);
        Assert.Contains("  Resolution Scale: 150", applied);
        Assert.Contains("Miscellaneous:", applied);
        Assert.Contains("  Start games in fullscreen mode: true", applied);
        var shared = Path.Combine(_root, "Emulators", "Shared", "RPCS3");
        Assert.True(File.Exists(Path.Combine(shared, "dev_flash", "vsh", "module", "vsh.self")));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "dev_flash")));
        var windowsVfs = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(executable)!, "vfs.yml"));
        Assert.Contains("/dev_hdd0/: $(EmulatorDir)../../Shared/RPCS3/dev_hdd0/", windowsVfs);
        Assert.Contains("/dev_flash/: $(EmulatorDir)../../Shared/RPCS3/dev_flash/", windowsVfs);

        var linuxExecutable = await InstallAsync("rpcs3", "RPCS3", "RPCS3.AppImage", PlatformKind.Linux);
        var linuxTarget = (await service.InspectAsync()).Single(item => item.Platform == PlatformKind.Linux);
        var linuxPreview = await service.PreviewAsync(linuxTarget, SimpleEmulationPresetCatalog.Get("balanced"));
        Assert.True(linuxPreview.CanApply);
        Assert.Contains("remaining portable-data", linuxPreview.Guidance);

        await service.ApplyAsync(linuxTarget, SimpleEmulationPresetCatalog.Get("balanced"));

        var linuxConfig = Path.Combine(linuxExecutable + ".config", "rpcs3", "config", "config.yml");
        Assert.Contains("  Renderer: Vulkan", await File.ReadAllTextAsync(linuxConfig));
        var linuxVfs = await File.ReadAllTextAsync(Path.Combine(linuxExecutable + ".config", "rpcs3", "vfs.yml"));
        Assert.Contains("/dev_hdd0/: $(EmulatorDir)../../../../Shared/RPCS3/dev_hdd0/", linuxVfs);
        Assert.Contains("/dev_flash/: $(EmulatorDir)../../../../Shared/RPCS3/dev_flash/", linuxVfs);
    }

    [Fact]
    public async Task ShadPs4CreatesCurrentNativeJsonWithoutFirstRun()
    {
        var executable = await InstallAsync("shadps4", "shadPS4", "shadPS4.exe");
        var service = new PortableEmulatorSetupService(_root, _root, "shadps4");
        var target = Assert.Single(await service.InspectAsync());
        var localHome = Path.Combine(Path.GetDirectoryName(executable)!, "user", "home", "1000", "savedata", "CUSA00001");
        Directory.CreateDirectory(localHome);
        await File.WriteAllTextAsync(Path.Combine(localHome, "SAVE.BIN"), "saved game");
        var localDlc = Path.Combine(Path.GetDirectoryName(executable)!, "user", "addcont", "CUSA00001");
        Directory.CreateDirectory(localDlc);
        await File.WriteAllTextAsync(Path.Combine(localDlc, "dlc.bin"), "dlc");

        Directory.CreateDirectory(Path.GetDirectoryName(target.ConfigurationPath)!);
        await File.WriteAllTextAsync(target.ConfigurationPath,
            "{\"General\":{\"install_dirs\":[{\"path\":\"D:/My PS4 Games\",\"enabled\":true}]},\"Custom\":{\"keep\":true}}");

        var preview = await service.PreviewAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));
        Assert.True(preview.CanApply);
        Assert.Contains("without opening", preview.Guidance);

        await service.ApplyAsync(target, SimpleEmulationPresetCatalog.Get("balanced"));

        var config = Path.Combine(Path.GetDirectoryName(executable)!, "user", "config.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(config))!.AsObject();
        Assert.True(json["GPU"]!["full_screen"]!.GetValue<bool>());
        Assert.Equal("Borderless", json["GPU"]!["full_screen_mode"]!.GetValue<string>());
        Assert.True(json["Input"]!["use_unified_input_config"]!.GetValue<bool>());
        Assert.Equal("D:/My PS4 Games", json["General"]!["install_dirs"]![0]!["path"]!.GetValue<string>());
        Assert.Equal("../../../Roms/PlayStation 4", json["General"]!["install_dirs"]![1]!["path"]!.GetValue<string>());
        Assert.Equal("../../Shared/shadPS4/games", json["General"]!["install_dirs"]![2]!["path"]!.GetValue<string>());
        Assert.Equal("../../Shared/shadPS4/home", json["General"]!["home_dir"]!.GetValue<string>());
        Assert.Equal("../../Shared/shadPS4/addcont", json["General"]!["addon_install_dir"]!.GetValue<string>());
        Assert.True(json["Custom"]!["keep"]!.GetValue<bool>());
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "user", "sys_modules")));
        Assert.Equal("saved game", await File.ReadAllTextAsync(Path.Combine(_root, "Emulators", "Shared", "shadPS4", "home", "1000", "savedata", "CUSA00001", "SAVE.BIN")));
        Assert.Equal("dlc", await File.ReadAllTextAsync(Path.Combine(_root, "Emulators", "Shared", "shadPS4", "addcont", "CUSA00001", "dlc.bin")));
        Assert.False(Directory.Exists(localHome));
        Assert.Empty((await service.PreviewAsync(target, SimpleEmulationPresetCatalog.Get("balanced"))).Changes);
    }

    [Fact]
    public void SetupCatalogCoversEveryManagedInstaller()
    {
        Assert.Equal(new[] { "duckstation", "pcsx2", "ppsspp", "rpcs3", "shadps4" },
            ManagedSetupCatalog.All.Select(item => item.EmulatorId).Order().ToArray());
    }

    private async Task<string> InstallAsync(string id, string folder, string executableName,
        PlatformKind platform = PlatformKind.Windows)
    {
        var platformName = platform.ToString();
        var executable = Path.Combine(_root, "Emulators", platformName, folder, executableName);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "fixture");
        await new EmulatorRegistryStore(_root).UpsertAsync(new()
        {
            EmulatorId = id, Platform = platform,
            ExecutableRelativePath = $"Emulators/{platformName}/{folder}/{executableName}",
            InstalledVersion = "test", InstalledChannelId = "stable", InstalledAt = DateTimeOffset.UtcNow
        });
        return executable;
    }

    private static void AssertMigration(SharedResourceMigration migration, string folder)
    {
        Assert.EndsWith(Path.Combine("PSP", folder), migration.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Emulators", "Shared", "MemorySticks", "PPSSPP", "PSP", folder), migration.TargetPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingResourceLinkManager : ISharedResourceLinkManager
    {
        public List<SharedResourceLink> Ensured { get; } = [];
        public List<SharedResourceMigration> Migrations { get; } = [];

        public IReadOnlyList<string> Preview(IReadOnlyList<SharedResourceLink> links) => links
            .Select(link => $"Create the shared {link.DisplayName} relative link")
            .ToArray();

        public Task EnsureAsync(IReadOnlyList<SharedResourceLink> links, CancellationToken token = default)
        {
            Ensured.AddRange(links);
            return Task.CompletedTask;
        }

        public IReadOnlyList<string> PreviewMigrations(IReadOnlyList<SharedResourceMigration> migrations) => migrations
            .Select(migration => $"Move existing {migration.DisplayName} into shared memory-stick storage")
            .ToArray();

        public Task MigrateAsync(IReadOnlyList<SharedResourceMigration> migrations,
            CancellationToken token = default)
        {
            Migrations.AddRange(migrations);
            return Task.CompletedTask;
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
