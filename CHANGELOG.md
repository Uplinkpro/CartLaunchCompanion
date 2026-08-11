# Changelog

All notable changes to Cart Launch Companion will be documented here.

The project follows semantic versioning where practical.

## [Unreleased]

## [2.2.0] - 2026-08-11

### Added

- Custom Series Collection launcher mode with collection logos, accent colors, ordered shelves, and per-game placement
- Responsive collection layout with centered era dividers and automatic hiding of empty shelves
- Startup loading screen with a circular animated progress indicator
- Series Collection fields and emulator CLI references in the Game Configurator
- Fullscreen emulator launch guide for RetroArch, DuckStation, PCSX2, Dolphin, and RPCS3
- Collection screenshot and complete customization instructions

### Changed

- Collection shelves scale together to remain visible across supported display sizes
- Metadata launcher and gamepad badges now render independently of collection branding
- Direct and custom game metadata pages retain the correct storefront identity
- Game Configurator workflow now includes collection placement before behavior and review

### Fixed

- Removed the false empty-library flash during startup discovery
- Fixed hidden launcher logos on metadata pages in Custom Series Collection mode
- Fixed unnamed and unpopulated collection shelves appearing in the library

## [2.0.0-rc.1] - 2026-08-05

### Added

- Cross-platform Avalonia interface for Windows, Linux, and SteamOS
- Portable Version 2 game configuration with Version 1 import compatibility
- Steam-first metadata, screenshot, and trailer discovery
- Optional SteamGridDB artwork fallback
- Local, Steam, launcher URI, Wine, Proton, Heroic, and Flatpak targets
- Native LibVLC trailer playback with screenshot fallback
- SDL3 controller navigation and hot-plug detection
- Steam Deck and gamepad compatibility badges
- 720p, Steam Deck 1280×800, 1080p, 1440p, and 4K presentation scaling
- Rotating portable logs and bounded metadata cache

### Changed

- Replaced the WinUI 3 frontend with a fullscreen Avalonia experience
- Reworked Home, Metadata, Exit, and error states around one launcher-aware theme

### RC1 limitations

- Linux and physical Steam Deck hardware certification is still required
- Steam Deck compatibility status is configuration-driven
- The game configuration editor remains a planned companion application

- GitHub Actions build and tagged-release workflows
- Repository branding assets
- Community health and contribution documentation
- Dependabot configuration
- Centralized version and repository metadata

## [1.0.0] - Initial public release

- Portable, controller-first WinUI 3 game library
- Steam metadata and trailer integration
- Multiple launcher and local-executable configurations
- Portable game folders, artwork, videos, logs, and cache
