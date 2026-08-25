using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Launching;

public enum KnownEmulator
{
    Unknown, RetroArch, DuckStation, Pcsx2, Dolphin, Rpcs3, Ppsspp, Cemu, Azahar,
    MelonDs, Mgba, Mesen, Snes9x, RosalieMupenGui, Vita3K, ShadPs4, Xemu, Xenia,
    Flycast, Mame, DosBoxStaging, ScummVm
}

public sealed record EmulatorDefinition(KnownEmulator Id, string DisplayName, string FolderName,
    string[] ExecutableTokens, string DefaultArguments, string[] RomFolders);

public static class EmulatorLaunchPresetCatalog
{
    public static IReadOnlyList<EmulatorDefinition> All { get; } =
    [
        new(KnownEmulator.RetroArch, "RetroArch", "RetroArch", ["retroarch"], "-f", ["Atari", "Arcade", "Game Boy", "Game Boy Color", "Game Boy Advance", "NES", "SNES", "Nintendo 64", "Nintendo DS", "Sega Genesis", "Sega Master System", "Sega Game Gear", "PC Engine", "Neo Geo Pocket", "WonderSwan"]),
        new(KnownEmulator.DuckStation, "DuckStation", "DuckStation", ["duckstation"], "-batch -fullscreen --", ["PlayStation"]),
        new(KnownEmulator.Pcsx2, "PCSX2", "PCSX2", ["pcsx2"], "-fullscreen -batch --", ["PlayStation 2"]),
        new(KnownEmulator.Dolphin, "Dolphin", "Dolphin", ["dolphin"], "-b -e", ["GameCube", "Wii"]),
        new(KnownEmulator.Rpcs3, "RPCS3", "RPCS3", ["rpcs3"], "--no-gui --fullscreen", ["PlayStation 3"]),
        new(KnownEmulator.Ppsspp, "PPSSPP", "PPSSPP", ["ppsspp"], "--fullscreen --pause-menu-exit", ["PSP"]),
        new(KnownEmulator.Cemu, "Cemu", "Cemu", ["cemu"], "-f -g", ["Wii U"]),
        new(KnownEmulator.Azahar, "Azahar", "Azahar", ["azahar"], "-f", ["Nintendo 3DS"]),
        new(KnownEmulator.MelonDs, "melonDS", "melonDS", ["melonds"], "", ["Nintendo DS"]),
        new(KnownEmulator.Mgba, "mGBA", "mGBA", ["mgba"], "-f", ["Game Boy", "Game Boy Color", "Game Boy Advance"]),
        new(KnownEmulator.Mesen, "Mesen", "Mesen", ["mesen"], "", ["NES", "SNES", "Game Boy", "Game Boy Color"]),
        new(KnownEmulator.Snes9x, "Snes9x", "Snes9x", ["snes9x"], "-fullscreen", ["SNES"]),
        new(KnownEmulator.RosalieMupenGui, "Rosalie's Mupen GUI", "RMG", ["rmg", "rosalie"], "--fullscreen", ["Nintendo 64"]),
        new(KnownEmulator.Vita3K, "Vita3K", "Vita3K", ["vita3k"], "", ["PlayStation Vita"]),
        new(KnownEmulator.ShadPs4, "shadPS4", "shadPS4", ["shadps4"], "-f", ["PlayStation 4"]),
        new(KnownEmulator.Xemu, "xemu", "xemu", ["xemu"], "-full-screen", ["Xbox"]),
        new(KnownEmulator.Xenia, "Xenia", "Xenia", ["xenia"], "--fullscreen=true", ["Xbox 360"]),
        new(KnownEmulator.Flycast, "Flycast", "Flycast", ["flycast"], "", ["Dreamcast", "Naomi"]),
        new(KnownEmulator.Mame, "MAME", "MAME", ["mame"], "-skip_gameinfo -nowindow", ["Arcade"]),
        new(KnownEmulator.DosBoxStaging, "DOSBox Staging", "DOSBox Staging", ["dosbox"], "-fullscreen", ["DOS"]),
        new(KnownEmulator.ScummVm, "ScummVM", "ScummVM", ["scummvm"], "-f", ["ScummVM"])
    ];

    private static readonly Dictionary<string, string[]> RetroArchCoreCompatibility = new(StringComparer.OrdinalIgnoreCase)
    {
        [".gba"] = ["mgba", "vbam", "vba_next", "gpsp"], [".gb"] = ["gambatte", "sameboy", "gearboy", "tgbdual", "mgba"],
        [".gbc"] = ["gambatte", "sameboy", "gearboy", "tgbdual", "mgba"], [".nes"] = ["mesen", "nestopia", "fceumm", "quicknes"],
        [".fds"] = ["mesen", "nestopia", "fceumm"], [".sfc"] = ["snes9x", "bsnes", "mesen_s"], [".smc"] = ["snes9x", "bsnes", "mesen_s"],
        [".md"] = ["genesis_plus_gx", "picodrive", "blastem"], [".gen"] = ["genesis_plus_gx", "picodrive", "blastem"],
        [".smd"] = ["genesis_plus_gx", "picodrive", "blastem"], [".gg"] = ["genesis_plus_gx", "gearsystem", "picodrive"],
        [".sms"] = ["genesis_plus_gx", "gearsystem", "picodrive"], [".nds"] = ["melonds", "desmume"],
        [".n64"] = ["mupen64plus_next", "parallel_n64"], [".z64"] = ["mupen64plus_next", "parallel_n64"],
        [".v64"] = ["mupen64plus_next", "parallel_n64"], [".a26"] = ["stella"], [".lnx"] = ["handy"],
        [".pce"] = ["mednafen_pce_fast", "mednafen_pce"], [".vb"] = ["mednafen_vb"], [".ws"] = ["mednafen_wswan"],
        [".wsc"] = ["mednafen_wswan"], [".ngp"] = ["mednafen_ngp"], [".ngc"] = ["mednafen_ngp"]
    };
    private static readonly HashSet<string> AmbiguousRetroArchExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".7z", ".bin", ".cue", ".iso", ".chd", ".pbp", ".m3u" };

    public static KnownEmulator Detect(string executable)
    {
        var name = Normalize(Path.GetFileNameWithoutExtension(executable));
        return All.FirstOrDefault(definition => definition.ExecutableTokens.Any(token =>
            name.Contains(Normalize(token), StringComparison.OrdinalIgnoreCase)))?.Id ?? KnownEmulator.Unknown;
    }

    public static EmulatorDefinition? Find(KnownEmulator emulator) => All.FirstOrDefault(item => item.Id == emulator);
    public static string DisplayName(KnownEmulator emulator) => Find(emulator)?.DisplayName ?? "Unknown emulator";
    public static string ApplyDefault(string executable, string currentArguments) =>
        !string.IsNullOrWhiteSpace(currentArguments) ? currentArguments : Find(Detect(executable))?.DefaultArguments ?? currentArguments;

    public static string AddRom(string executable, string currentArguments, string romPath, PlatformKind platform, string? retroArchCorePath = null)
    {
        var emulator = Detect(executable);
        var arguments = emulator == KnownEmulator.Unknown ? currentArguments.Trim() : ApplyDefault(executable, "").Trim();
        if (emulator == KnownEmulator.RetroArch && !string.IsNullOrWhiteSpace(retroArchCorePath))
        {
            arguments = Append(arguments, "-L");
            arguments = Append(arguments, Quote(retroArchCorePath));
        }
        return Append(arguments, Quote(romPath));
    }

    public static IReadOnlyList<string> FindCompatibleRetroArchCores(IEnumerable<string> installedCorePaths, string romPath)
    {
        var installed = installedCorePaths.Where(File.Exists).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        var extension = Path.GetExtension(romPath);
        if (AmbiguousRetroArchExtensions.Contains(extension) || !RetroArchCoreCompatibility.TryGetValue(extension, out var compatible)) return installed;
        return installed.Where(path =>
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_libretro", StringComparison.OrdinalIgnoreCase)) name = name[..^"_libretro".Length];
            return compatible.Any(candidate => name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        }).ToArray();
    }

    public static IReadOnlyList<string> GetRecommendedRetroArchCoreNames(string romPath) =>
        RetroArchCoreCompatibility.TryGetValue(Path.GetExtension(romPath), out var compatible) ? compatible : [];
    public static bool IsAmbiguousRetroArchExtension(string romPath) =>
        AmbiguousRetroArchExtensions.Contains(Path.GetExtension(romPath)) || !RetroArchCoreCompatibility.ContainsKey(Path.GetExtension(romPath));

    private static string Normalize(string value) => value.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static string Append(string existing, string value) => string.IsNullOrWhiteSpace(existing) ? value : existing.TrimEnd() + " " + value;
}
