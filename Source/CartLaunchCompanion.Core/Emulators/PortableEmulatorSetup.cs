using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Emulators;

public sealed record ManagedSetupDefinition(
    string EmulatorId, string DisplayName, string RomFolder, string Requirement,
    bool CanApplyAutomatically, string AutomationSummary);

public static class ManagedSetupCatalog
{
    public static IReadOnlyList<ManagedSetupDefinition> All { get; } =
    [
        new("ppsspp", "PPSSPP", "PSP", "No BIOS or firmware required", true,
            "Game folder, portable memory stick, fullscreen, and graphics preset"),
        new("duckstation", "DuckStation", "PlayStation", "A legally dumped PlayStation BIOS is required", true,
            "Game folder, portable data folders, fullscreen, and graphics preset"),
        new("pcsx2", "PCSX2", "PlayStation 2", "A legally dumped PlayStation 2 BIOS is required", true,
            "Game folder, BIOS, portable data folders, controller setup, and graphics preset"),
        new("rpcs3", "RPCS3", "PlayStation 3", "Install the official PS3UPDAT.PUP firmware in RPCS3", true,
            "Generated configuration, firmware readiness, fullscreen, Vulkan rendering, and the selected resolution preset"),
        new("shadps4", "shadPS4", "PlayStation 4", "Console modules may be required by individual games", true,
            "Game, DLC, home, module, controller, and fullscreen settings using shadPS4's native JSON format")
    ];

    public static ManagedSetupDefinition Get(string emulatorId) =>
        All.Single(item => item.EmulatorId == emulatorId);
}

public sealed record PortableSetupTarget(
    PlatformKind Platform, string ExecutablePath, string ConfigurationPath, bool ConfigurationExists);

public sealed record PortableSetupPreview(
    PortableSetupTarget Target, IReadOnlyList<string> Changes, bool CanApply, string Guidance);

/// <summary>Shared preset-first setup for emulators with verified portable INI formats.</summary>
public sealed class PortableEmulatorSetupService(string mediaRoot, string stateRoot, string emulatorId,
    ISharedResourceLinkManager? resourceLinks = null)
{
    private readonly string _mediaRoot = Path.GetFullPath(mediaRoot);
    private readonly string _stateRoot = Path.GetFullPath(stateRoot);
    private readonly IEmulatorRegistryStore _registry = new EmulatorRegistryStore(stateRoot);
    private readonly ISharedResourceLinkManager _resourceLinks = resourceLinks ?? new SharedResourceLinkManager(mediaRoot, stateRoot);
    public string MediaRoot => _mediaRoot;
    public string StateRoot => _stateRoot;
    public ManagedSetupDefinition Definition { get; } = ManagedSetupCatalog.Get(emulatorId);

    public async Task<IReadOnlyList<PortableSetupTarget>> InspectAsync(CancellationToken token = default)
    {
        var installs = (await _registry.LoadAsync(token)).Installations
            .Where(item => item.EmulatorId == Definition.EmulatorId).OrderBy(item => item.Platform).ToArray();
        return installs.Select(item =>
        {
            var executable = EmulatorPathContract.Resolve(_mediaRoot, item.ExecutableRelativePath);
            var config = ConfigurationPath(item.Platform, executable);
            return new PortableSetupTarget(item.Platform, executable, config, File.Exists(config));
        }).ToArray();
    }

    public async Task<PortableSetupPreview> PreviewAsync(PortableSetupTarget target, SimpleEmulationPreset preset,
        CancellationToken token = default)
    {
        if (Definition.EmulatorId == "rpcs3")
            return await PreviewRpcs3Async(target, preset, token);
        if (Definition.EmulatorId == "shadps4")
            return await PreviewShadPs4Async(target, token);
        if (!Definition.CanApplyAutomatically)
            return new(target, [], false, Definition.AutomationSummary + ". Open the emulator to complete this step.");
        var document = await IniFile.LoadAsync(target.ConfigurationPath, token);
        var changes = ValuesFor(preset, target).Where(value => document.Needs(value))
            .Select(value => value.DisplayName).ToArray();
        if (Definition.EmulatorId == "ppsspp")
            changes = changes.Concat(_resourceLinks.PreviewMigrations(PpssppMigrations(target))).ToArray();
        if (Definition.EmulatorId == "duckstation")
            changes = changes.Concat(_resourceLinks.PreviewMigrations(DuckStationMigrations(target))).ToArray();
        return new(target, changes, true, changes.Length == 0
            ? "Portable folders and the selected preset are already applied."
            : $"{changes.Length} setup items are ready to apply.");
    }

    private async Task<PortableSetupPreview> PreviewRpcs3Async(PortableSetupTarget target,
        SimpleEmulationPreset preset, CancellationToken token)
    {
        if (!File.Exists(target.ExecutablePath))
            return new(target, [], false, "The RPCS3 program files are missing. Reinstall this build before continuing.");
        var document = await Rpcs3Yaml.LoadAsync(target.ConfigurationPath, token);
        var changes = Rpcs3ValuesFor(preset).Where(document.Needs).Select(item => item.DisplayName).ToList();
        var vfs = await Rpcs3VfsYaml.LoadAsync(Rpcs3VfsPath(target), token);
        foreach (var value in Rpcs3VfsValues(target).Where(vfs.Needs)) changes.Add(value.DisplayName);
        changes.AddRange(_resourceLinks.PreviewMigrations(Rpcs3Migrations(target)));
        if (!Rpcs3FirmwareExists(target))
            return new(target, changes, true,
                "Apply this setup first. Then open RPCS3 on either operating system, choose File > Install Firmware, select your official PS3UPDAT.PUP, wait for installation to finish, and close RPCS3. The firmware, saves, trophies, updates, and DLC will be available to both builds.");
        return new(target, changes, true, changes.Count == 0
            ? "RPCS3 firmware, shared portable data, and the selected play-style preset are ready."
            : "Firmware is installed. Apply the remaining portable-data and play-style settings below.");
    }

    private bool Rpcs3FirmwareExists(PortableSetupTarget target) =>
        File.Exists(EmulatorPathContract.Resolve(Rpcs3SharedRoot(), "dev_flash/vsh/module/vsh.self")) ||
        File.Exists(EmulatorPathContract.Resolve(Rpcs3DataRoot(target), "dev_flash/vsh/module/vsh.self"));

    private static string Rpcs3DataRoot(PortableSetupTarget target) => target.Platform == PlatformKind.Windows
        ? Path.GetDirectoryName(target.ExecutablePath)!
        : target.ExecutablePath + ".config/rpcs3";

    private static string Rpcs3VfsPath(PortableSetupTarget target) =>
        EmulatorPathContract.Resolve(Rpcs3DataRoot(target), "vfs.yml");

    private string Rpcs3SharedRoot() =>
        EmulatorPathContract.Resolve(_mediaRoot, "Emulators/Shared/RPCS3");

    private IReadOnlyList<SharedResourceMigration> Rpcs3Migrations(PortableSetupTarget target)
    {
        var local = Rpcs3DataRoot(target);
        var shared = Rpcs3SharedRoot();
        return
        [
            new(Path.Combine(local, "dev_hdd0"), Path.Combine(shared, "dev_hdd0"), "RPCS3 saves, trophies, installed games, updates, and DLC"),
            new(Path.Combine(local, "dev_flash"), Path.Combine(shared, "dev_flash"), "RPCS3 firmware"),
            new(Path.Combine(local, "dev_flash2"), Path.Combine(shared, "dev_flash2"), "RPCS3 firmware settings"),
            new(Path.Combine(local, "dev_flash3"), Path.Combine(shared, "dev_flash3"), "RPCS3 firmware data")
        ];
    }

    private IReadOnlyList<Rpcs3VfsValue> Rpcs3VfsValues(PortableSetupTarget target)
    {
        var dataRoot = Rpcs3DataRoot(target);
        string Portable(string folder)
        {
            var destination = Path.Combine(Rpcs3SharedRoot(), folder);
            var relative = Path.GetRelativePath(dataRoot, destination).Replace('\\', '/');
            return "$(EmulatorDir)" + relative.TrimStart('/') + "/";
        }
        return
        [
            new("/dev_hdd0/", Portable("dev_hdd0"), "Use the shared RPCS3 saves, trophies, updates, and DLC"),
            new("/dev_flash/", Portable("dev_flash"), "Use the shared RPCS3 firmware"),
            new("/dev_flash2/", Portable("dev_flash2"), "Use the shared RPCS3 firmware settings"),
            new("/dev_flash3/", Portable("dev_flash3"), "Use the shared RPCS3 firmware data")
        ];
    }

    public async Task ApplyAsync(PortableSetupTarget target, SimpleEmulationPreset preset,
        CancellationToken token = default)
    {
        GameContentLayout.PreparePlatform(_mediaRoot, Definition.RomFolder);
        SharedEmulatorResourceLayout.Prepare(_mediaRoot, Definition.DisplayName);
        if (Definition.EmulatorId == "rpcs3")
        {
            await ApplyRpcs3Async(target, preset, token);
            return;
        }
        if (Definition.EmulatorId == "shadps4")
        {
            await ApplyShadPs4Async(target, token);
            return;
        }
        if (!Definition.CanApplyAutomatically) throw new NotSupportedException(Definition.AutomationSummary);
        EnsureNotRunning();
        if (Definition.EmulatorId == "ppsspp")
            await _resourceLinks.MigrateAsync(PpssppMigrations(target), token);
        if (Definition.EmulatorId == "duckstation")
            await _resourceLinks.MigrateAsync(DuckStationMigrations(target), token);
        var document = await IniFile.LoadAsync(target.ConfigurationPath, token);
        var values = ValuesFor(preset, target);
        if (!values.Any(document.Needs)) return;
        Directory.CreateDirectory(EmulatorPathContract.Resolve(_mediaRoot, $"Roms/{Definition.RomFolder}"));
        Directory.CreateDirectory(Path.GetDirectoryName(target.ConfigurationPath)!);
        if (File.Exists(target.ConfigurationPath))
        {
            var backup = EmulatorPathContract.Resolve(_stateRoot,
                $"Config/EmulatorCompanion/Backups/{Definition.EmulatorId}/{target.Platform}/latest.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(target.ConfigurationPath, backup, true);
        }
        foreach (var value in values) document.Apply(value);
        EnsureNotRunning();
        await document.SaveAsync(token);
    }

    private IReadOnlyList<SharedResourceMigration> PpssppMigrations(PortableSetupTarget target)
    {
        var system = Path.GetDirectoryName(target.ConfigurationPath)!;
        var psp = Directory.GetParent(system)?.FullName
            ?? throw new InvalidDataException("The PPSSPP memory-stick path is invalid.");
        var sharedPsp = Path.Combine(PpssppPortablePaths.MemoryStickRoot(_mediaRoot), "PSP");
        return
        [
            new(Path.Combine(psp, "SAVEDATA"), Path.Combine(sharedPsp, "SAVEDATA"), "PPSSPP saved games"),
            new(Path.Combine(psp, "PPSSPP_STATE"), Path.Combine(sharedPsp, "PPSSPP_STATE"), "PPSSPP save states"),
            new(Path.Combine(psp, "TEXTURES"), Path.Combine(sharedPsp, "TEXTURES"), "PPSSPP texture packs")
        ];
    }

    private IReadOnlyList<SharedResourceMigration> DuckStationMigrations(PortableSetupTarget target)
    {
        var dataRoot = Path.GetDirectoryName(target.ConfigurationPath)!;
        return
        [
            new(Path.Combine(dataRoot, "bios"), SharedEmulatorResourceLayout.GetPath(_mediaRoot, "BIOS", "DuckStation"), "DuckStation BIOS files"),
            new(Path.Combine(dataRoot, "memcards"), SharedEmulatorResourceLayout.GetPath(_mediaRoot, "Saves", "DuckStation"), "DuckStation memory cards"),
            new(Path.Combine(dataRoot, "savestates"), SharedEmulatorResourceLayout.GetPath(_mediaRoot, "States", "DuckStation"), "DuckStation save states"),
            new(Path.Combine(dataRoot, "screenshots"), SharedEmulatorResourceLayout.GetPath(_mediaRoot, "Screenshots", "DuckStation"), "DuckStation screenshots"),
            new(Path.Combine(dataRoot, "cheats"), SharedEmulatorResourceLayout.GetPath(_mediaRoot, "Cheats", "DuckStation"), "DuckStation cheats"),
            new(Path.Combine(dataRoot, "textures"), SharedEmulatorResourceLayout.GetPath(_mediaRoot, "TexturePacks", "DuckStation"), "DuckStation texture packs")
        ];
    }

    private async Task ApplyRpcs3Async(PortableSetupTarget target, SimpleEmulationPreset preset,
        CancellationToken token)
    {
        var preview = await PreviewRpcs3Async(target, preset, token);
        if (!preview.CanApply) throw new InvalidOperationException(preview.Guidance);
        EnsureNotRunning();
        await _resourceLinks.MigrateAsync(Rpcs3Migrations(target), token);
        foreach (var folder in new[] { "dev_hdd0", "dev_flash", "dev_flash2", "dev_flash3" })
            Directory.CreateDirectory(Path.Combine(Rpcs3SharedRoot(), folder));

        var vfs = await Rpcs3VfsYaml.LoadAsync(Rpcs3VfsPath(target), token);
        var vfsValues = Rpcs3VfsValues(target);
        if (vfsValues.Any(vfs.Needs))
        {
            Backup(Rpcs3VfsPath(target), target.Platform, "-vfs.yml");
            foreach (var value in vfsValues) vfs.Apply(value);
            await vfs.SaveAsync(token);
        }

        var document = await Rpcs3Yaml.LoadAsync(target.ConfigurationPath, token);
        var values = Rpcs3ValuesFor(preset);
        if (!values.Any(document.Needs)) return;
        var backup = EmulatorPathContract.Resolve(_stateRoot,
            $"Config/EmulatorCompanion/Backups/{Definition.EmulatorId}/{target.Platform}/latest.yml");
        if (File.Exists(target.ConfigurationPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(target.ConfigurationPath, backup, true);
        }
        foreach (var value in values) document.Apply(value);
        EnsureNotRunning();
        await document.SaveAsync(token);
    }

    private async Task<PortableSetupPreview> PreviewShadPs4Async(PortableSetupTarget target, CancellationToken token)
    {
        if (!File.Exists(target.ExecutablePath))
            return new(target, [], false, "The shadPS4 program files are missing. Reinstall this build before continuing.");
        var root = await LoadJsonAsync(target.ConfigurationPath, "shadPS4", token);
        var changes = ShadPs4Changes(root, target)
            .Concat(_resourceLinks.PreviewMigrations(ShadPs4Migrations(target))).ToArray();
        return new(target, changes, true, changes.Length == 0
            ? "shadPS4 portable folders and launch settings are ready."
            : "The shadPS4 configuration can be generated without opening the emulator.");
    }

    private async Task ApplyShadPs4Async(PortableSetupTarget target, CancellationToken token)
    {
        EnsureNotRunning();
        await _resourceLinks.MigrateAsync(ShadPs4Migrations(target), token);
        var root = await LoadJsonAsync(target.ConfigurationPath, "shadPS4", token);
        if (!ShadPs4Changes(root, target).Any()) return;
        Backup(target.ConfigurationPath, target.Platform, ".json");
        var general = root["General"] as JsonObject;
        if (general is null) { general = []; root["General"] = general; }
        var gpu = root["GPU"] as JsonObject;
        if (gpu is null) { gpu = []; root["GPU"] = gpu; }
        var input = root["Input"] as JsonObject;
        if (input is null) { input = []; root["Input"] = input; }
        var romPath = $"../../../Roms/{Definition.RomFolder}";
        var managedGamePath = ShadPs4SharedRelative(target, "games");
        var installDirs = general["install_dirs"] as JsonArray;
        if (installDirs is null)
        {
            installDirs = [];
            general["install_dirs"] = installDirs;
        }

        if (!installDirs.OfType<JsonObject>().Any(item => item["path"]?.GetValue<string>() == romPath))
            installDirs.Add(new JsonObject { ["path"] = romPath, ["enabled"] = true });
        if (!installDirs.OfType<JsonObject>().Any(item => item["path"]?.GetValue<string>() == managedGamePath))
            installDirs.Add(new JsonObject { ["path"] = managedGamePath, ["enabled"] = true });
        general["addon_install_dir"] = ShadPs4SharedRelative(target, "addcont");
        general["home_dir"] = ShadPs4SharedRelative(target, "home");
        general["sys_modules_dir"] = "user/sys_modules";
        general["font_dir"] = "user/fonts";
        gpu["full_screen"] = true;
        gpu["full_screen_mode"] = "Borderless";
        input["use_unified_input_config"] = true;
        var folder = Path.GetDirectoryName(target.ExecutablePath)!;
        foreach (var relative in new[] { "user/sys_modules", "user/fonts" })
            Directory.CreateDirectory(EmulatorPathContract.Resolve(folder, relative));
        foreach (var relative in new[] { "games", "addcont", "home" })
            Directory.CreateDirectory(Path.Combine(ShadPs4SharedRoot(), relative));
        Directory.CreateDirectory(EmulatorPathContract.Resolve(_mediaRoot, $"Roms/{Definition.RomFolder}"));
        await WriteJsonAsync(target.ConfigurationPath, root, token);
    }

    private string ShadPs4SharedRoot() => EmulatorPathContract.Resolve(_mediaRoot, "Emulators/Shared/shadPS4");

    private string ShadPs4SharedRelative(PortableSetupTarget target, string folder) =>
        Path.GetRelativePath(Path.GetDirectoryName(target.ExecutablePath)!,
            Path.Combine(ShadPs4SharedRoot(), folder))
            .Replace('\\', '/');

    private IReadOnlyList<SharedResourceMigration> ShadPs4Migrations(PortableSetupTarget target)
    {
        var local = Path.Combine(Path.GetDirectoryName(target.ExecutablePath)!, "user");
        var shared = ShadPs4SharedRoot();
        return
        [
            new(Path.Combine(local, "home"), Path.Combine(shared, "home"), "shadPS4 saves and user data"),
            new(Path.Combine(local, "games"), Path.Combine(shared, "games"), "shadPS4 installed games and updates"),
            new(Path.Combine(local, "addcont"), Path.Combine(shared, "addcont"), "shadPS4 DLC")
        ];
    }

    private IEnumerable<string> ShadPs4Changes(JsonObject root, PortableSetupTarget target)
    {
        var general = root["General"] as JsonObject;
        var gpu = root["GPU"] as JsonObject;
        var input = root["Input"] as JsonObject;
        var romPath = $"../../../Roms/{Definition.RomFolder}";
        var dirs = general?["install_dirs"] as JsonArray;
        if (dirs is null || !dirs.OfType<JsonObject>().Any(item => item["path"]?.GetValue<string>() == romPath && item["enabled"]?.GetValue<bool>() == true))
            yield return "Add the portable PlayStation 4 game folder";
        if (dirs is null || !dirs.OfType<JsonObject>().Any(item => item["path"]?.GetValue<string>() == ShadPs4SharedRelative(target, "games") && item["enabled"]?.GetValue<bool>() == true))
            yield return "Add the shared shadPS4 game and update folder";
        if (general?["addon_install_dir"]?.GetValue<string>() != ShadPs4SharedRelative(target, "addcont")) yield return "Set the shared DLC folder";
        if (general?["home_dir"]?.GetValue<string>() != ShadPs4SharedRelative(target, "home")) yield return "Set the shared saves and user-data folder";
        if (general?["sys_modules_dir"]?.GetValue<string>() != "user/sys_modules") yield return "Set the console-module folder";
        if (general?["font_dir"]?.GetValue<string>() != "user/fonts") yield return "Set the portable font folder";
        if (gpu?["full_screen"]?.GetValue<bool>() != true || gpu?["full_screen_mode"]?.GetValue<string>() != "Borderless")
            yield return "Start games in borderless fullscreen";
        if (input?["use_unified_input_config"]?.GetValue<bool>() != true) yield return "Use the unified controller configuration";
    }

    private static async Task<JsonObject> LoadJsonAsync(string path, string displayName, CancellationToken token)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException($"The {displayName} configuration is too large.");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, token)) as JsonObject
            ?? throw new InvalidDataException($"The {displayName} configuration is unreadable.");
    }

    private static async Task WriteJsonAsync(string path, JsonObject root, CancellationToken token) =>
        await NativeControllerConfigurationFiles.AtomicWriteAsync(path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, token);

    private void Backup(string path, PlatformKind platform, string extension)
    {
        if (!File.Exists(path)) return;
        var backup = EmulatorPathContract.Resolve(_stateRoot,
            $"Config/EmulatorCompanion/Backups/{Definition.EmulatorId}/{platform}/latest{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(path, backup, true);
    }

    private static IReadOnlyList<Rpcs3Value> Rpcs3ValuesFor(SimpleEmulationPreset preset) =>
    [
        new("Video", "Renderer", "Vulkan", "Use Vulkan rendering"),
        new("Video", "Resolution Scale", preset.Resolution switch
        {
            EmulationResolutionGoal.Original => "100", EmulationResolutionGoal.Hd => "150",
            EmulationResolutionGoal.FullHd => "200", _ => "300"
        }, $"Use the {preset.DisplayName} resolution preset"),
        new("Video", "VSync", preset.Vsync ? "true" : "false", "Vertical sync"),
        new("Miscellaneous", "Start games in fullscreen mode", "true", "Start games fullscreen")
    ];

    private IReadOnlyList<SetupValue> ValuesFor(SimpleEmulationPreset preset, PortableSetupTarget target)
    {
        if (!SimpleEmulationPresetCatalog.All.Contains(preset)) throw new InvalidDataException("Unknown setup preset.");
        var portableRomPath = $"../../../Roms/{Definition.RomFolder}";
        return Definition.EmulatorId switch
        {
            "ppsspp" =>
            [
                new("General", "FirstRun", "False", "Complete PPSSPP's generated first-run configuration"),
                new("General", "CurrentDirectory", portableRomPath, "Set the PSP game folder"),
                new("Graphics", "InternalResolution", preset.Resolution switch
                {
                    EmulationResolutionGoal.Original => "1", EmulationResolutionGoal.Hd => "2",
                    EmulationResolutionGoal.FullHd => "4", _ => "8"
                }, $"Use the {preset.DisplayName} graphics preset"),
                new("Graphics", "FullScreen", "True", "Start games fullscreen"),
                new("Graphics", "VerticalSync", preset.Vsync ? "True" : "False", "Vertical sync")
            ],
            "duckstation" =>
            [
                new("GameList", "RecursivePaths", DuckStationRelative(target, EmulatorPathContract.Resolve(_mediaRoot, $"Roms/{Definition.RomFolder}")), "Add the PlayStation game folder", true),
                new("BIOS", "SearchDirectory", DuckStationRelative(target, SharedEmulatorResourceLayout.GetPath(_mediaRoot, "BIOS", "DuckStation")), "Use the shared BIOS folder"),
                new("MemoryCards", "Directory", DuckStationRelative(target, SharedEmulatorResourceLayout.GetPath(_mediaRoot, "Saves", "DuckStation")), "Use the shared memory-card folder"),
                new("Folders", "SaveStates", DuckStationRelative(target, SharedEmulatorResourceLayout.GetPath(_mediaRoot, "States", "DuckStation")), "Use the shared save-state folder"),
                new("Folders", "Screenshots", DuckStationRelative(target, SharedEmulatorResourceLayout.GetPath(_mediaRoot, "Screenshots", "DuckStation")), "Use the shared screenshot folder"),
                new("Folders", "Cheats", DuckStationRelative(target, SharedEmulatorResourceLayout.GetPath(_mediaRoot, "Cheats", "DuckStation")), "Use the shared cheats folder"),
                new("Folders", "Textures", DuckStationRelative(target, SharedEmulatorResourceLayout.GetPath(_mediaRoot, "TexturePacks", "DuckStation")), "Use the shared texture-pack folder"),
                new("Main", "StartFullscreen", "true", "Start games fullscreen"),
                new("GPU", "Renderer", "Automatic", "Choose the renderer automatically"),
                new("GPU", "ResolutionScale", preset.Resolution switch
                {
                    EmulationResolutionGoal.Original => "1", EmulationResolutionGoal.Hd => "3",
                    EmulationResolutionGoal.FullHd => "5", _ => "9"
                }, $"Use the {preset.DisplayName} graphics preset"),
                new("GPU", "WidescreenHack", preset.WidescreenEnhancements ? "true" : "false", "Widescreen enhancement"),
                new("Display", "AspectRatio", preset.Aspect == EmulationAspectGoal.Widescreen ? "16:9" : "Auto (Game Native)", "Aspect ratio"),
                new("Display", "VSync", preset.Vsync ? "true" : "false", "Vertical sync")
            ],
            _ => []
        };
    }

    private static string DuckStationRelative(PortableSetupTarget target, string destination) =>
        Path.GetRelativePath(Path.GetDirectoryName(target.ConfigurationPath)!, destination).Replace('\\', '/');

    private string ConfigurationPath(PlatformKind platform, string executable)
    {
        var folder = Path.GetDirectoryName(executable)!;
        return (Definition.EmulatorId, platform) switch
        {
            ("ppsspp", PlatformKind.Windows) => PpssppPortablePaths.ConfigurationPath(executable, platform),
            ("ppsspp", PlatformKind.Linux) => PpssppPortablePaths.ConfigurationPath(executable, platform),
            ("duckstation", PlatformKind.Windows) => EmulatorPathContract.Resolve(folder, "settings.ini"),
            ("duckstation", PlatformKind.Linux) => EmulatorPathContract.Resolve(folder, "settings.ini"),
            ("rpcs3", PlatformKind.Windows) => EmulatorPathContract.Resolve(folder, "config/config.yml"),
            ("rpcs3", PlatformKind.Linux) => EmulatorPathContract.Resolve(folder, "RPCS3.AppImage.config/rpcs3/config/config.yml"),
            ("shadps4", PlatformKind.Windows) => EmulatorPathContract.Resolve(folder, "user/config.json"),
            ("shadps4", PlatformKind.Linux) => EmulatorPathContract.Resolve(folder, "user/config.json"),
            _ => Path.Combine(folder, "user", "configuration.pending")
        };
    }

    private void EnsureNotRunning()
    {
        var prefixes = Definition.EmulatorId == "ppsspp" ? new[] { "ppsspp" } : new[] { Definition.EmulatorId };
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matched = false;
                try
                {
                    if (!prefixes.Any(prefix => process.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
                    matched = true;
                    throw new IOException($"Close {Definition.DisplayName} before applying its setup.");
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error) when (matched)
                { throw new IOException($"Close {Definition.DisplayName} before applying its setup.", error); }
            }
        }
    }

    private sealed record SetupValue(string Section, string Key, string Value, string DisplayName, bool IsList = false);
    private sealed record Rpcs3Value(string Section, string Key, string Value, string DisplayName);
    private sealed record Rpcs3VfsValue(string Key, string Value, string DisplayName);

    private sealed class Rpcs3VfsYaml
    {
        private readonly string _path;
        private readonly List<string> _lines;
        private Rpcs3VfsYaml(string path, List<string> lines) { _path = path; _lines = lines; }

        public static async Task<Rpcs3VfsYaml> LoadAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new(path, []);
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The RPCS3 VFS configuration is too large.");
            return new(path, (await File.ReadAllLinesAsync(path, token)).ToList());
        }

        public bool Needs(Rpcs3VfsValue value) =>
            !string.Equals(Get(value.Key), value.Value, StringComparison.Ordinal);

        public void Apply(Rpcs3VfsValue value)
        {
            var prefix = value.Key + ":";
            var index = _lines.FindIndex(line => line.StartsWith(prefix, StringComparison.Ordinal));
            var replacement = $"{prefix} {value.Value}";
            if (index >= 0) _lines[index] = replacement;
            else _lines.Add(replacement);
        }

        public Task SaveAsync(CancellationToken token) => NativeControllerConfigurationFiles.AtomicWriteAsync(
            _path, string.Join(Environment.NewLine, _lines) + Environment.NewLine, token);

        private string? Get(string key)
        {
            var prefix = key + ":";
            var line = _lines.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal));
            return line is null ? null : line[prefix.Length..].Trim();
        }
    }

    private sealed class Rpcs3Yaml
    {
        private readonly string _path;
        private readonly List<string> _lines;
        private Rpcs3Yaml(string path, List<string> lines) { _path = path; _lines = lines; }
        public static async Task<Rpcs3Yaml> LoadAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new(path, []);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The RPCS3 configuration is too large.");
            var text = await File.ReadAllTextAsync(path, token);
            return new(path, text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList());
        }
        public bool Needs(Rpcs3Value value) => !string.Equals(Get(value.Section, value.Key), value.Value, StringComparison.Ordinal);
        public void Apply(Rpcs3Value value)
        {
            var range = Range(value.Section);
            if (range is null)
            {
                if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
                _lines.Add(value.Section + ":");
                _lines.Add($"  {value.Key}: {value.Value}");
                return;
            }
            for (var i = range.Value.Start + 1; i < range.Value.End; i++)
            {
                var trimmed = _lines[i].TrimStart();
                if (_lines[i].Length - trimmed.Length != 2 || !trimmed.StartsWith(value.Key + ":", StringComparison.Ordinal)) continue;
                _lines[i] = $"  {value.Key}: {value.Value}";
                return;
            }
            _lines.Insert(range.Value.End, $"  {value.Key}: {value.Value}");
        }
        public async Task SaveAsync(CancellationToken token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, string.Join(Environment.NewLine, _lines).TrimEnd() + Environment.NewLine,
                    new UTF8Encoding(false), token);
                File.Move(temporary, _path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private string? Get(string section, string key)
        {
            var range = Range(section); if (range is null) return null;
            for (var i = range.Value.Start + 1; i < range.Value.End; i++)
            {
                var trimmed = _lines[i].TrimStart();
                if (_lines[i].Length - trimmed.Length != 2 || !trimmed.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                return trimmed[(key.Length + 1)..].Trim();
            }
            return null;
        }
        private (int Start, int End)? Range(string section)
        {
            var heading = section + ":";
            for (var i = 0; i < _lines.Count; i++)
            {
                if (!string.Equals(_lines[i].TrimEnd(), heading, StringComparison.Ordinal) || _lines[i].Length != heading.Length) continue;
                var end = i + 1;
                while (end < _lines.Count && (_lines[end].Length == 0 || char.IsWhiteSpace(_lines[end][0]))) end++;
                return (i, end);
            }
            return null;
        }
    }

    private sealed class IniFile
    {
        private readonly string _path;
        private readonly List<string> _lines;
        private IniFile(string path, List<string> lines) { _path = path; _lines = lines; }
        public static async Task<IniFile> LoadAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new(path, []);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The emulator configuration is too large.");
            var text = await File.ReadAllTextAsync(path, token);
            return new(path, text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList());
        }
        public bool Needs(SetupValue value) => value.IsList
            ? !GetAll(value.Section, value.Key).Contains(value.Value, StringComparer.OrdinalIgnoreCase)
            : !string.Equals(GetAll(value.Section, value.Key).FirstOrDefault(), value.Value, StringComparison.Ordinal);
        public void Apply(SetupValue value)
        {
            if (value.IsList && !Needs(value)) return;
            var range = Range(value.Section);
            if (range is null)
            {
                if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
                _lines.Add($"[{value.Section}]"); _lines.Add($"{value.Key} = {value.Value}"); return;
            }
            if (!value.IsList)
                for (var i = range.Value.Start + 1; i < range.Value.End; i++)
                    if (TryKey(_lines[i], out var key, out _) && key.Equals(value.Key, StringComparison.OrdinalIgnoreCase))
                    { _lines[i] = $"{value.Key} = {value.Value}"; return; }
            _lines.Insert(range.Value.End, $"{value.Key} = {value.Value}");
        }
        public async Task SaveAsync(CancellationToken token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, string.Join(Environment.NewLine, _lines).TrimEnd() + Environment.NewLine,
                    new UTF8Encoding(false), token);
                File.Move(temp, _path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private IReadOnlyList<string> GetAll(string section, string key)
        {
            var range = Range(section); if (range is null) return [];
            var result = new List<string>();
            for (var i = range.Value.Start + 1; i < range.Value.End; i++)
                if (TryKey(_lines[i], out var found, out var value) && found.Equals(key, StringComparison.OrdinalIgnoreCase)) result.Add(value);
            return result;
        }
        private (int Start, int End)? Range(string section)
        {
            var start = -1;
            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i].Trim(); if (!line.StartsWith('[') || !line.EndsWith(']')) continue;
                if (start >= 0) return (start, i);
                if (line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase)) start = i;
            }
            return start < 0 ? null : (start, _lines.Count);
        }
        private static bool TryKey(string line, out string key, out string value)
        {
            key = value = ""; var equals = line.IndexOf('='); var trimmed = line.TrimStart();
            if (equals <= 0 || trimmed.Length == 0 || trimmed[0] is ';' or '#') return false;
            key = line[..equals].Trim(); value = line[(equals + 1)..].Trim(); return key.Length > 0;
        }
    }
}
