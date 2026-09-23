# Emulator catalog version 2 and registry version 1

## Scope

Core owns the versioned in-memory contracts, strict JSON serialization, portable-path validation, and registry persistence. The first emulator-specific entry and release-discovery adapter are documented in Emulator-Companion-PPSSPP.md. There is no launch integration or automatic cart migration. Catalog source metadata is descriptive only. The read-only library UI consumes these contracts.

## Portable layout

Retain the existing CLC layout: Emulators/<Windows|Linux>/<EmulatorFolder>/<Executable>.
The folder is a case-preserving disk name, not necessarily the catalog ID. Executables may reside in subfolders. EmulatorPathContract.ExecutablePath generates paths without creating directories.

Paths are relative to the portable media root, use forward slashes, and retain exact casing. Rooted paths, backslashes, parent/dot segments, empty segments, Windows reserved device names, control characters, Windows-invalid characters, and trailing spaces/dots are rejected. The platform directory must match the installation's platform.

EmulatorPathContract.Resolve combines a validated path with the current media root, rejecting existing symbolic links/reparse points along that path. It neither requires the executable to exist nor proves launch readiness. It is not protection against concurrent hostile filesystem changes; a future installer must validate its own operations.

Existing Roms and Emulators/Shared folders, including BIOS, remain unchanged. The previously proposed emulator-first layout and new Bios/Firmware roots are not introduced implicitly.

## JSON contracts

JSON uses camelCase property names; property names are case-sensitive. schemaVersion and the top-level collection are required. The catalog accepts versions 1 and 2, upgrading version 1 in memory; catalog writers emit version 2. The registry remains at version 1. Unknown properties and numeric platform values are rejected. Null entries, duplicate identities, invalid IDs, and invalid paths are rejected.

IDs begin with a lowercase ASCII letter or digit and contain only lowercase letters, digits, hyphens, underscores, or dots. Display names and on-disk names retain their original casing. Versions are opaque nonblank strings; no semantic-version assumptions are made.

Empty catalog:
~~~json
{
  "schemaVersion": 2,
  "emulators": []
}
~~~

Each catalog entry requires id and displayName. systemIds and releaseChannels default to empty arrays; system IDs and channel IDs must be unique within an entry. Channels require id and displayName and are project-defined, not a global Stable/Nightly enum.

Empty registry:
~~~json
{
  "schemaVersion": 1,
  "installations": []
}
~~~

Each installation requires emulatorId, platform ("windows" or "linux"), and executableRelativePath. The pair (emulatorId, platform) is unique. Optional installedVersion, installedChannelId, and installedAt may be null when unknown. installedAt uses the standard JSON DateTimeOffset representation.

The registry is independent of catalog availability: an installed entry survives removal from a later catalog. Channel-to-catalog and system compatibility checks belong to the future management service. Branding, attribution, and release-channel metadata are defined in Emulator-Companion-Catalog-Metadata.md. Desired-channel persistence, cached update status, and skip decisions remain future work. Contract changes require an explicit schema/migration decision.

## Persistence

EmulatorRegistryStore takes a media root and writes only Config/emulator-registry.json, a sibling writer lock, and temporary files. LoadAsync returns an empty version-1 registry only when the file or parent folder is absent and creates no folders. Malformed, inaccessible, or unsupported data raises an error.

UpsertAsync and RemoveAsync acquire an exclusive sibling .lock file across the complete read/modify/write operation. Contention raises IOException; the caller can retry. Reads may continue using the previous complete snapshot. Updates preserve other installation records and sort by emulator ID/platform for stable output.

Writes use a unique sibling temporary file, flush its contents, and replace the destination by a same-directory move. Cancellation before replacement leaves the old file intact; temporary files from handled failures are cleaned up. The small lock file remains after release to avoid lock-file deletion races. There is no claim of power-loss durability on every removable filesystem and no automatic recovery of corrupt registries.

IEmulatorRegistryStore exposes load/upsert/remove to future consumers without giving the UI direct filesystem ownership. Existing CLC services do not consume this registry yet.

## Library integration

EmulatorCatalogSource now loads a local catalog through the strict versioned serializer. EmulatorLibraryService joins it with the registry without writing either source. It keeps both platform installations, retains installed IDs absent from the catalog, and includes catalog entries with no installation. Rows are sorted by display name, ID, and platform. Expected read/validation errors are reported per source; a failed registry means unknown installation status, never a claim that nothing is installed. Cancellation propagates.

Emulator Companion copies Catalog/emulators.json beside its executable on build/publish. The catalog contains the PPSSPP entry; it can still be empty. The media root is discovered using CLC's existing rules through DiscoverReadOnly; ordinary Discover retains its folder-creation behavior for existing consumers.

Nested physical-cart layouts keep CLC state under `Cart/Config` while emulator binaries remain in the media-root `Emulators` folder. `EmulatorStorageLayout` resolves these independently: a flat release/development layout uses one root, while a `Cart` folder with sibling `Emulators` or `Roms` uses the parent as its media root. Emulator Companion also accepts the same explicit `--cart-root <absolute Cart folder>` form used by CLC for controlled development and Cart Monitor launches.

The Companion window loads on opening and supports manual refresh, loading, empty, and partial-error states. Its native Avalonia TableView shows name, platform, recorded version, channel, and installation status using compiled bindings and fixed-height rows. Recording an installation does not confirm executable presence, BIOS, configuration, or launch readiness. No polling or background network services run.

Validation includes catalog/registry joins and partial failures, read-only root discovery, view-model refresh/error/cancellation behavior, and a Windows off-screen render check at 960x640 and 480x320. A 2,000-entry table realized 9 rows at the desktop size and successfully materialized its last row after scrolling. Linux runtime behavior has not yet been exercised.

Next: expose release details and explicit channel selection, then implement verified package downloading and installation. The metadata contract and update policy are defined in Emulator-Companion-Catalog-Metadata.md. Installation, downloads, gamepad input integration, and launch-time prompts remain separate implementation steps.
