# PPSSPP: first release-discovery adapter

## Verified upstream

This integration targets [hrydgard/ppsspp](https://github.com/hrydgard/ppsspp), linked from the [official PPSSPP website](https://www.ppsspp.org/). Verified on 2026-09-19:

- The latest stable GitHub release was [v1.20.4](https://github.com/hrydgard/ppsspp/releases/tag/v1.20.4).
- Portable Windows x64 ZIP and Linux x86-64 AppImage packages are available on the official GitHub release, each with an upstream SHA-256 digest.
- The [README](https://github.com/hrydgard/ppsspp/blob/master/README.md) identifies Henrik Rydgard as creator and GPL 2.0 or later as the project license. The [license file](https://github.com/hrydgard/ppsspp/blob/master/LICENSE.TXT) also contains third-party notices.
- [Icon provenance](https://github.com/hrydgard/ppsspp/blob/master/icons/icon-512.svg) is recorded. No artwork or emulator code is bundled at this stage.
- [Development builds](https://www.ppsspp.org/devbuilds/) use a separate distribution path. Only the stable channel is implemented here.

## Catalog and UI

The bundled entry has ID ppsspp, display name PPSSPP, system ID psp, official website/repository/license/credits links, and a stable channel supporting Windows/Linux. Platform architecture is restricted to x64 by this first adapter.

The entry appears in the existing library table. The displayed installation state comes only from the registry; a catalog entry is not an installed emulator. Opening or refreshing the library makes no release-discovery request. Existing CLC launch presets, cart layouts, and installed paths remain unchanged.

Select PPSSPP in the library and choose Releases to open the release-details window. Choose Windows x64 or Linux x64 and the Stable channel, then select Check for release. Linux validation is focused on SteamOS, Bazzite, ChimeraOS, and CachyOS. Opening the window does not make a network request. A successful check displays the version, package name, size, publication date, and checksum availability, with a link to the official release notes. Changing the selection clears previous results and cancels an outstanding request; closing the window also cancels it. Connection errors, rate limits, invalid metadata, and missing packages have explicit messages and permit retrying.

## Discovery

PpssppReleaseAdapter implements IEmulatorReleaseAdapter and reads:

https://api.github.com/repos/hrydgard/ppsspp/releases/latest

The adapter follows GitHub's latest stable release, validates a numeric v-prefixed version tag, and derives exact package names from that tag:

| Target | Asset pattern | Format |
| --- | --- | --- |
| Windows x64 | PPSSPP-<tag>-Windows-x64.zip | ZIP |
| Linux x64 | PPSSPP-<tag>-anylinux-x86_64.AppImage | AppImage |

It does not hardcode v1.20.4. Drafts/prereleases, ARM builds, Android/iOS/macOS, source archives, installers, and .zsync sidecars are not selected. Duplicate matching packages fail instead of choosing arbitrarily. Missing platform packages return no candidate; older versions are not silently substituted.

Requests have a 15-second deadline covering headers and body, a 1 MiB metadata limit, and no automatic redirects or credentials. HTTP errors, unavailable repositories, rate limits, malformed metadata, and invalid source/asset URLs fail explicitly. Download URLs must match the exact upstream repository, release tag, and asset filename. Package sizes must be positive and no larger than 2 GiB.

The result includes version, platform/architecture, publication time, release page, package URL/name/size/format, optional upstream SHA-256, and an opaque revision identity. The identity combines repository, tag, release ID, asset ID, asset update time, size, and digest so an asset replacement can be distinguished from the original.

If the API omits a digest, Sha256 is null; the revision identifies metadata only and does not prove byte integrity. The installer refuses packages without a publisher digest and verifies the downloaded bytes before extracting them.

The adapter does not download, extract, install, check installed versions, or modify the registry. It does not yet check AppImage runtime requirements or executable permissions. A new upstream naming convention should require an explicit adapter update rather than fuzzy filename matching.

## Validation

Offline tests cover exact architecture/package matching, future stable versions, wrong-version assets, drafts/prereleases, missing/duplicate assets, HTTP failures, JSON validation, package URLs/digests/sizes, bounded metadata, cancellation, and changed asset revisions. Companion tests validate the shipped catalog, manual-only checks, platform selection, cancellation, stale-result suppression, retryable errors, and adapter disposal. Off-screen rendering checks cover the release window at normal and compact sizes and preserve library row virtualization.

The production adapter was also checked against live metadata for both Windows x64 and Linux x64 and returned v1.20.4 with SHA-256 digests. A later isolated installer smoke test downloaded and verified both official packages and registered their installations in a scratch media root. No emulator or game was executed.

## Portable installation and updates

The release window now offers **Download and install** for a discovered package with a publisher SHA-256 checksum. The target media root is displayed before installation. Downloads stream to a unique sibling staging folder with a 15-minute deadline, an exact byte count, and a checksum comparison before extraction. HTTPS redirects are limited to GitHub and its release asset hosts.

Managed launches keep Windows and Linux configuration and controller files in their respective installation folders while both builds use `Emulators/Shared/MemorySticks/PPSSPP` for saved games, save states, and texture packs. CLC and Emulator Companion add PPSSPP's official `--memstick`, `--config`, and `--controlconfig` arguments at process launch, calculating absolute paths from the cart's current mount location. Existing local memory-stick content is conflict checked, copied into the shared memory stick, and retained in the migration backup area. No symbolic links, administrator launch, Windows Developer Mode, or stored drive letter are required.

Windows ZIP extraction rejects traversal, nonportable names, duplicate paths, links, excessive entry counts, and oversized expanded data. The expected executable is PPSSPPWindows64.exe; installed.txt is rejected to retain [PPSSPP portable storage behavior](https://www.ppsspp.org/docs/getting-started/save-data-and-storage-windows/). Linux packages use the stable name PPSSPP.AppImage with adjacent .home and .config directories for [AppImage portable mode](https://docs.appimage.org/user-guide/portable-mode.html). Native Linux installation sets executable permissions. Preparing a Linux package on Windows requires enabling executable permission on Linux before launching; runtime compatibility and actual save locations have not yet been tested on Linux.

Managed stable PPSSPP installations can now be updated from the release window. The action changes to **Download and update** and shows the installed and offered versions. Numeric version comparison suppresses same-version installs and downgrades. Unknown versions, missing executables, nonstandard paths, and unregistered folders are blocked rather than adopted or overwritten. Republished assets under the same version are not yet offered as updates.

The update stages the verified package and copies existing data into it. Windows memstick and Linux PPSSPP.AppImage.home/.config directories are preserved; release ZIPs containing these reserved roots are rejected. Custom files and installed.txt are retained when absent from the new package. Files supplied by the new package replace matching application files, and obsolete application files not present in the new package are conservatively retained. External save locations are untouched. Content snapshots detect files added, removed, or changed during preparation. PPSSPP must be closed: running-process checks run before preparation and publication. No process is terminated.

A per-platform lock serializes Companion installation operations. Before folder replacement, a flushed, atomically published journal records the staging folder, old and new installation records, and the prepared payload digest. The existing folder moves to a backup inside staging, the prepared folder takes its place, and the atomic registry replacement commits the version. Cancellation is honored during download and preparation. Publication completes or rolls back without cancellation.

Companion checks for journals before loading the library on startup/refresh and before installing. If the registry contains the old record, recovery restores the backup. If it contains the new record, recovery keeps the new installation and finishes cleanup. Both first-install and update interruptions are covered, and recovery can itself be interrupted and retried. Changed or corrupt records, missing backup files, and files changed after the interruption stop recovery with a visible error and retain the available folders. In particular, new saves written after a crash are never silently discarded. Such conflicts require manual recovery.

Cleanup targets only the unique staging directory and refuses links. Failed cleanup, crashes before journal publication, or crashes during cleanup can leave inert staging folders. No broad orphan-directory sweep is performed. These checks cover application/process interruption; they do not guarantee recovery from storage corruption or all power-loss ordering on every filesystem, and are not a boundary against hostile concurrent filesystem changes. CLC launch behavior is unchanged, so users must keep PPSSPP closed during updates and recovery.

Tests simulate interruption after journal publication, old-folder backup, new-folder publication, registry commit, and during rollback itself. They also cover save preservation on Windows/Linux, same-version/downgrade suppression, registry failure, cancellation, save changes during preparation and after a crash, corrupt/conflicting recovery records, and the corresponding UI actions.

## Launch-time update choices

CLC now checks before starting a game whose Local/Custom launch target exactly matches the managed PPSSPP executable on its current platform. It does not infer emulator identity from game names, extensions, Steam targets, wrappers, or arbitrary executable names. Other launch targets retain their existing behavior. Launcher uses the same bundled catalog as Emulator Companion.

Successful release results, including no matching release, are cached for the selected interval (24 hours by default) in Config/ppsspp-updates-<platform>.json. Refresh requests have a two-second deadline; network failures/timeouts back off for 15 minutes and allow the existing installation to launch. The timeout and failure backoff remain centralized in EmulatorLaunchUpdateOptions. The check interval and launch-check toggle are saved user preferences. Cached packages are validated again before offering an update. Cache writes use a per-platform lock and atomic replacement; a failed write can retain a session-only cache.

The prompt offers **Update**, **Skip for now**, and **Skip this version**. Update runs the existing verified installer, then resumes the same game request after successful completion. Skip for now launches without persisting a preference and can prompt again on the next launch. Skip this version persists the exact version for the stable channel and selected platform, including across Launcher restarts; a later version is eligible again. Same-version republished assets remain skipped. Failure to save that choice is shown rather than silently claiming success.

Keyboard/controller Confirm updates, Back skips for now, and Trailer skips the version. The prompt displays the active input labels. While updating, Back and Cancel update request cancellation and return to the choices; failed updates also leave the prompt open. Closing the Launcher cancels the pending decision and cannot start a game afterward. Mouse/touch buttons invoke the same decisions.

Release-service failures do not imply that PPSSPP is up to date. Recovery failures are different: an interrupted or inconsistent installation blocks launch until recovery succeeds. A final shared installation lock covers process creation to prevent an update from publishing files concurrently. Existing managed lock files can be opened read-only, and unregistered installations do not acquire a new lock.

Tests cover cache expiry, negative caching, failure backoff, timeout, persistent skips, poisoned cache entries, launch/update locking, read-only locks, update failures, controller decisions, cancellation, and exactly-once launch continuation. Off-screen rendering covers normal and compact prompt layouts. No emulator was executed for these checks.

## Update settings

Emulator Companion now has an **Update settings** dialog. Preferences apply to the current media-root library on both platforms and are stored in Config/emulator-update-preferences.json (schema 1). Defaults retain the existing behavior: launch checks enabled, once a day. Users can disable launch checks or select every 6 hours, once a day, or once a week. Changes are saved only by **Save preferences**; closing discards unsaved edits. Launcher reloads preferences at its next applicable game launch, without restarting. Disabling checks suppresses release requests and prompts while preserving mandatory installation recovery and manual Companion release checks.

The dialog shows skipped PPSSPP versions independently for Windows and Linux. **Clear Windows skip** and **Clear Linux skip** immediately remove only the selected platform's skip. Cached release metadata, freshness, retry timing, other-platform choices, and unsaved preference edits are preserved. The existing per-platform cache lock coordinates clearing with Launcher checks and writes; Launcher reads the changed skip state even when it already has a session cache.

Settings and cache access now share the existing serialized cache shape through a small store, with bounded reads, atomic writes, and writer locks. Unsupported/corrupt preferences are not silently overwritten. The dialog displays save/clear failures and retains edits for retry. Unreadable preferences suppress optional launch checks while allowing ordinary launch/recovery behavior.

Tests cover default read-only loading, persistence, all supported intervals, enable/disable without restart, clearing without another network request, platform isolation, busy writers, corrupt preferences, cancelled saves, and the dialog's independent save/clear behavior. Normal and compact dialog rendering and library virtualization were also checked.

## Both platforms

The release screen's Platform menu now includes **Both** alongside Windows x64 and Linux x64. Both checks the selected channel for each platform and shows separate package details, availability/errors, installation status, and release-note links. The option requires a channel supporting both platforms.

**Download available builds** installs or updates each eligible build sequentially. Already-current or blocked builds are not replaced. Each platform keeps its own verified installation and registry commit: if one fails or the operation is cancelled, a completed build remains installed. Retrying without rechecking attempts only unfinished eligible builds. Changing the platform/channel clears both results and cancels pending discovery.

## About and attribution

The optional **About** window stays in the library header outside the install/update flow. It shows the Emulator Companion version, Cart Launch Companion project/release/support links, and third-party attribution generated from the loaded catalog. PPSSPP contributes its official website, GitHub repository, license, credits, and branding-source links through the existing catalog metadata.

## Windows validation

The complete production Windows install path was exercised against the live official PPSSPP release on 2026-09-20. Release discovery selected v1.20.4, the installer downloaded the 20,390,334-byte Windows x64 ZIP, verified its publisher SHA-256 digest, extracted the portable package, registered the installation, and then reported the version as current. The installed executable was 19,818,496 bytes and `installed.txt` was absent. PPSSPP itself was not launched during this validation.

The physical test cart was then validated with its nested layout: CLC state under `H:\Cart` and emulator content under `H:\Emulators`. Its existing PPSSPP v1.20.4 executable was validated and registered without replacing files; the `memstick` settings/save tree was retained. No root-level `H:\Config` folder was created. A PSP game launched and ran successfully through the test-cart configuration. Save persistence across closing and relaunching the game remains a separate validation step.

## Next

Confirm that a PSP save survives closing and relaunching the game, then exercise the launch-time update prompt when a newer stable release becomes available. Linux install/update validation remains deferred until a Linux host is available.
