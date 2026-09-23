# Emulator Companion configuration workflow

Emulator Companion starts with a small set of shared presets. A preset describes the player's goal, such as compatibility, balanced play, or higher image quality. It does not contain PCSX2, RPCS3, PPSSPP, or other emulator setting names.

## Integration research gate

An emulator is not considered setup-capable merely because it can be downloaded. Before adding its setup action, verify against the emulator's current official documentation and source:

1. Native configuration filename, format, schema, and migration behavior.
2. Windows and Linux portable-data rules.
3. Game-library, BIOS or firmware, saves, DLC, screenshots, and controller paths.
4. Exact setting names and accepted values for every managed option.
5. Which files can be generated before first launch and which steps require the emulator UI.
6. Settings that are safe globally versus settings that should remain per-game.
7. Backup, restore, update, and cross-platform-copy behavior.

Every managed value must have a source-backed adapter and a fixture test. Unknown or unstable values remain unmanaged until researched. Emulator Companion preserves keys it does not own.

The current researched integrations are:

| Emulator | Native files | Generated without first launch | User prerequisite |
|---|---|---|---|
| PPSSPP | `ppsspp.ini`, `controls.ini` | Game path, first-run state, resolution, fullscreen, VSync, controller | None |
| DuckStation | `settings.ini` | Game list, portable BIOS search folder, renderer, resolution, display, controller | Legally dumped PlayStation BIOS copied to the prepared BIOS folder |
| PCSX2 | `PCSX2.ini`, controller INI | Game list, BIOS folder, saves, graphics, display, controller | Legally dumped PlayStation 2 BIOS imported by the user |
| RPCS3 | `config.yml`, `vfs.yml`, controller YAML | Renderer, resolution, fullscreen, VSync, controller, shared firmware and virtual hard drive | Install official `PS3UPDAT.PUP` once through **File → Install Firmware** |
| shadPS4 | `user/config.json` | Shared game updates, DLC, saves and user home; local modules, fullscreen, unified SDL input | User-dumped modules required by individual games |

Primary references: [PPSSPP configuration source](https://github.com/hrydgard/ppsspp/blob/master/Core/Config.cpp), [DuckStation settings source](https://github.com/stenzek/duckstation/blob/master/src/core/settings.cpp), [DuckStation BIOS settings](https://github.com/stenzek/duckstation/blob/master/src/duckstation-qt/biossettingswidget.cpp), [PCSX2 configuration source](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/Pcsx2Config.cpp), [RPCS3 system configuration](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Emu/system_config.h), and [shadPS4 settings schema](https://github.com/shadps4-emu/shadPS4/blob/main/src/core/emulator_settings.h).

## Portable game content layout

Consoles with researched update and DLC support use one user-facing source structure:

```text
Roms/
  PlayStation 4/
    Game Name/
      Updates/
      DLC/
```

The same structure currently applies to PlayStation 3. Emulator Companion creates `Updates` and `DLC` inside each existing game folder and uses this contract when it creates a new game folder. These are the user's organized source files. An emulator adapter must translate or import them into the emulator's native runtime layout and must not move, rename, or delete the source files.

Support is enabled per console only after its update and DLC behavior has been researched. For example, shadPS4 expects updates beside the base title and DLC under its configured add-on root, while RPCS3 installs packages under its virtual `dev_hdd0`. The portable library does not expose either emulator-specific layout.

The guided setup screen scans this structure and previews every pending import. RPCS3 accepts `.pkg`, `.rap`, and `.edat` files through its package installer on the current operating system. shadPS4 accepts extracted update folders named `CUSAxxxxx-patch` or `CUSAxxxxx-UPDATE` and extracted DLC folders associated with a recognizable `CUSAxxxxx` title ID. shadPS4 copies updates to `Emulators/Shared/shadPS4/games` and DLC to `Emulators/Shared/shadPS4/addcont/CUSAxxxxx`; its generated configuration includes both locations.

Imports never delete or rename source content. A per-platform receipt records each successful item. Unchanged content is not offered twice, changed source content is offered again, and interrupted batches resume from the first item that did not complete.

RPCS3 packages are installed by the build that can run on the current operating system. Both builds mount the same portable `Emulators/Shared/RPCS3/dev_hdd0` through RPCS3's native `vfs.yml`, so an imported update or DLC package is available from Windows and Linux and is recorded once for the cart.

## Player workflow

1. Install an emulator for Windows, Linux, or both.
2. Complete legal prerequisites such as a user-provided BIOS or firmware.
3. Choose one simple preset. Balanced is the default.
4. Emulator Companion translates that preset for each installed emulator and operating system.
5. Emulator Companion adds the cart's known game, BIOS, save, screenshot, and controller-profile folders where the emulator supports them.
6. Review and apply the changes. Existing files are backed up and unrelated settings are preserved.
7. From the main Emulator Companion screen, connect and test one controller directly through SDL. Steam Input is not used.
8. Emulator Companion saves one canonical library profile for the current operating system and translates it for every installed emulator adapter.
9. PCSX2 and DuckStation receive player-one SDL bindings in their portable configuration. PPSSPP and shadPS4 use the verified device through native automatic detection.
10. Launch a game. Advanced emulator settings and per-game overrides remain optional.

If both platforms are selected, the shared preset is applied to both installations. Each adapter may choose different native values when Windows and Linux capabilities differ.

## Shared emulator resources

The authoritative copies of reusable emulator data live under:

```text
Emulators/Shared/
  BIOS/<Emulator>/
  Saves/<Emulator>/
  States/<Emulator>/
  Screenshots/<Emulator>/
  Cheats/<Emulator>/
  TexturePacks/<Emulator>/
```

Adapters use relative native configuration paths whenever the emulator supports them. PCSX2 routes its BIOS, memory cards, save states, screenshots, and texture packs directly to these folders. DuckStation routes its BIOS, memory cards, save states, screenshots, cheats, and texture packs through its native `settings.ini` folder settings. Its portable data root is the directory beside the executable on both Windows and Linux because the managed installation contains DuckStation's `portable.txt` marker. Existing local resource folders are conflict checked, copied into shared storage, and retained under the migration backup area.

PPSSPP keeps separate configuration and controller files for each operating system while both builds use `Emulators/Shared/MemorySticks/PPSSPP` as their memory-stick root. CLC supplies PPSSPP's official `--memstick`, `--config`, and `--controlconfig` arguments whenever it launches a managed build. The paths are rebuilt from the cart's current location, so no drive letter is stored and no symbolic-link privilege is required. Before replacing existing local `PSP/SAVEDATA`, `PSP/PPSSPP_STATE`, or `PSP/TEXTURES` folders, Emulator Companion checks for conflicting files, copies missing files into the shared memory stick, and moves the original folder to `Config/EmulatorCompanion/Backups/SharedResources`.

RPCS3 keeps operating-system-specific configuration, controllers, caches, and compiled shaders. Its native `vfs.yml` mounts `dev_hdd0`, `dev_flash`, `dev_flash2`, and `dev_flash3` from `Emulators/Shared/RPCS3` using paths relative to `$(EmulatorDir)`. This makes saves, trophies, installed games, updates, DLC, and the user-installed firmware available to both builds without links or stored drive letters. Existing virtual-drive data is conflict checked and retained under the migration backup area before the shared mounts are enabled.

shadPS4 keeps each platform's `config.json`, input configuration, and caches local. Its `home_dir` points to `Emulators/Shared/shadPS4/home` for saves and current user data. Its game and DLC settings point to the shared managed folders above. Existing `user/home`, `user/games`, and `user/addcont` folders are conflict checked and backed up before the shared paths are enabled. Older shadPS4 builds also used `user/savedata`; that legacy location is retained for manual review rather than assigned to a newer user profile automatically.

When an emulator cannot redirect a resource through configuration, Emulator Companion may create a platform-specific relative NTFS symbolic link from that emulator's Windows or Linux directory to the authoritative shared folder. Relative links contain no drive letter or Linux mount point. Links are repairable configuration and never the only copy of user data. A link adapter must verify the host NTFS driver can create and follow the link before enabling it; otherwise it uses a researched configuration path or a guarded synchronization adapter.

When a future adapter requires relative links on Windows, creating those links may require Developer Mode or an elevated process. PPSSPP does not use links. Linux link support still depends on the active NTFS driver, so the SteamOS, Bazzite, ChimeraOS, and CachyOS validation pass must verify any link-based adapter before that workflow is marked validated.

## Configuration layers

Settings are resolved in this order:

1. **Simple preset** — emulator-neutral goals shared by the whole library.
2. **Emulator translation** — native values selected by that emulator's adapter.
3. **Platform capability filter** — removes or replaces settings unavailable on the selected operating system.
4. **Advanced emulator overrides** — optional changes for users who want granular control.
5. **Per-game overrides** — exceptions applied only to one title.

The simple preset catalog is the single source for the main choices shown in the app. Emulator adapters translate it; they do not define duplicate preset lists.

## Controller auto-configuration

Controller configuration is library-level and uses SDL devices directly. The shared controller model records the device identity, detected layout, standard SDL bindings, rumble support, player number, and whether face buttons should follow physical position or printed labels. Emulator setup screens do not ask the player to repeat the controller test.

The built-in family catalog recognizes all 8BitDo devices by vendor, PlayStation 3/4/5 and DualSense Edge controllers, first- and second-generation Steam Controllers, Xbox 360/One/Series/Elite/Adaptive controllers, Nintendo layouts, and third-party controllers reported through an Xbox protocol. SDL supplies the normalized mapping for recognized devices. A conventional unmapped controller with enough buttons, axes, and a D-pad receives an Xbox-style fallback; the profile cannot be saved unless the full guided test succeeds.

An emulator controller adapter converts those canonical inputs into the emulator's native format. Automatic mapping is offered only when SDL reports a standard mapping that the adapter supports. Generic or unusual controllers fall back to the emulator's manual setup rather than receiving guessed bindings.

A generated mapping is not considered ready until the player completes an input test. Player 1 and the optional Player 2 controller are verified and stored separately. This catches swapped Nintendo-style face buttons, missing axes, unusual third-party layouts, and accidental reuse of the same device before a game starts. Rumble bindings are added only when SDL reports rumble support.

## Adapter responsibilities

Each emulator adapter owns only these details:

- Translating shared preset goals into supported native settings.
- Translating verified canonical controller inputs into native bindings.
- Validating platform and emulator-version capabilities.
- Previewing the exact changes before writing them.
- Backing up and restoring files while preserving settings it does not own.

PCSX2 and DuckStation translate the profiles into their native `Pad1` and `Pad2` SDL bindings. RPCS3 writes separate `Player 1 Input` and `Player 2 Input` YAML blocks. PPSSPP translates Player 1 into its numeric control codes; Player 2 is retained for other emulators because PSP hardware has one local controller. shadPS4 uses its unified SDL input path to enumerate both verified devices.
