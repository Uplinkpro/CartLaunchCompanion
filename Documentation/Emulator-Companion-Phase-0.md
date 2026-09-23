# Emulator Companion foundation

- Source/CartLaunchCompanion.EmulatorCompanion is a separate .NET 10 / Avalonia 12.1 desktop executable for Windows and Linux. It references Core and reuses the existing CLC icon, Inter font, and Configurator palette. Its shell has no management behavior.
- Source/CartLaunchCompanion.Core/Emulators contains shared catalog, release-channel, and installation models. Core already provides platform detection and portable-path services, so no additional shared library is needed.
- Catalog identity and installation state are separate. Channels use project-defined IDs. Installation paths are relative to the media root; these models alone do not validate or resolve paths.
- Existing launcher/configurator behavior, launch presets, portable layout, and update mechanisms remain unchanged. The Windows executable is included in Windows-capable release bundles with a root-level launcher; Linux packaging remains deferred pending host validation.
- These are in-memory foundation models, not a finalized persisted schema. Subsequent registry storage and catalog metadata work is documented below; adapters and input navigation remain future work.

Follow-up: version-1 catalog/registry contracts and persistence are now defined in Emulator-Companion-Registry.md. The existing Emulators/<Windows|Linux>/<Emulator> layout is retained; no carts are migrated. The read-only catalog/registry library service and Companion library UI are now connected. See Emulator-Companion-Registry.md for current library behavior and Emulator-Companion-Catalog-Metadata.md for catalog version 2, attribution/branding, and update-channel policy.
