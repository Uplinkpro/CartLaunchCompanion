# Emulator catalog metadata: version 2

## Versioning and scope

Catalog schema 2 adds optional branding, attribution, and channel metadata. Readers accept version 1 and upgrade it in memory to version 2 without rewriting the source file. Writers emit version 2 only. Version-1 files retain their original strict field set; extended fields require version 2. Unsupported versions fail visibly.

Registry schema remains version 1. No installed paths, preferred channels, or user decisions are changed. The bundled catalog now includes the PPSSPP project; see Emulator-Companion-PPSSPP.md. This step defines metadata and policy, not release discovery, downloading, installation, artwork rendering, or an About screen.

## Branding and attribution

Each emulator may have a branding object:

- iconRelativePath, logoRelativePath, bannerRelativePath: optional PNG/SVG paths under Assets/Emulators/<Folder>/ relative to the directory containing the catalog. They are not relative to the cart root. Preserve case and use forward slashes.
- themeColor: optional #RRGGBB accent hint.
- usesOfficialBranding: false unless the catalog explicitly identifies official artwork; true requires attribution.brandingSourceUrl.

Each emulator may have an attribution object:

- websiteUrl, repositoryUrl: project home and source repository. Repository links are not restricted to GitHub.
- license, licenseUrl: optional license label/expression and supporting page. Do not infer a license if unknown.
- credits: optional plain text, including contributor attribution.
- brandingSourceUrl: provenance page for the referenced artwork.

Links must be absolute HTTPS URLs without embedded credentials. Validation checks syntax, not availability, ownership, license terms, or permission to redistribute. Unknown metadata is omitted/null; blank strings are rejected. Metadata does not fetch assets or open links.

Future presentation should use icon, then logo, then text initials when artwork is missing/unusable. Accent hints must not replace readable CLC text/focus styles. Attribution belongs in an optional, out-of-the-way About view generated from the catalog.

## Channels and release sources

Channel IDs and display names remain project-specific. An emulator may declare defaultChannelId, which must reference one of its channels. This is a catalog recommendation, not a persisted user choice.

Channels add:

- description: optional plain text explaining the upstream channel.
- isStable: explicit catalog classification; default false means stability is not asserted.
- supportedPlatforms: unique windows/linux values. Empty means unknown support, not universal support.
- source: optional release-discovery metadata. A source requires explicit supported platforms.

A source contains required kind and url. kind is gitHubReleases or projectWebsite:

- gitHubReleases: url identifies https://github.com/<owner>/<repository>. prereleases is any (default), exclude, or only. Optional tag is an exact release tag; tagPrefix is a case-sensitive tag prefix. They are mutually exclusive. All specified filters apply together. Drafts are never install candidates. The adapter must discover actual upstream releases before claiming a channel exists.
- projectWebsite: url points to an official project release page. GitHub filters are forbidden. A future project adapter must define how to interpret the page; this metadata does not authorize generic scraping or installation.

These sources are not asset URLs. No shell commands, executable arguments, regex filters, credentials, or arbitrary scripts are stored here. Asset architecture, archive format, integrity checks, and version ordering must be defined by the first real adapter.

A branch named main is not automatically a release channel. Moving tags such as nightly cannot be treated as immutable version identifiers.

## Update policy for the next implementation

1. User-selected channels take precedence per emulator/platform. Never silently substitute another channel when the preference is unavailable or unsupported.
2. Without an explicit choice, prefer a confirmed stable channel supporting the target platform. If several qualify, use the declared default only if it is among them; otherwise ask the user to choose.
3. If no stable channel qualifies, offer the declared supported default for explicit selection. Never silently opt the user into preview/nightly builds.
4. installedChannelId describes what is installed. A future preferredChannelId must be stored separately. Changing preference alone does not alter an installation or imply that a downgrade is safe.
5. Cache update checks (initial freshness target: 24 hours). Offline or failed checks must not prevent launching an existing usable installation. A timestamp for a failed request must not masquerade as a successful fresh result.
6. Present Update Now, Skip for Now, and Skip This Version. Skip for Now dismisses this launch prompt without changing the installed version. Skip This Version applies only to that emulator, platform, channel, and immutable release identity.
7. For moving tags, include upstream release/asset revision information in that identity; a display tag alone is insufficient. Different channels or later revisions must not inherit an unrelated skip.
8. Release discovery, downloaded-asset verification, transactional installation, and rollback must be implemented before offering Update Now. Registry records alone do not prove launch readiness.

This policy is documented for implementation; it does not yet add preference persistence, update checks, prompts, or adapters.

## Next

The first catalog entry and bounded release-discovery adapter now target the PPSSPP project. See Emulator-Companion-PPSSPP.md for verified package selection, revision identity, and next steps. Preference persistence and update prompts remain future work.
