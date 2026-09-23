# DuckStation: Windows and Linux portable support

## Verified upstream

This integration targets the official `stenzek/duckstation` project. The upstream project documents Stable and Preview channels, with Stable tracking the GitHub `latest` release and Preview tracking `preview`.

The supported x64 packages are:

- Windows: `duckstation-windows-x64-release.zip`, launched with `duckstation-qt-x64-ReleaseLTCG.exe`.
- Linux: `DuckStation-x64.AppImage`, stored locally as `DuckStation.AppImage`.

Both packages publish SHA-256 digests in GitHub release metadata. An empty `portable.txt` beside the executable makes the executable directory DuckStation's data root on both Windows and Linux. Emulator Companion writes `settings.ini` there and routes BIOS files, memory cards, save states, screenshots, cheats, and texture packs to `Emulators/Shared` with DuckStation's native folder settings. Existing local folders are migrated with conflict checks and retained backups. DuckStation documents Ubuntu 22.04 or an equivalent distribution as the minimum environment for its current AppImage.

## Emulator Companion behavior

DuckStation supports Windows x64, Linux x64, and **Both** for Stable and Preview. Release discovery uses the exact official tagged release endpoint and exact package name for each platform. It validates the release page, download URL, package metadata, and publisher digest before offering installation.

The Linux product target is gaming-focused distributions: SteamOS, Bazzite, ChimeraOS, and CachyOS. Generic Linux compatibility is useful when it comes from the same AppImage, but it is not the current validation priority.

The installer downloads into an isolated staging folder, verifies size and SHA-256, rejects unsafe Windows archive entries, creates the portable marker, and records each platform separately. Updates preserve portable user files. Publication and registry changes use a recovery record so an interrupted operation can finish cleanup or restore the prior installation.

Live discovery on 2026-09-21 found valid publisher-signed metadata for all four combinations:

- Stable Windows: `stable-2026.09.12.122020`
- Stable Linux: `stable-2026.09.12.122036`
- Preview Windows: `preview-2026.09.21.053135`
- Preview Linux: `preview-2026.09.21.053154`

## Validation status

Windows and Linux package discovery and installation behavior are covered by automated tests. Linux AppImage execution, graphics, controller behavior, game launch, portable storage behavior, and return-to-CLC behavior still need validation on SteamOS, Bazzite, ChimeraOS, and CachyOS. SteamOS plus at least one additional target distribution should pass before Linux automatic launch is treated as validated. The implementation does not hide Linux while that hardware validation is pending.
