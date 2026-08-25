using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorLaunchPresetCatalogTests
{
    [Theory]
    [InlineData("../../../Emulators/RetroArch/retroarch.exe", KnownEmulator.RetroArch, "-f")]
    [InlineData("../../../Emulators/DuckStation/duckstation-qt-x64-ReleaseLTCG.exe", KnownEmulator.DuckStation, "-batch -fullscreen --")]
    [InlineData("../../../Emulators/PCSX2/pcsx2-qt.exe", KnownEmulator.Pcsx2, "-fullscreen -batch --")]
    [InlineData("../../../Emulators/Dolphin/Dolphin.exe", KnownEmulator.Dolphin, "-b -e")]
    [InlineData("../../../Emulators/RPCS3/rpcs3.exe", KnownEmulator.Rpcs3, "--no-gui --fullscreen")]
    [InlineData("../../../Emulators/PPSSPP/PPSSPPWindows64.exe", KnownEmulator.Ppsspp, "--fullscreen --pause-menu-exit")]
    [InlineData("../../../Emulators/Windows/Cemu/Cemu.exe", KnownEmulator.Cemu, "-f -g")]
    [InlineData("../../../Emulators/Linux/Azahar/Azahar.AppImage", KnownEmulator.Azahar, "-f")]
    [InlineData("../../../Emulators/Windows/melonDS/melonDS.exe", KnownEmulator.MelonDs, "")]
    [InlineData("../../../Emulators/Linux/mGBA/mGBA.AppImage", KnownEmulator.Mgba, "-f")]
    [InlineData("../../../Emulators/Windows/RMG/RMG.exe", KnownEmulator.RosalieMupenGui, "--fullscreen")]
    [InlineData("../../../Emulators/Linux/Vita3K/Vita3K.AppImage", KnownEmulator.Vita3K, "")]
    [InlineData("../../../Emulators/Windows/xemu/xemu.exe", KnownEmulator.Xemu, "-full-screen")]
    [InlineData("../../../Emulators/Linux/Flycast/flycast.AppImage", KnownEmulator.Flycast, "")]
    [InlineData("../../../Emulators/Windows/MAME/mame.exe", KnownEmulator.Mame, "-skip_gameinfo -nowindow")]
    [InlineData("../../../Emulators/Linux/DOSBox Staging/dosbox-staging.AppImage", KnownEmulator.DosBoxStaging, "-fullscreen")]
    public void DetectsKnownEmulatorAndAppliesDefault(string path, KnownEmulator expected, string arguments)
    {
        Assert.Equal(expected, EmulatorLaunchPresetCatalog.Detect(path));
        Assert.Equal(arguments, EmulatorLaunchPresetCatalog.ApplyDefault(path, ""));
    }

    [Fact]
    public void DoesNotOverwriteCustomArguments()
    {
        Assert.Equal("--my-custom-option", EmulatorLaunchPresetCatalog.ApplyDefault("PPSSPPWindows64.exe", "--my-custom-option"));
    }

    [Fact]
    public void AddsWindowsMgbaCoreAndGbaRomForRetroArch()
    {
        var arguments = EmulatorLaunchPresetCatalog.AddRom(
            "retroarch.exe", "-f", "../../Roms/Game Boy Advance/Game.gba", PlatformKind.Windows,
            "cores/mgba_libretro.dll");

        Assert.Equal("-f -L \"cores/mgba_libretro.dll\" \"../../Roms/Game Boy Advance/Game.gba\"", arguments);
    }

    [Fact]
    public void FiltersInstalledRetroArchCoresByRomExtension()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var mgba = Path.Combine(folder.FullName, "mgba_libretro.dll");
            var snes = Path.Combine(folder.FullName, "snes9x_libretro.dll");
            File.WriteAllText(mgba, "core");
            File.WriteAllText(snes, "core");

            var matches = EmulatorLaunchPresetCatalog.FindCompatibleRetroArchCores([mgba, snes], "Game.gba");

            Assert.Equal([mgba], matches);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void ReturnsAllInstalledCoresForAmbiguousContainer()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var first = Path.Combine(folder.FullName, "beetle_psx_hw_libretro.dll");
            var second = Path.Combine(folder.FullName, "ppsspp_libretro.dll");
            File.WriteAllText(first, "core");
            File.WriteAllText(second, "core");

            var matches = EmulatorLaunchPresetCatalog.FindCompatibleRetroArchCores([first, second], "Game.iso");

            Assert.Equal(2, matches.Count);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void RecommendsGbaCoresForDownload()
    {
        Assert.Equal(
            ["mgba", "vbam", "vba_next", "gpsp"],
            EmulatorLaunchPresetCatalog.GetRecommendedRetroArchCoreNames("Game.gba"));
    }

    [Fact]
    public void AddsRomToPpssppPreset()
    {
        var arguments = EmulatorLaunchPresetCatalog.AddRom(
            "PPSSPPSDL", "", "../../Roms/PSP/Game.iso", PlatformKind.Linux);

        Assert.Equal("--fullscreen --pause-menu-exit \"../../Roms/PSP/Game.iso\"", arguments);
    }

    [Fact]
    public void ReplacesPreviousRomWhenKnownEmulatorRecipeIsRegenerated()
    {
        var arguments = EmulatorLaunchPresetCatalog.AddRom(
            "PPSSPPWindows64.exe",
            "--fullscreen --pause-menu-exit \"../../Roms/PSP/Old.iso\"",
            "../../Roms/PSP/New.iso",
            PlatformKind.Windows);

        Assert.Equal(
            "--fullscreen --pause-menu-exit \"../../Roms/PSP/New.iso\"",
            arguments);
    }

    [Fact]
    public void ReplacesPreviousRetroArchRomAndCore()
    {
        var arguments = EmulatorLaunchPresetCatalog.AddRom(
            "retroarch.exe",
            "-f -L \"cores/old_libretro.dll\" \"../../Roms/Old.gba\"",
            "../../Roms/New.gba",
            PlatformKind.Windows,
            "cores/mgba_libretro.dll");

        Assert.Equal(
            "-f -L \"cores/mgba_libretro.dll\" \"../../Roms/New.gba\"",
            arguments);
    }
}
