# Cart Launch Updater security

The Cart Launch Updater is a small maintenance component intended to update the portable CLC runtime on a cart after the launcher closes. It does not update games, collection configuration, artwork, media, or user metadata.

> The signed updater is available in 2.3 and continues to receive defense-in-depth hardening on `main`. Update checks and installation are always initiated by the user; offline launching remains unaffected.

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

The cart root and every existing component of the active runtime, maintenance, staging, backup, failed-runtime, manifest, journal, and archive paths are rejected if they are symbolic links, junctions, or reparse points. These checks run before download and again immediately before extraction and filesystem replacement. Abandoned staging directories older than seven days are removed without following links; encountering a linked staging entry fails closed.

## Manifest requirements

An update manifest is limited to 512 KiB, a JSON depth of 8, and 4,096 payload files. It must identify Cart Launch Companion, the destination platform, security version, launcher entry point, every file length and SHA-256 hash, and a combined root fingerprint.

The parser rejects comments, trailing commas, duplicate properties, unknown fields, unsupported versions, malformed hashes, duplicate paths, absolute paths, traversal, network paths, links, junctions, reparse points, missing files, and unexpected files.

## Transaction

1. Parse and validate the bounded manifest.
2. Verify its publisher signature.
3. Verify every staged file and the root fingerprint.
4. Read the installed launcher's embedded product version and require the signed update version to be strictly newer.
5. Write and flush an update journal.
6. Wait for the running launcher to close.
7. Move the current platform runtime to `previous-runtime`.
8. Move the complete staged runtime into the familiar `System/<platform>` location.
9. Verify the activated runtime again.
10. Start the new launcher and observe a ten-second startup health window.
11. Delete the backup only after the new launcher remains running.

If activation or verification fails, the previous runtime is restored. If the new launcher exits during the health window, it is moved to `failed-runtime`, the previous runtime is restored, and the previous launcher is restarted.

An interrupted journal is recovered before another update starts. Until the ten-second health window completes, every activation state is treated as unconfirmed: if a backup exists, the new runtime is removed and the known-good backup is restored—even if a crash prevented the journal from recording the directory move. Rollback itself ignores cancellation once filesystem replacement has begun, and it never deletes the active runtime unless a backup is actually present.

## Signing boundary

HTTPS transport is not sufficient update authorization. Production manifests are signed by the Uplinkpro release-signing key stored as an encrypted GitHub Actions secret, and the corresponding public key is compiled into the updater. The private key is never committed or included on a cart. The updater never accepts a public key, executable path, command, or update endpoint from a cart manifest.

Tagged release builds generate platform-specific runtime payloads and signed manifests in GitHub Actions. A forged or modified manifest cannot pass verification with the embedded public key.

CLC uses an embedded key-ID allowlist to support deliberate signing-key rotation. A successor public key must first be added to CLC and distributed in a normal trusted release while the existing key is still active. Only after that rollout may the repository's `CLC_UPDATE_SIGNING_KEY_ID` variable and encrypted private-key secret switch to the successor. The signer refuses IDs absent from the compiled allowlist and refuses private keys that do not match the selected ID. The manifest's key ID is itself covered by the signature, unknown IDs are rejected, and manifests cannot supply their own public keys. Retiring a compromised or obsolete key requires removing it from the allowlist in a subsequent release.

A valid signature does not authorize a downgrade. The maintenance updater independently compares the signed manifest version with the product version embedded in the installed launcher. Equal versions, older versions, malformed versions, and an unverifiable installed version all fail closed before any active runtime files are moved.

## Download and staging

The launcher checks only the official GitHub repository's latest release. It requires the expected manifest and payload names for the current platform, limits downloads to 1 GiB, reserves an additional 128 MiB of free space, and stages data beneath `.cartlaunch/update-staging`.

Update networking disables automatic redirects so CLC can validate every hop. The API request, advertised assets, and up to five redirects must use HTTPS on the default port, contain no embedded credentials, and target an exact approved GitHub or GitHub release-asset hostname. HTTP, lookalike domains, arbitrary subdomains, custom ports, and redirects to any other host are rejected before response content is trusted.

The manifest signature is verified before archive extraction. Before downloading the payload, CLC rejects manifests declaring more than 4,096 files, more than 1 GiB for any file, or more than 2 GiB total expanded data. ZIP and TAR extraction then streams through the signed manifest as an exact allowlist: paths, entry count, individual lengths, total size, duplicates, missing files, and unexpected files are enforced before the maintenance updater starts. Linux links and other non-file archive entries are rejected. Every extracted file is subsequently SHA-256 verified against the signed manifest. Update checking is opt-in and a network failure never prevents normal offline use.

## Tests

The core test suite covers exact payload verification, modified files, unexpected executables, path escape attempts, unknown manifest fields, transactional activation, manual rollback, every interrupted activation state, missing-backup fail-closed behavior, release discovery, bounded downloads, extraction, and cart-package creation. Tagged releases also refuse to publish when the signing secret does not match the public key compiled into CLC, or when package integrity, contents, or required release assets fail the automated audit.
