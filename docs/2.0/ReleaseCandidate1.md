# Cart Launch Companion 2.0 RC1

Version: `2.0.0-rc.1`

RC1 is the first portable release candidate for the Avalonia-based Version 2
launcher. It is intended for Windows, Linux, and SteamOS testing before the
final 2.0 release.

## Included

- Windows x64 self-contained runtime
- Linux x64 self-contained runtime for desktop Linux and Steam Deck
- Shared portable `Games`, `Assets`, `Config`, `Schemas`, `Logs`, and `Cache`
- Version 1 configuration compatibility importer
- Steam metadata and optional SteamGridDB artwork enrichment
- Steam, local executable, URI, Heroic, Flatpak, Wine, and Proton launch targets
- SDL3 controller input
- Local, Steam, YouTube-compatible, and direct trailer sources through LibVLC

## Validation completed

- Release build with zero warnings or errors
- 20 Core tests and 27 Desktop tests
- Windows Home, Metadata, trailer, Escape, and Exit interaction pass
- Windows portable logs and cache maintenance
- 1280×720 reference layout and uniform scaling rules for 1280×800,
  1920×1080, 2560×1440, and 3840×2160

## Validation still required

- Linux distribution smoke test with LibVLC installed
- Physical Steam Deck controller, video, suspend/resume, and game-launch test
- Multiple-monitor and DPI testing on additional Windows systems
- Clean-machine Windows test outside the development workstation

Report issues with the RC version, operating system, game configuration,
steps to reproduce, and the relevant file from `Logs`.
