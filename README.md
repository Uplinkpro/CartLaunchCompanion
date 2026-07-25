# Cart Launch Companion

A portable, fullscreen, gamepad-friendly launcher for Windows that brings together games from multiple launchers into a single console-style interface.

Inspired by the simplicity of physical game cartridges, Cart Launch Companion lets you browse your collection with cover art, game metadata, trailers, and launcher branding while launching games from Steam, Epic Games, GOG, Amazon Games, Xbox, Ubisoft Connect, Rockstar Games Launcher, Flashpoint, or directly from executable files.

---

## Features

### Console-Style Interface

* Fullscreen controller-friendly UI
* Dynamic layouts that adapt to your library size
* Responsive navigation using keyboard, mouse, or gamepad
* Large cover artwork and metadata pages
* Visual selection effects
* Automatic launcher branding

---

## Supported Launchers

| Launcher                | Status |
| ----------------------- | ------ |
| Steam                   | ✔      |
| Epic Games              | ✔      |
| GOG Galaxy              | ✔      |
| Amazon Games            | ✔      |
| Xbox App / Game Pass    | ✔      |
| Ubisoft Connect         | ✔      |
| Rockstar Games Launcher | ✔      |
| Flashpoint              | ✔      |
| Direct Executable       | ✔      |

---

## Rich Metadata

Each game can display:

* Title
* Synopsis
* Developer
* Publisher
* Genres
* Release date
* Website
* Player counts
* Cover artwork
* Wide hero artwork
* Local gameplay videos
* Steam trailers
* YouTube trailers

Steam metadata can even be used for non-Steam games.

---

## Trailer Support

Cart Launch Companion supports multiple trailer sources.

Priority order:

1. Local `snaps.mp4`
2. Steam adaptive trailers
3. YouTube
4. Local video URL
5. No trailer fallback

Native Steam adaptive trailers are played through LibVLC.

---

## Gamepad Support

Fully navigable using an Xbox-compatible controller.

Default controls:

| Button             | Action               |
| ------------------ | -------------------- |
| D-Pad / Left Stick | Navigate             |
| A                  | Select / Launch      |
| B                  | Back                 |
| Start              | Launch selected game |
| View               | Return to library    |

Metadata pages also display on-screen controller prompts.

---

## Portable

No installation required.

```
CartLaunchCompanion/
│
├── Assets/
├── Games/
├── System/
│
├── CartLaunchCompanion.exe
├── CartLaunchCompanion.cmd
└── Launch.ps1
```

All user data stays inside the portable folder.

---

# Game Configuration

Each game is stored in its own folder.

Example:

```
Games/
    Halo Infinite/
        Game.json
        Cover.png
        Header.png
        snaps.mp4
```

---

## Example Game.json

```json
{
  "Name": "Halo Infinite",
  "Launcher": "Steam",

  "SteamID": "1240440",

  "SteamMetadataID": "1240440",

  "Executable": "",

  "Arguments": "",

  "WorkingDirectory": "",

  "ProcessName": "HaloInfinite",

  "RestoreOnExit": true,

  "CoverImage": "Cover.png",

  "HeaderImage": "Header.png",

  "VideoFile": "snaps.mp4",

  "VideoUrl": "",

  "YouTubeUrl": "",

  "Description": ""
}
```

---

## Game.json Fields

| Field            | Description                                                  |
| ---------------- | ------------------------------------------------------------ |
| Name             | Display name                                                 |
| Launcher         | Launcher type                                                |
| SteamID          | Steam App ID used for launching                              |
| SteamMetadataID  | Steam App ID used only for metadata and trailers             |
| Executable       | Local executable path                                        |
| Arguments        | Command-line arguments                                       |
| WorkingDirectory | Working directory                                            |
| ProcessName      | Executable name used to restore the launcher after game exit |
| RestoreOnExit    | Automatically restore launcher when game exits               |
| CoverImage       | Standard cover artwork shown in the library                  |
| HeaderImage      | Wide artwork shown on the metadata page                      |
| VideoFile        | Local trailer (`snaps.mp4`)                                  |
| VideoUrl         | Direct video URL                                             |
| YouTubeUrl       | YouTube trailer                                              |
| Description      | Optional custom synopsis                                     |

---

## Steam Metadata Override

Any launcher can use Steam metadata without changing how the game launches.

Example:

```json
{
    "Launcher": "GOG",
    "GogGameId": "123456",
    "SteamMetadataID": "620"
}
```

The game still launches from GOG while using Steam artwork, trailers, genres, descriptions, and release information.

---

## Supported Artwork

Library:

```
Cover.png
```

Metadata Page:

```
Header.png
```

Trailer:

```
snaps.mp4
```

---

## Building

Requirements:

* Visual Studio 2022
* .NET 10 SDK
* Windows App SDK
* LibVLCSharp
* VLC runtime

Build:

```
dotnet build
```

Publish portable:

```
Publish-Portable.ps1
```

---

## Current Features

* Portable architecture
* Fullscreen launcher
* Gamepad navigation
* Steam metadata
* Steam trailer playback
* YouTube trailers
* Local trailers
* Dynamic launcher branding
* Automatic launcher restore
* Multiple launcher support
* Responsive UI
* Adaptive layouts

---

## Roadmap

Planned improvements include:

* Automatic library import
* Theme editor
* Favorites
* Collections
* Search and filters
* Custom controller mappings
* Emulation support
* Background music
* Download manager
* Cloud artwork cache

---

## Contributing

Contributions, bug reports, feature requests, and pull requests are welcome.

Please open an issue before beginning work on large features so they can be discussed.

---

## License

This project is released under the MIT License.

Launcher names, logos, trademarks, and game artwork remain the property of their respective owners.

---

## Acknowledgements

* Valve
* Microsoft
* Epic Games
* GOG
* Ubisoft
* Rockstar Games
* LibVLC
* VLC Media Player
* Windows App SDK
* .NET
