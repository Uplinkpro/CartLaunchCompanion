# Game Configurator

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
4. Add any optional metadata, artwork, and behavior settings.
5. Open **Review & save**, then select **Validate and save game.json**.
6. Choose the folder for this game.

The configurator creates `game.json` plus standard `Artwork` and `Media` folders. It does not require the game executable to exist yet, which makes test configurations and configs prepared on another computer possible.

## Find a game on Steam

Enter a title or a numeric Steam App ID and select **Find on Steam**. The results are searched automatically when the window opens. Searching by title requires a Steam Web API key; an exact App ID can be looked up directly. Use **Settings** to open Steam's official registration page, sign in, create the key, and save it securely. The key is never added to a game folder or `game.json`.

Choose the exact game or edition from the results. The configurator then fills the Steam ID and available game information automatically.

Name searches combine Steam's current Store catalogue with SteamGridDB. Delisted historical games can therefore appear as legacy matches. When Steam no longer publishes the legacy App ID, select the match and enter its known numeric Steam App ID before continuing.

## Requirement labels

- **Required**: needed for a valid, launchable configuration.
- **Optional**: improves presentation or helps game detection, but may be left blank.
- **Advanced**: safe to keep at its default unless the game needs special handling.

Use **Open game.json** to edit an existing Version 2 configuration. Saving uses the same safe replacement method as Cart Launch Companion itself.
