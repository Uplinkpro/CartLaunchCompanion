# Cart Launch Companion 2.0 Development Workflow

## Branches

- `main` — stable Version 1 source
- `avalonia-migration` — Version 2 development

Version 2 becomes `main` only after it is usable and the Version 1 source has a permanent branch and release tag.

## Build

```powershell
dotnet restore CartLaunchCompanion.Avalonia.sln
dotnet build CartLaunchCompanion.Avalonia.sln -c Release
```

## Run desktop app

```powershell
dotnet run --project Source/CartLaunchCompanion.Desktop
```

## Commit scope

Keep commits focused:

- Documentation
- Configuration
- Library
- Home
- Metadata
- Launching
- Controller
- Theme
- Animation
- Build and packaging

## Pull requests

Large changes should include:

- Purpose
- Supported principle or roadmap phase
- Files changed
- Windows test result
- Linux/SteamOS test result when relevant
- Controller test result when relevant
- Screenshots for visual changes
- Storage impact for new assets or dependencies

## Definition of done

A phase is complete when:

- It builds without warnings introduced by the phase.
- Required tests pass.
- Failure states are logged and user-visible where appropriate.
- Documentation reflects implemented behavior.
- No user library, personal paths, logs, caches, or build output are committed.
