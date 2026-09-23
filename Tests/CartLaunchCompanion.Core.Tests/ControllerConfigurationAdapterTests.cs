using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class ControllerConfigurationAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-controller-config-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Pcsx2AppliesVerifiedSdlBindingsAndPreservesOtherSettings()
    {
        var configuration = await InstallAsync("pcsx2", PlatformKind.Windows);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
        await File.WriteAllTextAsync(configuration,
            "; user setting\r\n[Pad1]\r\nCross = Keyboard/X\r\nPressureModifier = Keyboard/P\r\n\r\n[UI]\r\nTheme = custom\r\n");
        var adapter = new Pcsx2ControllerConfigurationAdapter(_root, _root);

        var plan = await adapter.PreviewAsync(PlatformKind.Windows, Request());
        await adapter.ApplyAsync(plan);
        var applied = await File.ReadAllTextAsync(configuration);

        Assert.Equal(ControllerMappingReadiness.ReadyToApply, plan.Readiness);
        Assert.Contains("Type = DualShock2", applied);
        Assert.Contains("Cross = SDL-0/A", applied);
        Assert.Contains("LLeft = SDL-0/-LeftX", applied);
        Assert.Contains("RDown = SDL-0/+RightY", applied);
        Assert.Contains("PressureModifier = Keyboard/P", applied);
        Assert.Contains("Theme = custom", applied);
        Assert.Contains("; user setting", applied);
        Assert.Empty((await adapter.PreviewAsync(PlatformKind.Windows, Request())).NativeChanges);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root,
            "Config", "EmulatorCompanion", "Backups", "pcsx2", "Windows"), "controller-*.ini"));
    }

    [Fact]
    public async Task DuckStationUsesItsNativeTypeAndCanFollowNintendoPrintedLabels()
    {
        var configuration = await InstallAsync("duckstation", PlatformKind.Linux);
        var adapter = new DuckStationControllerConfigurationAdapter(_root, _root);
        var request = Request(ControllerLayoutKind.Nintendo, ControllerFaceButtonPreference.PrintedLabels);

        await adapter.ApplyAsync(await adapter.PreviewAsync(PlatformKind.Linux, request));
        var applied = await File.ReadAllTextAsync(configuration);

        Assert.Contains("[InputSources]", applied);
        Assert.Contains("SDL = true", applied);
        Assert.Contains("Type = AnalogController", applied);
        Assert.Contains("Cross = SDL-0/B", applied);
        Assert.Contains("Circle = SDL-0/A", applied);
        Assert.Contains("Square = SDL-0/Y", applied);
        Assert.Contains("Triangle = SDL-0/X", applied);
    }

    [Fact]
    public async Task IncompleteProfileIsNotApplied()
    {
        await InstallAsync("duckstation", PlatformKind.Windows);
        var adapter = new DuckStationControllerConfigurationAdapter(_root, _root);
        var incomplete = Request() with { Bindings = [new(CanonicalControllerInput.South, "SDL-0/A")] };

        var plan = await adapter.PreviewAsync(PlatformKind.Windows, incomplete);

        Assert.Equal(ControllerMappingReadiness.NeedsManualSetup, plan.Readiness);
        Assert.Empty(plan.NativeChanges);
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.ApplyAsync(plan));
    }

    [Fact]
    public async Task PpssppTranslatesTheSharedLayoutAndPreservesUnownedMappings()
    {
        var configuration = await InstallAsync("ppsspp", PlatformKind.Windows);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
        await File.WriteAllTextAsync(configuration,
            "[ControlMapping]\r\nCross = 1-54\r\nRapidFire = 1-59\r\n");
        var adapter = new PpssppControllerConfigurationAdapter(_root, _root);

        await adapter.ApplyAsync(await adapter.PreviewAsync(PlatformKind.Windows, Request()));
        var applied = await File.ReadAllTextAsync(configuration);

        Assert.Contains("Cross = 10-189", applied);
        Assert.Contains("Circle = 10-190", applied);
        Assert.Contains("L = 10-193", applied);
        Assert.Contains("An.Left = 10-4001", applied);
        Assert.Contains("RapidFire = 1-59", applied);
        Assert.Empty((await adapter.PreviewAsync(PlatformKind.Windows, Request())).NativeChanges);
    }

    [Fact]
    public async Task Rpcs3WritesAnSdlProfileAndPreservesOtherPlayers()
    {
        var configuration = await InstallAsync("rpcs3", PlatformKind.Windows);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
        await File.WriteAllTextAsync(configuration,
            "Player 2 Input:\r\n  Handler: Keyboard\r\n  Device: Keyboard\r\n");
        var adapter = new Rpcs3ControllerConfigurationAdapter(_root, _root);

        await adapter.ApplyAsync(await adapter.PreviewAsync(PlatformKind.Windows, Request()));
        var applied = await File.ReadAllTextAsync(configuration);

        Assert.Contains("Player 1 Input:", applied);
        Assert.Contains("Handler: SDL", applied);
        Assert.Contains("Device: \"Test Controller 1\"", applied);
        Assert.Contains("Cross: South", applied);
        Assert.Contains("Left Stick Left: \"LS X-\"", applied);
        Assert.Contains("Player 2 Input:", applied);
        Assert.Empty((await adapter.PreviewAsync(PlatformKind.Windows, Request())).NativeChanges);
    }

    [Fact]
    public async Task ShadPs4EnablesUnifiedInputWithoutReplacingOtherSettings()
    {
        var configuration = await InstallAsync("shadps4", PlatformKind.Windows);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
        await File.WriteAllTextAsync(configuration, "{\"GPU\":{\"full_screen\":true},\"Input\":{\"background_controller_input\":true}}");
        var adapter = new ShadPs4ControllerConfigurationAdapter(_root, _root);

        await adapter.ApplyAsync(await adapter.PreviewAsync(PlatformKind.Windows, Request()));
        var applied = await File.ReadAllTextAsync(configuration);

        Assert.Contains("\"use_unified_input_config\": true", applied);
        Assert.Contains("\"background_controller_input\": true", applied);
        Assert.Contains("\"full_screen\": true", applied);
        Assert.Empty((await adapter.PreviewAsync(PlatformKind.Windows, Request())).NativeChanges);
    }

    [Fact]
    public async Task PlayerTwoUsesTheSecondNativeSlotAndController()
    {
        var pcsx2Configuration = await InstallAsync("pcsx2", PlatformKind.Windows);
        Directory.CreateDirectory(Path.GetDirectoryName(pcsx2Configuration)!);
        await File.WriteAllTextAsync(pcsx2Configuration, "[Pad1]\r\nCross = SDL-0/A\r\n");
        var pcsx2 = new Pcsx2ControllerConfigurationAdapter(_root, _root);
        await pcsx2.ApplyAsync(await pcsx2.PreviewAsync(PlatformKind.Windows,
            Request(playerNumber: 2)));
        var pcsx2Applied = await File.ReadAllTextAsync(pcsx2Configuration);
        Assert.Contains("[Pad1]", pcsx2Applied);
        Assert.Contains("Cross = SDL-0/A", pcsx2Applied);
        Assert.Contains("[Pad2]", pcsx2Applied);
        Assert.Contains("Cross = SDL-1/A", pcsx2Applied);

        var rpcs3Configuration = await InstallAsync("rpcs3", PlatformKind.Windows);
        var rpcs3 = new Rpcs3ControllerConfigurationAdapter(_root, _root);
        await rpcs3.ApplyAsync(await rpcs3.PreviewAsync(PlatformKind.Windows,
            Request(playerNumber: 2)));
        var rpcs3Applied = await File.ReadAllTextAsync(rpcs3Configuration);
        Assert.Contains("Player 2 Input:", rpcs3Applied);
        Assert.Contains("Device: \"Test Controller 2\"", rpcs3Applied);
    }

    private async Task<string> InstallAsync(string emulatorId, PlatformKind platform)
    {
        var platformName = platform.ToString();
        var folderName = emulatorId switch
        {
            "pcsx2" => "PCSX2", "ppsspp" => "PPSSPP", "rpcs3" => "RPCS3", "shadps4" => "shadPS4",
            _ => "DuckStation"
        };
        var executableName = (emulatorId, platform) switch
        {
            ("pcsx2", PlatformKind.Windows) => "pcsx2-qt.exe",
            ("pcsx2", PlatformKind.Linux) => "PCSX2.AppImage",
            ("duckstation", PlatformKind.Windows) => "duckstation-qt-x64-ReleaseLTCG.exe",
            ("ppsspp", PlatformKind.Windows) => "PPSSPPWindows64.exe",
            ("ppsspp", PlatformKind.Linux) => "PPSSPP.AppImage",
            ("rpcs3", PlatformKind.Windows) => "rpcs3.exe",
            ("rpcs3", PlatformKind.Linux) => "RPCS3.AppImage",
            ("shadps4", PlatformKind.Windows) => "shadPS4.exe",
            ("shadps4", PlatformKind.Linux) => "Shadps4-sdl.AppImage",
            _ => "DuckStation.AppImage"
        };
        var executable = Path.Combine(_root, "Emulators", platformName, folderName, executableName);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "fixture");
        await new EmulatorRegistryStore(_root).UpsertAsync(new()
        {
            EmulatorId = emulatorId, Platform = platform,
            ExecutableRelativePath = $"Emulators/{platformName}/{folderName}/{executableName}",
            InstalledVersion = "test", InstalledChannelId = "stable", InstalledAt = DateTimeOffset.UtcNow
        });
        return (emulatorId, platform) switch
        {
            ("pcsx2", PlatformKind.Windows) => Path.Combine(Path.GetDirectoryName(executable)!, "inis", "PCSX2.ini"),
            ("pcsx2", PlatformKind.Linux) => Path.Combine(executable + ".config", "PCSX2", "inis", "PCSX2.ini"),
            ("duckstation", PlatformKind.Windows) => Path.Combine(Path.GetDirectoryName(executable)!, "settings.ini"),
            ("ppsspp", PlatformKind.Windows) => Path.Combine(Path.GetDirectoryName(executable)!, "memstick", "PSP", "SYSTEM", "controls.ini"),
            ("ppsspp", PlatformKind.Linux) => Path.Combine(executable + ".config", "ppsspp", "PSP", "SYSTEM", "controls.ini"),
            ("rpcs3", PlatformKind.Windows) => Path.Combine(Path.GetDirectoryName(executable)!, "config", "input_configs", "global", "Default.yml"),
            ("rpcs3", PlatformKind.Linux) => Path.Combine(executable + ".config", "rpcs3", "input_configs", "global", "Default.yml"),
            ("shadps4", _) => Path.Combine(Path.GetDirectoryName(executable)!, "user", "config.json"),
            _ => Path.Combine(Path.GetDirectoryName(executable)!, "settings.ini")
        };
    }

    private static ControllerAutoConfigurationRequest Request(
        ControllerLayoutKind layout = ControllerLayoutKind.Xbox,
        ControllerFaceButtonPreference preference = ControllerFaceButtonPreference.PhysicalPosition,
        int playerNumber = 1)
    {
        var prefix = $"SDL-{playerNumber - 1}/";
        return new(new("test-device", "Test Controller", layout, true, false, DeviceOrdinal: playerNumber),
            playerNumber, preference,
        [
            new(CanonicalControllerInput.South, prefix + "A"), new(CanonicalControllerInput.East, prefix + "B"),
            new(CanonicalControllerInput.West, prefix + "X"), new(CanonicalControllerInput.North, prefix + "Y"),
            new(CanonicalControllerInput.DpadUp, prefix + "DPadUp"), new(CanonicalControllerInput.DpadDown, prefix + "DPadDown"),
            new(CanonicalControllerInput.DpadLeft, prefix + "DPadLeft"), new(CanonicalControllerInput.DpadRight, prefix + "DPadRight"),
            new(CanonicalControllerInput.LeftShoulder, prefix + "LeftShoulder"), new(CanonicalControllerInput.RightShoulder, prefix + "RightShoulder"),
            new(CanonicalControllerInput.LeftTrigger, prefix + "+LeftTrigger"), new(CanonicalControllerInput.RightTrigger, prefix + "+RightTrigger"),
            new(CanonicalControllerInput.LeftStickX, prefix + "LeftX"), new(CanonicalControllerInput.LeftStickY, prefix + "LeftY"),
            new(CanonicalControllerInput.RightStickX, prefix + "RightX"), new(CanonicalControllerInput.RightStickY, prefix + "RightY"),
            new(CanonicalControllerInput.LeftStickClick, prefix + "LeftStick"), new(CanonicalControllerInput.RightStickClick, prefix + "RightStick"),
            new(CanonicalControllerInput.Start, prefix + "Start"), new(CanonicalControllerInput.Back, prefix + "Back")
        ]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
