# Cart Launch Companion 2.0 Roadmap

The roadmap describes development order, not release promises.

## Phase 0 — Foundation

- Vision
- Design principles
- Architecture
- Folder structure
- JSON direction
- Theme guide
- Controller guide
- Roadmap
- Development-branch README notice

## Phase 1 — Configuration

- Version 2 configuration models
- JSON serializer
- JSON schema
- Runtime validation
- Version 1 importer
- Complete example configurations
- Unit tests

## Phase 2 — Portable Library

- Portable-root discovery
- Game-folder discovery
- Artwork and media path resolution
- Windows/Linux target selection
- Library loading
- Missing-file reporting
- Cache and logging rules

## Phase 3 — Home

- Avalonia Home view
- Centered one- or two-row curated shelf
- Launcher glyph and accent
- Controller, mouse, and keyboard selection
- A opens Metadata
- B opens Exit
- Responsive scaling

## Phase 4 — Metadata

- 3840×1240 background convention
- Cover art up to 600×900
- Game information
- Trailer panel
- A launches
- B returns
- X controls trailer
- Launcher glyph inset from upper-right

## Phase 5 — Launching

- Shared launch requests
- Windows adapters
- Linux/SteamOS adapters
- Steam
- Local/native
- URI
- Flatpak
- Heroic
- Wine/Proton
- Process monitoring
- Hide and restore behavior

## Phase 6 — Controller

- SDL input service
- Hot-plugging
- Steam Deck controls
- Input repeat
- Controller glyph selection
- Optional vibration
- Automated navigation tests where practical

## Phase 7 — Themes

- Shared theme service
- Launcher accents
- Chrome branding
- Scene layers
- Near-black base environment
- Consistent Exit and Error states

## Phase 8 — Animation

- Animated overhead light
- Haze and dust
- Floor response
- Page transitions
- Cartridge-inspired launch transition
- Reduced-motion mode

## Phase 9 — Media and Polish

**Status: complete.** Windows visual validation and automated release checks
are complete. Linux/SteamOS hardware certification remains part of Phase 10.

- Cross-platform trailer backend
- Error recovery
- Performance profiling
- Storage review
- Log rotation
- Accessibility
- Multi-resolution testing (1280×720, Steam Deck 1280×800, 1920×1080,
  2560×1440, and 3840×2160)

## Phase 10 — Release Candidate

- Windows portable publish
- Linux portable publish
- Combined portable folder
- SteamOS hardware testing
- Documentation update
- Screenshots
- README rewrite
- Upgrade guide
- Version 2 release candidate
