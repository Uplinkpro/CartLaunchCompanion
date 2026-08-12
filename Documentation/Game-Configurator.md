# Game Configurator

When opened on an existing cart, the Configurator discovers saved entries under `Cart/Games`, loads the first game automatically, and provides a game selector in the top toolbar. A blank configuration is shown only when the cart has no saved game entries or when **New** is selected.

## Portable file locators

Choose the configuration folder under `Cart/Games` before selecting launch files. The Windows and Linux launch pages provide locator buttons for native games, emulators, ROM or disc images, and optional companion applications.

Files selected from the media root's `Games`, `Emulators`, `Roms`, or `Cart` folders are converted into relative paths automatically. Drive letters and Linux mount locations are never written into `game.json`. Selecting a game also fills its working directory and process name; selecting an emulator switches the launcher to `Custom`; selecting a ROM appends a quoted path relative to the emulator's working directory.

Files outside the cart are reported as non-portable and are not saved by the locator.

Launcher verification is opt-in and checks only the launcher selected for that platform. The launcher itself may be installed anywhere on the host computer, but game files must remain on the same media as `Cart`. Native games are accepted from the cart's `Games` folder; emulators from `Emulators`; ROMs from `Roms`; and Steam/Xbox-managed content from `SteamLibrary`, `steamapps`, or `XboxGames` at the media root. A manually located host launcher folder is confirmed for the current setup session and is never written into `game.json`.

The Game Configurator is a separate desktop app for creating and editing Version 2 game folders without writing JSON by hand.

On first launch, the online metadata setup appears before the editor. It provides official registration links for a Steam Web API key and an optional SteamGridDB API key. The setup can be reopened later with **Settings**. API keys are stored in Windows Credential Manager or the Linux desktop keyring and are never kept in `game.json` or plain-text settings files.

## Start the configurator

```powershell
dotnet run --project Source/CartLaunchCompanion.Configurator
```

## Create a game folder

1. Enter the game name.
2. Choose the Windows and/or Linux launcher.
3. Enter the ID, executable, or launch address required by that launcher.
4. Optionally assign the game to a Custom Series Collection shelf.
5. Add any optional metadata, artwork, and behavior settings.
6. Open **Review & save**, then select **Validate and save game.json**.
7. Choose the folder for this game.

The configurator creates `game.json` plus standard `Artwork` and `Media` folders. It does not require the game executable to exist yet, which makes test configurations and configs prepared on another computer possible.

## Find a game on Steam

Enter a title or a numeric Steam App ID and select **Find on Steam**. The results are searched automatically when the window opens. Searching by title requires a Steam Web API key; an exact App ID can be looked up directly. Use **Settings** to open Steam's official registration page, sign in, create the key, and save it securely. The key is never added to a game folder or `game.json`.

Choose the exact game or edition from the results. The configurator then fills the Steam ID and available game information automatically.

Name searches combine Steam's current Store catalogue with SteamGridDB. Non-Steam, console, emulated, and delisted games can therefore be matched and saved with a SteamGridDB game ID even when no Steam App ID exists. Steam IDs remain optional and are used separately for Steam descriptions, screenshots, and trailers.

## Requirement labels

- **Required**: needed for a valid, launchable configuration.
- **Optional**: improves presentation or helps game detection, but may be left blank.
- **Advanced**: safe to keep at its default unless the game needs special handling.

Use **Open game.json** to edit an existing Version 2 configuration. Saving uses the same safe replacement method as Cart Launch Companion itself.

## Custom Series Collection placement

Open **Series collection** to arrange every saved game visually. Drag cover cards between shelves or onto another card to reorder them. Shelves can be added, renamed, reordered, or removed; removing one safely returns its games to **Unassigned**. **Save collection layout** updates `Config/collection.json` and every affected `game.json` as one transaction, restoring earlier files if any write fails.

This step writes the game's `collection.shelf` and `collection.order` values. The collection-wide name, logo, accent color, and shelf definitions remain in `Config/collection.json`; start from `Config/collection.example.json`. Shelves without games are hidden automatically.

The Series collection page also previews the collection-wide header logo. Use a transparent PNG at `1440 × 448` pixels; the launcher displays it at `360 × 112`. Keep important lettering and characters inside the centered `1320 × 360` safe area. The logo picker copies artwork into `Assets/Collections/<SeriesName>` and updates `Config/collection.json` without overwriting an existing logo file.

## Artwork sanity check

The Review & Save page checks every saved game’s cover, hero or 16:9 background, logo, and icon. A checkmark means the file exists and can be decoded as an image; an X identifies missing or unreadable artwork. The current game is checked from the fields presently shown in the editor, while other games are loaded from their saved `game.json` files. The audit also runs after saving.

The Artwork & Media page begins with a metadata-screen mockup showing the cover, background, logo, and icon together. Each local file-path field then has its own preview directly underneath. Local files take priority, with the corresponding configured URL used as a preview fallback. Missing assets are labeled individually, and **Refresh previews** reloads the page after manual path or URL changes.

Steam and SteamGridDB panoramic artwork is stored as a **Hero**, displayed across the top, and faded into true black without stretching. A user-supplied 16:9 background is a separate full-screen option and takes priority when present.

**Browse SteamGridDB artwork** provides ranked Cover, Hero, Logo, and Icon galleries. Results default to static artwork with adult, humor, and epilepsy-tagged entries excluded. Selecting an asset downloads and validates its full-resolution file and records the artist and asset attribution. **Update all API artwork** safely refreshes API-managed assets and screenshots while preserving custom 16:9 backgrounds and local trailers. Individual local artwork files can be removed with **Delete file** without clearing their reusable configured paths.

Direct download addresses are not fetched merely by saving text fields. Use **Download artwork and save** beneath those fields to download supplied cover, hero, 16:9 background, logo, icon, and direct-video URLs into the game folder. Images must decode successfully and are limited to 25 MB each; direct videos are limited to 1 GB. Successful downloads update the local paths, save `game.json`, refresh previews, and rerun the artwork sanity check. YouTube links remain streaming fallbacks and are not downloaded as files.

## Emulator launches

The Windows and Linux launch pages include command-line recipes for RetroArch, DuckStation, PCSX2, Dolphin, and RPCS3.

For an emulated game:

1. Set **Launcher** to **Custom**.
2. Set **Executable** to the emulator executable or AppImage.
3. Set **Working directory** to `.` when game-image paths are relative to the game folder.
4. Copy the matching fullscreen recipe into **Arguments** and replace the sample game path.
5. Set **Process name** to the emulator process so the launcher returns after emulation ends.

See the [Emulator launch guide](Emulator-Launch-Guide.md) for shared portable emulator folders, complete Windows and Linux examples, controller exit hotkeys, and troubleshooting.
