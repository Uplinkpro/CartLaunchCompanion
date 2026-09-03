# Changelog

All notable changes to Cart Launch Companion will be documented here.

The project follows semantic versioning where practical.

## [Unreleased]

## [2.7.0] - 2026-09-03

### Added

- Added an optional required-launcher setting that keeps executable launch commands separate from Steam, Rockstar, and other storefront ownership and branding
- Added pre-launch readiness checks that start required Windows launchers, or Steam and Heroic on Linux, before starting the configured game command
- Added UMU-Proton support for portable Windows executables on Linux and SteamOS, including installed-version discovery and guided UMU setup
- Added a cross-platform first-insert setup review for CLC-shaped drives when CLC-Cart Monitor is already installed; setup, trust, and automatic launch remain separate confirmations

### Changed

- Updated the exit confirmation overlay to use the current cartridge logo instead of the retired generic device glyph
- Direct executable games can now retain the correct storefront banner, logo, colors, and launcher label
- Replaced circular loading indicators with a consistent animated three-dot throbber
- Moved one-time trailer runtime preparation into cold startup so opening a metadata page no longer waits for VLC initialization
- Replaced the retired cold-start device symbol with the current cartridge app logo and audited executable, window, drive, loading, and confirmation branding
- Linux autofill now maps portable Windows EXEs to UMU-Proton, with automatically managed stable or GE-Proton choices and isolated host-side compatibility data
- Refreshed the repository screenshots to show the current collection library, metadata layout, platform selection, and UMU-Proton configuration workflow

### Validation

- Passed all 305 automated Core and Desktop tests in the Release configuration
- Installed and hash-verified the Windows and Linux launchers on the GTA Collection physical test cart
- Passed the release-package audit for the Windows, Linux, and combined portable distributions

## [2.6.0] - 2026-08-31

### Added

- Added an in-app launcher ID guide with publisher-specific formats, discovery instructions, reliability warnings, and verified examples
- Added EA app as a first-class Windows launcher using a cart-local executable or complete launch URI
- Added confirmed local launcher discovery from Steam manifests, Epic manifests, Windows AUMIDs, and launcher-created shortcuts
- Added safe Windows drive branding that uses the CLC app icon and collection name without executable AutoRun commands
- Added normalized platform-banner discovery, Configurator platform suggestions, and optional platform logos for emulated games
- Added folder-driven launcher branding for Battle.net, EA, Flatpak, Heroic, HoYoverse, itch.io, Proton, Wine, and the existing launcher catalog
- Added Linux launch autofill from predictable Windows configurations while keeping every generated command editable

### Changed

- Clarified that GOG, Rockstar, and Amazon game IDs are reference metadata and that CLC requires an executable or complete URI to launch those games
- Simplified launcher assets to `Banner.png` and `Logo.png`, removing obsolete launcher backgrounds and glyphs
- Made the Windows and Linux launch pages show only the fields relevant to the selected launcher while retaining executable and companion controls

### Fixed

- Prevented the Game Configurator title, path, and toolbar from overlapping at high Windows display-scaling levels by placing the controls in a responsive wrapping row
- Display platform logos for emulator games instead of the generic Cart Launch placeholder when a matching platform logo exists

### Validation

- Passed all 297 automated Core and Desktop tests in the Release configuration
- Verified PSP banner and logo resolution on the GTA Collection physical test cart
- Passed the release-package audit for the Windows, Linux, and combined portable distributions

## [2.5.1] - 2026-08-29

### Added

- Added per-stage physical-cart launch timing diagnostics for detection, trust lookup, source verification, protected copying, staged verification, final authorization, process start, and total automatic-launch time
- Added unified cartridge artwork for the launcher, configurator, Cart Monitor, repository banner, and social preview

### Changed

- Moved the physical-cart identity into the cross-platform hidden `.cartlaunch/cartridge.json` location and hardened the reserved directory against links and junctions
- Trust and re-trust now immediately ask whether the specific cart should launch automatically, while clearly distinguishing cart permission from Monitor sign-in startup

### Fixed

- Prevented a transient Windows remount during safe eject from being mistaken for a fresh cart insertion, and retry busy ejections before reporting failure
- Explicitly show and foreground both the verified launcher and interactive Monitor windows when started by a background process
- Kept the required trust confirmation beside the trust action in a fixed footer so display scaling cannot hide it inside the review content
- Kept exit actions visible after keyboard input and added direct E-key/X-button safe-eject handling for trusted physical carts
- Prevented background cart detection from probing and remounting a fixed-media USB volume while Windows is safely ejecting it
- Allow slow UAS enclosures to finish an accepted Plug-and-Play removal before reporting or retrying a safe eject

### Validation

- Passed the complete Windows physical-cart cycle on the GTA Collection USB SSD: trust, automatic launch, busy-drive rejection, retry, safe removal, physical reinsertion, and protected relaunch
- Hash-verified the Windows and Linux cart runtimes against the v2.5.1 release candidate while preserving cart-specific games, ROMs, emulators, configuration, logs, and metadata cache
- Passed all 278 automated Core and Desktop tests in the Release configuration

## [2.5.0] - 2026-08-27

### Added

- Added branded verification and launch feedback for trusted physical carts
- Added native Windows Plug-and-Play safe removal for fixed-media USB bridges
- Added platform-banner artwork support and clearer physical-cart controls
- Added Windows launchers that start the graphical applications without command windows

### Changed

- Renamed the optional local component to CLC-Cart Monitor and consolidated its portable files under `System/CartMonitor`
- Moved portable assets, cache, maintenance tools, and schemas under `Cart/System` for a cleaner cart layout
- Background Monitor startup now verifies and launches an approved cart that was already mounted at sign-in or immediately after installation
- Explorer cart windows are minimized during trusted verification and closed before safe removal

### Fixed

- Fixed USB SSD ejection that could report success without actually removing the Windows volume
- Prevented the post-eject status check from probing and remounting a just-dismounted drive
- Fixed Monitor repair and startup behavior after the Cart Monitor rename
- Preserved exact cover-art proportions and platform branding across library, platform selection, and metadata views

### Validation

- Passed an end-to-end Windows 11 NTFS USB SSD cycle: trust, automatic verification, protected local launch, safe eject, physical removal, reinsertion, and automatic relaunch
- Passed 275 automated Core and Desktop tests

## [2.4.0] - 2026-08-25

> Physical-cart Host support is opt-in. Hardware reports remain especially valuable on SteamOS, Bazzite, ChimeraOS, CachyOS, and systems with unusual removable-media policies.

### Added

- Hardened signed updates with key rotation, downgrade and replay protection, approved HTTPS origins, bounded redirects, manifest-governed archive extraction, and crash-safe rollback
- Added automated release-package audits for checksums, platform separation, documentation, Linux executable modes, and development-file leakage
- Added update maintenance-path protection against symbolic links, junctions, reparse points, and abandoned staging accumulation

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
- Added an explicit Configurator-to-Host trust-review handoff that revalidates readiness and selects the cart without granting trust or automatic launch

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
