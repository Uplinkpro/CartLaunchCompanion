# Changelog

All notable changes to Cart Launch Companion will be documented here.

The project follows semantic versioning where practical.

## [Unreleased]

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
