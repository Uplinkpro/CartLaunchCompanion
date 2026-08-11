# Cart Launch Updater security

The Cart Launch Updater is a small maintenance component intended to update the portable CLC runtime on a cart after the launcher closes. It does not update games, collection configuration, artwork, media, or user metadata.

> The updater foundation is under development. Production update application remains disabled until the official offline release-signing public key is embedded in the maintenance executable.

## Portable layout

```text
CartLaunchCompanion/
├── System/
│   ├── Windows-x64/
│   └── Linux-x64/
├── Maintenance/
│   ├── Windows-x64/CartLaunchCompanion.Updater.exe
│   └── Linux-x64/CartLaunchCompanion.Updater
└── .cartlaunch/
    ├── update-staging/
    ├── previous-runtime/
    ├── failed-runtime/
    └── update-journal.json
```

Only a payload staged beneath `.cartlaunch/update-staging/` can be activated. The destination platform must be exactly `Windows-x64` or `Linux-x64`.

## Manifest requirements

An update manifest is limited to 64 KiB, a JSON depth of 8, and 4,096 payload files. It must identify Cart Launch Companion, the destination platform, security version, launcher entry point, every file length and SHA-256 hash, and a combined root fingerprint.

The parser rejects comments, trailing commas, duplicate properties, unknown fields, unsupported versions, malformed hashes, duplicate paths, absolute paths, traversal, network paths, links, junctions, reparse points, missing files, and unexpected files.

## Transaction

1. Parse and validate the bounded manifest.
2. Verify its publisher signature.
3. Verify every staged file and the root fingerprint.
4. Write and flush an update journal.
5. Wait for the running launcher to close.
6. Move the current platform runtime to `previous-runtime`.
7. Move the complete staged runtime into the familiar `System/<platform>` location.
8. Verify the activated runtime again.
9. Start the new launcher and observe a ten-second startup health window.
10. Delete the backup only after the new launcher remains running.

If activation or verification fails, the previous runtime is restored. If the new launcher exits during the health window, it is moved to `failed-runtime`, the previous runtime is restored, and the previous launcher is restarted.

An interrupted journal in the `ActiveMovedToBackup` state is recovered on the next maintenance run before another update starts.

## Signing boundary

HTTPS transport is not sufficient update authorization. Production manifests must be signed by the offline Uplinkpro release-signing key, and the corresponding public key must be compiled into the updater. The updater never accepts a public key, executable path, command, or update endpoint from a cart manifest.

Until that trust anchor is configured, the updater exits without changing the active runtime.

## Tests

The core test suite covers exact payload verification, modified files, unexpected executables, path escape attempts, unknown manifest fields, transactional activation, manual rollback, and interrupted-update recovery. Additional signing, extraction, download, free-space, removal-during-update, and end-to-end packaging tests are required before the updater is exposed in the launcher interface.
