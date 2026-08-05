# Cart Launch Companion 2.0

A portable, fullscreen, controller-first game launcher for Windows, Linux, and
SteamOS.

Version 2 is now at `2.0.0-rc.1` on the `avalonia-migration` branch. The stable
Version 1 source remains on `main` until Version 2 completes release-candidate
testing.

## What it does

- Presents a curated game library designed for televisions and handhelds.
- Uses launcher-aware branding while keeping one consistent interface.
- Opens a metadata page with artwork, screenshots, descriptions, trailers,
  Steam Deck status, and gamepad support.
- Launches Steam titles, local executables, launcher URIs, Heroic, Flatpak,
  Wine, and Proton targets.
- Navigates with SDL3 controllers, keyboard, mouse, and common media remotes.
- Keeps configurations, artwork, logs, and cache in a portable folder.

## RC1 packages

The RC1 packaging process produces:

- `CartLaunchCompanion-2.0.0-rc.1-win-x64.zip`
- `CartLaunchCompanion-2.0.0-rc.1-linux-x64.tar.gz`
- `CartLaunchCompanion-2.0.0-rc.1-portable.zip`
- `SHA256SUMS.txt`

See [RC1 notes](Docs/2.0/ReleaseCandidate1.md) for completed and outstanding
validation. Existing users should read the [Version 1 upgrade guide](Docs/2.0/UpgradeGuide.md).

## Portable layout

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

`Games`, `Config`, `Logs`, and `Cache` must be writable. `Cache` is disposable;
logs rotate automatically.

## Game configuration

Each folder under `Games` contains a `game.json`. The complete schema lives at
`Schemas/game.schema.json`, and working samples live in `Games/Examples`.

```json
{
  "$schema": "../../../Schemas/game.schema.json",
  "formatVersion": 2,
  "game": {
    "name": "Example Game",
    "steamDeckCompatibility": "unknown",
    "gamepadSupport": "full"
  },
  "artwork": {
    "steamMetadataId": "123456",
    "downloadMissingArtwork": true
  },
  "launch": {
    "preferredPlatform": "automatic",
    "windows": {
      "enabled": true,
      "launcher": "steam",
      "steamId": "123456"
    },
    "linux": {
      "enabled": true,
      "launcher": "steam",
      "steamId": "123456"
    }
  }
}
```

Use the full example because the schema intentionally requires the complete
grouped structure. Version 1 configurations can be loaded through the
compatibility importer without rewriting the original file.

## Metadata

Steam is the primary metadata and screenshot source. SteamGridDB can fill
missing artwork when the user creates `Config/metadata.json` from the included
example and supplies their own API key. Local artwork remains the final
fallback and is never intentionally overwritten.

Supported trailers include local media such as `Media/Trailer.mp4`, Steam
video sources, and configured YouTube/direct URLs. Playback uses LibVLC and
falls back visibly to screenshots when a trailer fails.

## Controls

| Input | Action |
|---|---|
| D-pad / left stick / arrow keys | Navigate |
| A / Enter | Open or launch |
| B / Escape | Back or open Exit |
| X / Space | Pause or resume trailer |

On-screen action buttons follow the active input device. The controller-status
icon dims when no controller is connected.

## Display support

The reference presentation is 1280×720 and scales uniformly to 1080p, 1440p,
and 4K. Steam Deck’s 1280×800 display uses true-black 40-pixel letterbox bands
to preserve the 16:9 composition without distortion.

## Build and test

Requires the .NET 10 SDK.

```powershell
dotnet build CartLaunchCompanion.Avalonia.sln -c Release
dotnet test Tests/CartLaunchCompanion.Core.Tests/CartLaunchCompanion.Core.Tests.csproj -c Release
dotnet test Tests/CartLaunchCompanion.Desktop.Tests/CartLaunchCompanion.Desktop.Tests.csproj -c Release
```

Run the desktop project:

```powershell
dotnet run --project Source/CartLaunchCompanion.Desktop -c Release
```

## Documentation

- [Vision](Docs/2.0/Vision.md)
- [JSON specification](Docs/2.0/JsonSpecification.md)
- [Folder structure](Docs/2.0/FolderStructure.md)
- [Controller guide](Docs/2.0/ControllerGuide.md)
- [Roadmap](Docs/2.0/Roadmap.md)
- [RC1 notes](Docs/2.0/ReleaseCandidate1.md)
- [Upgrade guide](Docs/2.0/UpgradeGuide.md)

## Reporting RC1 issues

Include the operating system, display resolution, input device, selected launch
method, steps to reproduce, and the relevant file from `Logs`. Remove private
paths, usernames, account information, and API keys before sharing logs.

## License and trademarks

Cart Launch Companion is distributed under the [MIT License](LICENSE). It is
not affiliated with or endorsed by Valve, Microsoft, Rockstar Games, Ubisoft,
VideoLAN, or any other storefront or publisher. Third-party artwork, names,
logos, and trademarks belong to their respective owners.
