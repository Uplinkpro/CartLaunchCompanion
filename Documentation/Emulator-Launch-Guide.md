# Emulator launch guide

Cart Launch Companion can launch emulated games directly because its custom launcher accepts any executable and command-line arguments. Each game remains a normal library entry with its own artwork, metadata, trailer, screenshots, and controller navigation.

This guide covers RetroArch, DuckStation, PCSX2, Dolphin, and RPCS3. Emulator command lines can change between releases, so test one game before building a complete collection and check the emulator's built-in `--help` output after major updates.

> Use only firmware, BIOS files, keys, games, and disc images that you are legally permitted to use. Cart Launch Companion does not provide emulator software or copyrighted game content.

## Recommended portable layout

Keep shared emulator installations outside individual game folders:

```text
CartLaunchCompanion/
├── Emulators/
│   ├── RetroArch/
│   ├── DuckStation/
│   ├── PCSX2/
│   ├── Dolphin/
│   └── RPCS3/
└── Games/
    └── Example Emulated Game/
        ├── game.json
        ├── Artwork/
        ├── Media/
        └── Game/
            └── GameImage.iso
```

In the examples below:

- `executable` starts two folders above the game folder, then enters `Emulators`;
- `workingDirectory` is `.` so ROM paths are relative to the game's own folder;
- `arguments` contains the emulator flags followed by the game image;
- `processName` lets Cart Launch Companion wait for the emulator to close before restoring itself.

Use forward slashes in JSON paths. Always quote game or emulator paths that may contain spaces.

## RetroArch

RetroArch needs both a libretro core and the selected content. `-f` requests fullscreen and `-L` selects the core.

### Windows

```json
"windows": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../Emulators/RetroArch/retroarch.exe",
  "arguments": "-f -L \"../../Emulators/RetroArch/cores/snes9x_libretro.dll\" \"Game/Example Game.sfc\"",
  "workingDirectory": ".",
  "processName": "retroarch"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../Emulators/RetroArch/retroarch",
  "arguments": "-f -L \"../../Emulators/RetroArch/cores/snes9x_libretro.so\" \"Game/Example Game.sfc\"",
  "workingDirectory": ".",
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
  "executable": "../../Emulators/DuckStation/duckstation-qt-x64-ReleaseLTCG.exe",
  "arguments": "-batch -fullscreen -- \"Game/Example Game.cue\"",
  "workingDirectory": ".",
  "processName": "duckstation-qt-x64-ReleaseLTCG"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../Emulators/DuckStation/DuckStation-x64.AppImage",
  "arguments": "-batch -fullscreen -- \"Game/Example Game.cue\"",
  "workingDirectory": ".",
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
  "executable": "../../Emulators/PCSX2/pcsx2-qt.exe",
  "arguments": "-fullscreen -batch -- \"Game/Example Game.iso\"",
  "workingDirectory": ".",
  "processName": "pcsx2-qt"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../Emulators/PCSX2/pcsx2-qt.AppImage",
  "arguments": "-fullscreen -batch -- \"Game/Example Game.iso\"",
  "workingDirectory": ".",
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
  "executable": "../../Emulators/Dolphin/Dolphin.exe",
  "arguments": "-b -e \"Game/Example Game.rvz\"",
  "workingDirectory": ".",
  "processName": "Dolphin"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../Emulators/Dolphin/dolphin-emu",
  "arguments": "-b -e \"Game/Example Game.rvz\"",
  "workingDirectory": ".",
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
  "executable": "../../Emulators/RPCS3/rpcs3.exe",
  "arguments": "--no-gui --fullscreen \"Game/Example Game/PS3_GAME/USRDIR/EBOOT.BIN\"",
  "workingDirectory": ".",
  "processName": "rpcs3"
}
```

### Linux or SteamOS

```json
"linux": {
  "enabled": true,
  "launcher": "custom",
  "executable": "../../Emulators/RPCS3/rpcs3.AppImage",
  "arguments": "--no-gui --fullscreen \"Game/Example Game/PS3_GAME/USRDIR/EBOOT.BIN\"",
  "workingDirectory": ".",
  "processName": "rpcs3.AppImage"
}
```

Install the required firmware and configure the controller in RPCS3 before launching from the couch. The first boot of a game may remain visible while RPCS3 compiles modules and shaders.

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
