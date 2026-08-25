# Emulator launch guide

Cart Launch Companion can launch emulated games directly because its custom launcher accepts any executable and command-line arguments. Each game remains a normal library entry with its own artwork, metadata, trailer, screenshots, and controller navigation.

CLC recognizes a curated catalog of major console, arcade, and classic-PC emulators. Emulator command lines can change between releases, so test one game before building a complete collection and check the emulator's built-in `--help` output after major updates.

> Use only firmware, BIOS files, keys, games, and disc images that you are legally permitted to use. Cart Launch Companion does not provide emulator software or copyrighted game content.

## Recommended portable layout

Keep shared emulator installations outside individual game folders:

```text
GameCart/
├── CartLaunchCompanion/
│   └── Games/
│       └── Example Emulated Game/
│           ├── game.json
│           ├── Artwork/
│           └── Media/
├── Emulators/
│   ├── Windows/
│   │   ├── RetroArch/
│   │   ├── DuckStation/
│   │   └── ...
│   ├── Linux/
│   │   ├── RetroArch/
│   │   ├── DuckStation/
│   │   └── ...
│   └── Shared/
│       ├── BIOS/
│       ├── Saves/
│       ├── States/
│       ├── Screenshots/
│       └── Cheats/
└── Roms/
    └── System/
        └── GameImage.iso
```

In the examples below:

- `executable` starts three folders above the CLC game configuration, then enters the drive-root `Emulators` folder;
- the configurator normally sets `workingDirectory` to the selected emulator folder;
- `arguments` contains the emulator flags followed by the game image;
- `processName` lets Cart Launch Companion wait for the emulator to close before restoring itself.

Use forward slashes in JSON paths. Always quote game or emulator paths that may contain spaces.

## Automatic emulator recipes

When **Locate emulator** selects a recognized executable, Game Configurator fills a conservative fullscreen or batch recipe if the Arguments field is still empty. Existing custom arguments are always preserved.

The generated portable structure supports:

- Multi-system: RetroArch;
- Nintendo: Dolphin, Cemu, Azahar, melonDS, mGBA, Mesen, Snes9x, and Rosalie's Mupen GUI;
- Sony: DuckStation, PCSX2, RPCS3, PPSSPP, Vita3K, and shadPS4;
- Microsoft: xemu and Xenia;
- Sega and arcade: Flycast and MAME;
- Classic PC: DOSBox Staging and ScummVM.

Put Windows portable builds and Linux x86_64 AppImages in their matching generated folders. Configure each pair to use the real folders under `Emulators/Shared`; CLC intentionally does not create cross-platform symlinks.

## Emulator Library in Game Configurator

Open **Emulator Library** from the Configurator header to see every supported emulator. Each row reports its Windows and Linux/SteamOS status separately. **Add Windows** and **Add Linux** accept an executable only from that emulator's assigned portable folder; CLC deliberately does not copy a lone executable because most emulators depend on other files beside it.

After a complete portable build has been placed in the indicated folder, select its executable and rescan. Installed emulators then appear under **Windows launch → Use a portable emulator**. Applying one fills its executable, working directory, process name, launcher type, and safe default arguments. When both operating-system builds are present, Windows and Linux are enabled and configured together.

For RetroArch, **Locate ROM** scans the selected portable installation's `cores` folder. A file extension with one compatible installed core is selected automatically. When several installed cores support the extension, the Configurator asks which one to use. Ambiguous containers such as ISO, BIN, CUE, CHD, ZIP, and 7Z always require a core choice. The resulting portable `-L` core path and ROM path are added together.

## RetroArch

### Missing cores

When **Locate ROM** recognizes RetroArch but cannot find a compatible installed core, the Configurator offers **Download and use core**. It shows the official Libretro buildbot URL and the exact portable `RetroArch/cores` destination before making any change. The selected core is downloaded directly into the RetroArch copy on the cart and added to the generated launch arguments automatically.

Automatic downloads currently support Windows x64 and Linux x64. Ambiguous disc/container formats such as `.iso`, `.chd`, `.cue`, and `.zip` still require the user to install or select the correct system core because the extension alone cannot identify the emulated console safely.

## Multiple platform versions of one game

Keep every release in its own `CartLaunchCompanion/Games/<Version Name>` configuration folder. This preserves separate cover art, metadata, ROM or executable, emulator, core, and launch behavior.

In **Game details → Platform versions**, configure:

- **Version group:** the exact same identifier for every release, such as `gta-vice-city-stories`.
- **Platform label:** the name shown in the version picker, such as `PC`, `PSP`, or `PlayStation 2`.
- **Primary shelf version:** enable this for one release whose cover should represent the group on the collection shelf.

CLC collapses matching releases to one shelf card. Opening that card displays the individual cover arts and platform labels before the metadata page. Back from metadata returns to the version picker, and Back again returns to the collection shelf. Configurations without a version group retain the original one-card behavior.

RetroArch needs both a libretro core and the selected content. `-f` requests fullscreen and `-L` selects the core.

### Windows

```json
"windows": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/RetroArch/retroarch.exe",
  "arguments": "-f -L \"cores/snes9x_libretro.dll\" \"../../Roms/Super Nintendo/Example Game.sfc\"",
  "workingDirectory": "../../../Emulators/RetroArch",
  "processName": "retroarch"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/RetroArch/retroarch",
  "arguments": "-f -L \"cores/snes9x_libretro.so\" \"../../Roms/Super Nintendo/Example Game.sfc\"",
  "workingDirectory": "../../../Emulators/RetroArch",
  "processName": "retroarch"
}
```

Replace the Snes9x core and `.sfc` file with the correct core and content for the system. RetroArch also supports Flatpak, but a native portable build provides simpler paths for a self-contained cart.

## DuckStation — PlayStation

DuckStation accepts a disc image after `--`. `-batch` closes the interface when emulation stops, while `-fullscreen` enters fullscreen immediately.

### Windows

```json
"windows": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/DuckStation/duckstation-qt-x64-ReleaseLTCG.exe",
  "arguments": "-batch -fullscreen -- \"../../Roms/PlayStation/Example Game.cue\"",
  "workingDirectory": "../../../Emulators/DuckStation",
  "processName": "duckstation-qt-x64-ReleaseLTCG"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/DuckStation/DuckStation-x64.AppImage",
  "arguments": "-batch -fullscreen -- \"../../Roms/PlayStation/Example Game.cue\"",
  "workingDirectory": "../../../Emulators/DuckStation",
  "processName": "DuckStation-x64.AppImage"
}
```

For multi-track games, point DuckStation at the `.cue` file rather than an individual `.bin` track.

## PCSX2 — PlayStation 2

PCSX2 uses `-fullscreen` for immediate fullscreen and `-batch` to close the emulator interface when the game stops. `--` marks everything after it as the boot filename.

### Windows

```json
"windows": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/PCSX2/pcsx2-qt.exe",
  "arguments": "-fullscreen -batch -- \"../../Roms/PlayStation 2/Example Game.iso\"",
  "workingDirectory": "../../../Emulators/PCSX2",
  "processName": "pcsx2-qt"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/PCSX2/pcsx2-qt.AppImage",
  "arguments": "-fullscreen -batch -- \"../../Roms/PlayStation 2/Example Game.iso\"",
  "workingDirectory": "../../../Emulators/PCSX2",
  "processName": "pcsx2-qt.AppImage"
}
```

PCSX2 also offers `-nogui` and `-bigpicture`. `-batch` is normally the best fit when Cart Launch Companion already provides the front end.

## Dolphin — GameCube and Wii

Dolphin uses `-b` for batch mode and `-e` to boot a specific game. Enable **Start in Fullscreen** once in Dolphin's graphics settings; Dolphin then applies it to games started from Cart Launch Companion.

### Windows

```json
"windows": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/Dolphin/Dolphin.exe",
  "arguments": "-b -e \"../../Roms/GameCube/Example Game.rvz\"",
  "workingDirectory": "../../../Emulators/Dolphin",
  "processName": "Dolphin"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/Dolphin/dolphin-emu",
  "arguments": "-b -e \"../../Roms/GameCube/Example Game.rvz\"",
  "workingDirectory": "../../../Emulators/Dolphin",
  "processName": "dolphin-emu"
}
```

Configure controller profiles and an Exit Emulation hotkey in Dolphin before couch use. Dolphin's hotkey settings provide a controller-friendly way to leave fullscreen and stop the game.

## RPCS3 — PlayStation 3

RPCS3 can boot a game's `EBOOT.BIN` directly. `--no-gui` suppresses the main game list and `--fullscreen` starts the game fullscreen.

### Windows

```json
"windows": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/RPCS3/rpcs3.exe",
  "arguments": "--no-gui --fullscreen \"../../Roms/PlayStation 3/Example Game/PS3_GAME/USRDIR/EBOOT.BIN\"",
  "workingDirectory": "../../../Emulators/RPCS3",
  "processName": "rpcs3"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/RPCS3/rpcs3.AppImage",
  "arguments": "--no-gui --fullscreen \"../../Roms/PlayStation 3/Example Game/PS3_GAME/USRDIR/EBOOT.BIN\"",
  "workingDirectory": "../../../Emulators/RPCS3",
  "processName": "rpcs3.AppImage"
}
```

Install the required firmware and configure the controller in RPCS3 before launching from the couch. The first boot of a game may remain visible while RPCS3 compiles modules and shaders.

## PPSSPP — PlayStation Portable

PPSSPP accepts an ISO, CSO, or other supported PSP game path directly. `--fullscreen` forces fullscreen mode and `--pause-menu-exit` changes the pause-menu exit action so it closes PPSSPP and returns cleanly to CLC. PPSSPP does not require a PSP BIOS.

### Windows

```json
"windows": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/PPSSPP/PPSSPPWindows64.exe",
  "arguments": "--fullscreen --pause-menu-exit \"../../Roms/PlayStation Portable/Example Game.iso\"",
  "workingDirectory": "../../../Emulators/PPSSPP",
  "processName": "PPSSPPWindows64"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../../Emulators/PPSSPP/PPSSPPSDL",
  "arguments": "--fullscreen --pause-menu-exit \"../../Roms/PlayStation Portable/Example Game.iso\"",
  "workingDirectory": "../../../Emulators/PPSSPP",
  "processName": "PPSSPPSDL"
}
```

The Configurator recognizes common PPSSPP executable names and fills the two launch switches automatically. Use **Locate ROM** afterward to append the portable game path.

## Behavior settings

These settings work well for emulators:

```json
"behavior": {
  "restoreLauncherAfterExit": true,
  "hideWhileGameRuns": true,
  "processStartTimeoutSeconds": 180,
  "processExitPollSeconds": 2
}
```

If the launcher returns too early, confirm that `processName` matches the emulator process shown by the operating system. AppImage process names can vary by build; leave `processName` blank only when normal executable monitoring already behaves correctly.

## Controller and exit setup

Before adding several games, test this complete couch workflow with one title:

1. Launch the game from Cart Launch Companion.
2. Confirm that the emulator enters fullscreen without showing its library window.
3. Confirm that the expected controller profile loads.
4. Use the emulator's mapped hotkey to stop emulation cleanly.
5. Confirm that the emulator process closes and Cart Launch Companion returns.

Prefer the emulator's own clean-exit hotkey over forcibly terminating its process. A clean exit gives the emulator time to save memory cards, configuration, achievements, and shader data.

## Troubleshooting

### The emulator opens but the game does not

- Check that the ROM path is relative to the game folder because `workingDirectory` is `.`.
- Keep quotes around paths containing spaces.
- Confirm the file extension is supported by that emulator.
- Run the emulator's `--help` command and compare the installed version's syntax.

### Cart Launch Companion returns immediately

- Set `processName` to the emulator's actual process name without `.exe` on Windows.
- Confirm the emulator does not hand the game to a differently named child process.
- Increase `processStartTimeoutSeconds` for emulators that compile content on first boot.

### The game starts windowed

- Confirm the fullscreen argument is present for RetroArch, DuckStation, PCSX2, or RPCS3.
- Enable **Start in Fullscreen** inside Dolphin.
- On Linux or SteamOS, Gamescope can provide an additional fullscreen container when an emulator's own fullscreen behavior is inconsistent.

### A Linux executable does not start

- Make the native binary or AppImage executable.
- Confirm required graphics drivers and runtime libraries are installed.
- Use the exact case-sensitive filename in `game.json`.

## Official command-line references

- [RetroArch CLI](https://docs.libretro.com/guides/cli-intro/)
- [DuckStation command-line arguments](https://github.com/stenzek/duckstation/wiki/Command-Line-Arguments)
- [PCSX2 command-line options](https://pcsx2.net/docs/advanced/cli/)
- [Dolphin command-line usage](https://github.com/dolphin-emu/dolphin#command-line-usage)
- [RPCS3 project](https://github.com/RPCS3/rpcs3)
