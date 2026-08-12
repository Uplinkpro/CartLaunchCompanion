# Security Policy

Cart Launch Companion launches games and optional companion applications configured by the user. That capability deserves a clear security boundary, especially as the project develops support for self-contained installations on removable physical "carts."

No software can make arbitrary removable hardware perfectly safe. Our goal is to minimize authority, reject executable instructions from untrusted media, make every security-sensitive change visible, and fail closed when identity or integrity cannot be verified.

## Supported versions

Security fixes are provided for the latest published release and the current `main` branch. Older releases may not receive security updates.

## Current application security

The following statements apply to the currently published 2.3 release:

- Game launch targets and optional companion applications come from user-managed `game.json` files.
- CLC does not treat downloaded metadata or artwork as permission to launch a program.
- Steam and SteamGridDB credentials are stored through the operating system's protected credential facility and are not written to `game.json` or plaintext configurator settings.
- Portable paths are resolved relative to the CLC installation where supported.
- CLC does not intentionally send private game-library contents, local paths, or stored credentials to telemetry services.
- Metadata and media providers receive only the requests necessary to retrieve the content the user configured or selected.

Because game configurations can intentionally name executables, arguments, launchers, emulators, and companion applications, users should run only configurations they created or reviewed. A `game.json` file from an untrusted source must be treated as security-sensitive.

## Planned physical cart security model

### Safe removal implementation

- `Eject Cart` appears only when CLC was started by the trusted-cart Host.
- The launcher sends only a versioned `eject` request and trusted cart ID over a current-user-only local pipe; it cannot provide commands, executables, device paths, or shell text.
- The Host accepts the request only for an exact process session it launched and still tracks.
- It closes that process, removes its verified staging directory, flushes writes, and asks the operating system to eject the matching media root.
- Windows uses bounded native volume operations. Supported Linux gaming distributions use fixed `udisksctl` arguments without a shell.

Physical Cart support is under development and is not part of the 2.3 release. The requirements below are design commitments for that feature, not claims about functionality that has already shipped.

Each physical cart is intended to contain its own portable CLC installation, games, configuration, artwork, and media. A small optional **Cart Launch Host** installed on the computer will detect and start carts that the user has explicitly trusted.

### Installation and removal

- Installing or uninstalling the Cart Launch Host requires an explicit confirmation screen.
- The confirmation identifies the component's purpose, every installation location, its startup registration, settings, trust database, and logs.
- The host runs as the signed-in user. It must not require a Windows service, kernel driver, administrator account, Linux root service, or system-wide `udev` rule for normal operation.
- Windows offers current-user installation without elevation or an all-users runtime installation through the normal administrator confirmation. In both modes, cart trust records, settings, and logs remain isolated to each signed-in user.
- Uninstallation stops monitoring, removes automatic startup, removes the local host, and clearly offers removal of trust records, settings, and logs.
- The local Host runtime and its user data occupy separate directories, so removing executable files cannot implicitly remove trust records, settings, or logs the user chose to preserve.
- Installing or uninstalling the local host never modifies or deletes a connected physical cart without a separate, explicit cart-management action.

### Detection is not execution permission

Operating-system volume events only report that media was mounted. Detection never grants trust by itself.

The host will inspect only a bounded, versioned `cartlaunch.cartridge.json` identity manifest at the cart root. A cart cannot supply PowerShell, shell, command-prompt, interpreter, or arbitrary process instructions to the host.

### Trust and integrity

- Trust is stored per operating-system user and granted per cart.
- The cart identity, protected runtime manifest, requested behavior, and security-sensitive launch configuration are verified independently.
- Every protected CLC runtime file, managed assembly, and native library is covered by a signed manifest or a locally approved integrity record.
- Direct game executables and companion applications are recorded as security-sensitive launch targets. Unexpected changes require review and approval before launch.
- Runtime and launch-plan integrity are checked on every insertion, not only when the cart is first enrolled.
- Automatic launch is disabled by default and requires separate approval for each cart.
- A minimum security-version policy prevents a trusted cart from silently downgrading to a known-vulnerable runtime.

### Safe process creation

The host will not execute the verified runtime directly from writable removable media. It will copy the protected runtime into a new, user-only local session directory, verify the staged copy, and launch that copy with the physical cart supplied only as its data root. The session copy is removed after use.

This design reduces file-replacement races, DLL or shared-library substitution, and Linux `noexec` mount compatibility problems while leaving the authoritative portable installation and all user content on the cart.

During trust enrollment, the Host records an exact per-platform runtime inventory containing every relative path, length, SHA-256 hash, and a combined root fingerprint. Preparation verifies the cart against that approved inventory, copies only those approved files to a new per-user session directory, verifies the copied directory again, and exposes only the fixed CLC launcher entry point. Failed or incomplete sessions are removed.

Process creation must:

- launch only the expected CLC executable;
- avoid command shells and script interpreters;
- disable shell execution;
- use structured argument lists rather than concatenated command strings;
- clear dangerous inherited environment variables;
- reject executable paths outside the verified staging directory;
- never accept an executable path or command directly from removable-media metadata.

Manual physical-cart launch additionally requires a separate confirmation showing the cart name, connected media root, and verified local executable. The Host starts only the `PreparedCartRuntime` returned by protected staging, supplies exactly one structured `--cart-root` argument, removes runtime-injection environment variables, tracks the exact child process, and deletes the local runtime session after that process exits. This confirmation does not enable automatic launch.

Automatic launch is a separate per-cart approval. An insertion can launch only after the identity, approval, minimum security version, and complete runtime inventory all match. The Host suppresses duplicate sessions, rate-limits retries, cancels verification when media disappears, and closes only the exact tracked CLC child if its backing cart is removed.

### Path and parser hardening

The host will reject:

- absolute, network, device-namespace, or URI paths;
- `.` or `..` path segments;
- symbolic links, NTFS junctions, reparse points, and nested mount points;
- paths that resolve outside the originally detected cart;
- unsupported manifest versions and unknown security-sensitive fields;
- duplicate JSON properties, comments, trailing content, and malformed encodings;
- manifests exceeding fixed limits for bytes, nesting depth, field count, or string length.

Detection is debounced and rate-limited. The host examines a fixed root filename rather than recursively scanning newly inserted media.

### Local communication

Communication between CLC and the Cart Launch Host is local and restricted to the signed-in user:

- Windows uses a named pipe restricted to the current user identity.
- Linux uses a user-owned Unix socket under the user's runtime directory.
- Messages have a version, strict size limits, and a small allowlist of operations.
- The protocol has no generic `execute` operation.

### Media and presentation

- Cart artwork is not used to style a trust prompt until the cart has been validated.
- User-created carts are labeled as locally trusted, not as Uplinkpro-signed.
- Image types, encoded size, decoded dimensions, and pixel counts are limited.
- Remote media is not treated as trusted code or launch authorization.
- Diagnostic logs are size-limited, rotated, and sanitized to prevent control-character or log-forging attacks.

## Security limitations

### Passive carts can be cloned

An ordinary SSD, USB drive, SD card, CD, or DVD has no protected processor capable of proving that it is a unique physical object. A bit-for-bit clone may reproduce its files and identifiers. CLC can verify approved contents and behavior, but cannot provide strong anti-cloning without additional secure hardware.

A clone must still be unable to introduce new commands, launch targets, or modified executables without failing integrity verification.

### BadUSB and malicious hardware

A USB device can impersonate another hardware class, such as a keyboard or network adapter. This occurs below CLC and cannot be prevented by an application after the device is connected. Cart Launch Host verification protects the CLC launch path; it does not certify USB firmware or the physical device.

### Same-user compromise

Malware already running as the same operating-system user may be able to interfere with user-owned files and processes. The host uses operating-system permissions, protected secrets, integrity checks, and restricted local communication to reduce this risk, but it is not a replacement for operating-system security or endpoint protection.

## Reporting a vulnerability

Please do not open a public issue for a suspected security vulnerability.

Use GitHub's private vulnerability reporting feature for this repository when available. Include:

- a clear description of the issue;
- steps to reproduce;
- the affected version or commit;
- the operating system and relevant filesystem;
- potential impact;
- proof-of-concept files only when safe to share privately;
- any suggested mitigation.

Please do not include real credentials, personal paths, account identifiers, private game-library data, or harmful payloads beyond what is necessary to demonstrate the issue.

Reports will be reviewed and acknowledged after triage. Confirmed issues are prioritized based on severity, exploitability, required user interaction, and affected users. Public disclosure should wait until a fix or practical mitigation is available.

## Security testing expectations

Physical Cart support will not be considered ready for automatic launch until it has automated coverage for:

- path traversal and path canonicalization;
- symbolic-link, junction, reparse-point, and mount-point escape attempts;
- malformed, oversized, duplicated, and deeply nested manifest data;
- runtime, dependency, configuration, and launch-target replacement;
- verification-to-launch race handling;
- command and argument injection;
- untrusted environment variables;
- local IPC authorization and message bounds;
- cart removal during verification, staging, launch, and configuration writes;
- downgrade and unsupported security-version behavior;
- trust revocation, host repair, and complete uninstallation.

Manifest parsing should also receive fuzz testing, and the completed automatic-launch design should receive an independent security review before being enabled by default.

The transactional updater foundation and its current fail-closed signing status are documented in [Updater Security](Documentation/Updater-Security.md).
