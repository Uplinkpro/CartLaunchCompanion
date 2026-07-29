# Cart Launch Companion 2.0 Folder Structure

## Development repository

```text
CartLaunchCompanion/
├── Source/
│   ├── CartLaunchCompanion.Core/
│   ├── CartLaunchCompanion.Desktop/
│   ├── CartLaunchCompanion.Platform.Windows/
│   └── CartLaunchCompanion.Platform.Linux/
├── Tests/
├── Assets/
├── Games/
│   └── Examples/
├── Schemas/
├── docs/
│   └── 2.0/
├── Build/
└── CartLaunchCompanion.Avalonia.sln
```

## Finished portable distribution

```text
CartLaunchCompanion/
├── Start Cart Launch Companion.bat
├── Start Cart Launch Companion.sh
├── System/
│   ├── Windows-x64/
│   └── Linux-x64/
├── Games/
├── Assets/
├── Config/
├── Schemas/
├── Logs/
└── Cache/
```

## Ownership rules

### System

Runtime-specific application files only.

Users should not edit this folder.

### Games

User-managed game folders and configuration.

This folder is shared by Windows and Linux builds.

### Assets

Application-owned shared visual assets:

- Cart Launch Companion branding
- Launcher glyphs
- Scene layers
- Controller glyphs
- Default placeholders

Game-specific art does not belong here.

### Config

Application-wide user settings.

### Schemas

JSON schemas used for validation and editor autocomplete.

### Logs

Rotating runtime logs.

Logs must remain small and safe to delete.

### Cache

Disposable generated data.

The application must recreate this folder when missing.

## Game folder

```text
Games/
└── Forza Horizon 4/
    ├── game.json
    ├── Artwork/
    │   ├── Cover.jpg
    │   ├── Background.jpg
    │   ├── Logo.png
    │   └── Icon.png
    ├── Media/
    │   └── Trailer.mp4
    └── Cache/
```

## Game folder rules

- `game.json` is the only required file.
- `Artwork` stores presentation images.
- `Media` stores trailers or gameplay previews.
- `Cache` is disposable and may be deleted at any time.
- User-supplied files must never be overwritten without explicit configuration or a safe backup.
- Generated files must use predictable names.
- Deep nesting should be avoided.
- Personal game libraries must remain ignored by Git.
- Only `Games/Examples` belongs in source control.

## Path rules

- Paths in `game.json` are relative to the game folder unless explicitly documented otherwise.
- Platform-specific absolute paths are permitted but reduce portability.
- Shared relative paths should use forward slashes in JSON.
- The application normalizes separators for the current operating system.
