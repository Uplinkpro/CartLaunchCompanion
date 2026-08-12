# Changelog

All notable changes to Cart Launch Companion will be documented here.

The project follows semantic versioning where practical.

## [Unreleased]

### Added

- Added Host-authorized safe eject for verified physical-cart sessions on Windows and supported Linux gaming distributions
- Added current-user-only, size-bounded eject requests that cannot carry arbitrary commands or device paths
- Unified manual and automatic trusted-cart session tracking so either launch type can be closed and cleaned safely
- Hardened Host eject messages with an exact JSON schema, bounded endpoint names, malformed and truncated payload rejection, and isolated same-user pipe tests
- Added automated exactly-once insertion and removal coverage for physical carts
- Revalidate the exact tracked cart identity immediately before safe removal and report media removed mid-eject as already removed
- Isolated platform ejection behind a testable boundary with coverage for busy media, flush failures, identity substitution, exact target selection, and Linux mount mapping
- Added fail-safe, structured Host audit logs with sanitized event results, pseudonymous cart tokens, 512 KiB rotation, and three-file retention
- Added Host lifecycle coverage for immediate trust revocation, data-preserving repair, all eight uninstall retention combinations, and path-containment rejection
- Host uninstall now always removes transient protected runtime sessions while preserving trust, settings, and logs exactly as selected
- Added a final pre-launch authorization gate that reloads cart identity, trust, and runtime approval after staging to close removal, substitution, and revocation races
- Added race coverage proving cancellation removes partial staging sessions and changed approval fingerprints cannot launch
- Made standalone collection and cart-identity writes atomic with same-directory temporary files, disk flush, and failure cleanup
- Added interruption coverage proving cancelled game, collection, layout, and identity writes preserve prior valid data and remove temporary files
- Artwork and trailer downloads now guarantee partial `.download` cleanup and replace existing media only after a complete transfer
- Hardened runtime inventory paths to reject control characters, repeated separators, dot segments, traversal, rooted paths, and ambiguous normalization before staging
- Added deterministic manifest fuzzing and boundary tests for duplicate fields, nesting, malformed Unicode, randomized bytes, excessive inventories, links, and fingerprint stability
- Added a per-user single-instance Host lock to prevent competing monitors and duplicate automatic-launch state
- Final authorization now re-hashes the staged runtime immediately before process creation to reject local session tampering
- Added a removable-drive hardware test checklist for Windows, SteamOS, Bazzite, ChimeraOS, and CachyOS
- Upgraded the Configurator with a guided Create/Prepare Physical Cart workflow that preserves existing identities and files
- Added a clear readiness report for root folders, cart identity, and verified Windows/Linux runtime inventories before Host trust

- Added the Phase 3 physical-cart identity foundation with bounded root manifests and stable SHA-256 fingerprints
- Added a strict per-user trusted-cart database with separate automatic-launch approval and trust revocation
- Added an explicit local Cart Launch Host installation plan enumerating its executable, startup entry, settings, trust database, and logs
- New portable carts now receive a one-time friendly identity while host trust remains a separate per-computer decision
- Added the Cart Launch Host management utility with explicit install, repair, trust, revocation, and selective uninstall screens
- Added separate per-user Host runtime and data locations so uninstall can preserve or remove trust, settings, and logs exactly as selected
- Added Windows current-user and all-users Host installation choices; all-users uses normal administrator confirmation while cart trust remains isolated per user
- CLC now offers the optional Host installer when no running or installed Host is found
- Added debounced passive mounted-cart detection that validates only the fixed root identity file and never grants trust or executes software
- Trust enrollment now records exact Windows and Linux runtime file lengths, SHA-256 hashes, and combined fingerprints
- Added protected per-user runtime staging with verification before copying, verification after copying, fixed launcher entry points, and automatic failed-session cleanup
- The Host can manually verify and prepare a trusted connected cart while deliberately providing no execution action yet
- Added a separately confirmed manual trusted-cart launch using one fixed staged executable, one structured cart-root argument, no command shell, and a sanitized environment
- Protected launch sessions track the exact CLC child process and remove their local runtime after it exits or when launch is cancelled
- Added explicit per-cart automatic-launch approval that can be disabled independently without revoking cart trust
- Insert-triggered launch requires identity trust, auto-launch approval, approved runtime verification, duplicate suppression, and retry rate limiting
- Removing a cart cancels in-progress preparation or closes only that cart's tracked CLC process before cleaning its local session

## [2.3.0] - 2026-08-11

### Changed

- Relicensed future source and releases under the PolyForm Noncommercial License 1.0.0
- Added required Uplinkpro attribution notices and separate commercial licensing terms
- Updated the continuous-integration badge to track `main` after retiring `avalonia-migration`

### Added

- Added the fail-closed foundation for a small cross-platform Cart Launch Updater
- Added strict update manifests, full payload integrity checks, transactional runtime activation, rollback, and interrupted-update recovery
- Added adversarial tests for path escape, tampering, unexpected files, malformed manifests, and recovery
- Added the official ECDSA update trust anchor, a release-only manifest signer, and signed tagged-release automation
- Added opt-in update discovery, bounded downloads, safe archive extraction, free-space checks, signed staging, and maintenance restart handoff
- Added portable file locators for game, emulator, ROM, and companion paths in the Game Configurator
- Added collection header-logo preview, dimension guidance, safe artwork import, and collection.json saving to the Series tab
- Added automatic loading and selection of existing game configurations plus a per-game artwork readability audit on the Review page
- Replaced the single metadata image with dedicated cover, background, logo, and icon previews in the Configurator
- Added selected-launcher-only host detection and strict same-cart residency rules for games, emulators, ROMs, and companion tools
- Added an explicit direct-URL download action that validates artwork, stores files locally, updates paths, saves game.json, and refreshes previews
- Added visual Series Collection shelf organization with drag-and-drop ordering and transactional saving
- Added SteamGridDB title matching, artwork galleries, refresh controls, deletion controls, and persisted non-Steam matches
- Added hero and 16:9 background handling with artwork previews and readability checks
- Added a guided portable cart creator that produces `Cart`, `Games`, `Emulators`, and `Roms` at the media root
- Added release checks that reject mismatched signing keys, missing assets, empty assets, and debug symbols

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
