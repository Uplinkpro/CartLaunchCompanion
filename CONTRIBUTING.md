# Contributing to Cart Launch Companion

Thank you for helping improve Cart Launch Companion.

## Contribution terms

Cart Launch Companion is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE), not an OSI-approved open-source license.

By intentionally submitting a contribution for inclusion in this project, you confirm that you have the right to submit it and grant Uplinkpro a perpetual, worldwide, non-exclusive, irrevocable, royalty-free license to use, reproduce, modify, distribute, sublicense, relicense, and commercially license that contribution as part of Cart Launch Companion and related works. You retain ownership of your contribution.

Do not submit a contribution if you cannot grant these rights. Substantial contributions may require a separate contributor agreement before acceptance.

## Before opening a change

1. Search existing issues and pull requests.
2. Open an issue for large features or behavior changes.
3. Keep each pull request focused on one fix or feature.
4. Do not commit personal game libraries, credentials, private paths, logs, or copyrighted artwork.

## Development setup

- Windows 10 or Windows 11
- Visual Studio with Windows application development components
- .NET 10 SDK
- Windows App SDK requirements for WinUI 3

Clone and build:

```powershell
git clone https://github.com/Uplinkpro/CartLaunchCompanion.git
cd CartLaunchCompanion
dotnet restore
dotnet build CartLaunchCompanion.sln -c Release -p:Platform=x64
```

## Coding guidelines

- Follow `.editorconfig`.
- Keep nullable reference types enabled.
- Prefer clear names and small, focused methods.
- Preserve portable paths and avoid machine-specific absolute paths.
- Add or update documentation when configuration behavior changes.

## Testing

Before submitting a pull request:

```powershell
dotnet restore
dotnet build CartLaunchCompanion.sln -c Release -p:Platform=x64 --no-restore
dotnet publish CartLaunchCompanion.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64
```

Also verify controller navigation, trailer playback, launcher restoration, and at least one configured game where relevant.

## Pull requests

Include:

- What changed
- Why it changed
- How it was tested
- Screenshots or video for interface changes
- Any configuration or compatibility impact
