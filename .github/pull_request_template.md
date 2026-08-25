## Summary

Describe the user-facing change and why it belongs in the portable game-cart workflow.

## Validation

- [ ] `dotnet build CartLaunchCompanion.Avalonia.sln --configuration Release -p:TreatWarningsAsErrors=true`
- [ ] `dotnet test CartLaunchCompanion.Avalonia.sln --configuration Release -p:TreatWarningsAsErrors=true`
- [ ] Windows behavior was tested or is unchanged.
- [ ] Linux/SteamOS behavior was tested or is unchanged.
- [ ] No API keys, credentials, personal paths, ROMs, game files, or generated build output are included.

## Screenshots

Include before-and-after screenshots for visible interface changes when practical.

## Security and compatibility

Call out changes to process launching, removable-media trust, updates, path handling, configuration schemas, or backward compatibility.
