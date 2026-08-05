# Upgrading from Version 1 to Version 2 RC1

## Preserve the old installation

Keep a backup of the complete Version 1 folder. Install RC1 into a new folder
for the first test; do not overwrite the working Version 1 installation.

## Move the game library

Copy the Version 1 game folders into the RC1 `Games` folder. Version 2 detects
legacy JSON and imports known fields in memory without rewriting the original
file.

Review each imported game before relying on it:

- Confirm its Windows and Linux launch targets.
- Confirm executable, working-directory, and process-monitoring paths.
- Move artwork into the documented `Artwork` folder when convenient.
- Put a local trailer in `Media/Trailer.mp4` when desired.
- Add a Steam metadata ID independently from the actual launcher.

## Convert to Version 2 JSON

Use `Games/Examples/Example Game/game.json` as the reference. The Version 2
schema is `Schemas/game.schema.json`.

Compatibility fields are optional:

```json
"steamDeckCompatibility": "verified",
"gamepadSupport": "full"
```

## Metadata settings

Copy `Config/metadata.example.json` to `Config/metadata.json` only when using a
SteamGridDB API key. Never share or commit the populated file.

## Rollback

Close RC1 and start the preserved Version 1 folder. RC1 does not intentionally
modify legacy configurations, so rollback does not require a conversion step.
