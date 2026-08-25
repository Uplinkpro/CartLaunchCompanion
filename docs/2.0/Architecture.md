# Cart Launch Companion 2.0 Architecture

## Objectives

The architecture must support:

- Windows and Linux/SteamOS
- A shared Avalonia UI
- Platform-specific launch behavior
- Human-readable configuration
- Controller-first navigation
- Portable deployment
- Clean separation of concerns
- Low storage and maintenance overhead

## Solution layout

```text
Source/
├── CartLaunchCompanion.Core/
├── CartLaunchCompanion.Desktop/
├── CartLaunchCompanion.Configurator/
├── CartLaunchCompanion.Updater/
├── CartLaunchCompanion.Host/
├── CartLaunchCompanion.HostCleanup/
├── CartLaunchCompanion.Platform.Windows/
└── CartLaunchCompanion.Platform.Linux/
```

## Dependency direction

```text
Desktop UI
    ↓
Core abstractions and models
    ↑
Windows implementation / Linux implementation
```

### CartLaunchCompanion.Core

Contains platform-neutral code:

- Game configuration models
- JSON loading and serialization
- Validation
- Portable path discovery abstractions
- Artwork and metadata contracts
- Launch request models
- Launch-service interfaces
- Input action models
- Theme definitions
- Logging abstractions

Core must not reference Avalonia, WinUI, Windows APIs, Wine, Flatpak, or storefront-specific native APIs.

### CartLaunchCompanion.Desktop

Contains the Avalonia application:

- Views
- View models
- Reusable controls
- Styles
- Scene composition
- Navigation
- Focus behavior
- Mouse and keyboard input
- Controller-action handling
- Dependency composition

Business rules do not belong in view code-behind.

Code-behind is limited to view-specific behavior that cannot be expressed cleanly through bindings, styles, behaviors, or view models.

### CartLaunchCompanion.Platform.Windows

Contains Windows-specific behavior:

- Windows process launching
- URI and shell launching
- Microsoft Store/Xbox app launching
- Process monitoring
- Foreground and restoration support
- Windows path and executable handling

### CartLaunchCompanion.Platform.Linux

Contains Linux-specific behavior:

- Native executable launching
- Steam URI launching
- Flatpak launching
- Heroic integration
- Wine/Proton launching
- Process monitoring
- Linux path and executable permission handling
- SteamOS-specific behavior where required

## Service boundaries

Suggested interfaces:

```text
IGameConfigurationService
IGameLibraryService
IArtworkService
IMetadataService
ILaunchService
IProcessMonitor
IPortablePathService
IPlatformService
IControllerService
INavigationService
IThemeService
ILogService
```

## Navigation states

The primary states are:

```text
Home
Metadata
Launching
ExitConfirmation
Error
```

The UI should use explicit application state rather than directly hiding and showing unrelated controls.

## Error handling

- Expected failures return structured results.
- Errors include a user-facing message and a technical log entry.
- Decorative systems such as animation, lighting, sound, and trailers must never prevent navigation or launching.
- Unsupported launch methods must be clearly reported instead of silently failing.

## Testing priorities

- JSON parsing and validation
- Current-format JSON validation
- Portable-root resolution
- Windows/Linux target selection
- Launch request generation
- Navigation state transitions
- Controller action mapping
- Missing and invalid file handling
