# Launcher ID and URI Guide

Steam is unusual: its public numeric App ID is easy to find and is normally enough to launch a game. Other storefronts often use installed-package identifiers, private catalog values, edition-specific offer IDs, or complete launch URIs. Copying an ID from an unverified web list can therefore open the wrong edition or fail silently.

The Game Configurator includes the same guidance under **Windows launch → Launcher ID guide**.

## Recommended workflow

1. Select the launcher actually used by that edition of the game.
2. Use the launcher to install or recognize the game on the target computer.
3. Prefer CLC's **Verify selected launcher** and file locators.
4. If the launcher can create a desktop shortcut, inspect that shortcut and preserve its complete URI.
5. Use a cart-local executable when the game supports direct launch.
6. Test the exact edition before trusting or distributing the cart.

On Windows, **Find installed match** automates steps 2–4. It searches local Steam and Epic manifests, installed Windows application AUMIDs, and launcher-created Start menu or desktop shortcuts. CLC shows the ranked candidates and changes nothing until the user confirms one. An executable is saved only when it is stored on the cart.

## Launcher cheat sheet

| Launcher | CLC expects | Where to find it | Example or format |
|---|---|---|---|
| Steam | Numeric Steam App ID | Number after `/app/` in the Steam store URL, or **Find on Steam** | Portal 2: `620`; `steam://rungameid/620` |
| Xbox / Microsoft Store | Installed AUMID | Inspect the game in `shell:AppsFolder` using Microsoft's AUMID instructions | `PackageFamilyName!ApplicationId` |
| Epic Games | Epic `AppName` | Installed Epic `.item` manifest under the ProgramData manifest folder | `com.epicgames.launcher://apps/{AppName}?action=launch&silent=true` |
| GOG Galaxy | Executable or complete URI | Locate the cart game executable or inspect a Galaxy-created shortcut | Product ID is reference metadata; it is not enough by itself in CLC |
| Ubisoft Connect | Numeric game ID or complete URI | Create a Ubisoft desktop shortcut and inspect its target | Black Flag: `565`; `uplay://launch/565/0` |
| Rockstar Games Launcher | Executable or complete URI | Locate the cart executable or inspect a known-working shortcut | GTA V may use `PlayGTAV.exe`; do not guess a copied game ID |
| Amazon Games | Executable or complete URI | Locate the cart executable or inspect an Amazon-created shortcut | Preserve the complete installed shortcut target |
| EA app | Executable or complete URI | Locate the cart executable or inspect an EA-created shortcut | Legacy Origin offer IDs are not assumed to work in the current EA app |
| Battle.net | Executable or complete URI | Locate the cart executable or inspect a Battle.net-created shortcut | Preserve the complete shortcut target; do not guess internal product codes |
| HoYoverse / HoYoPlay | Executable or complete URI | Locate the cart executable or inspect a launcher-created shortcut | Use the exact target produced for the installed game |
| itch.io | Executable and optional arguments | Locate the portable game executable on the cart | No storefront ID is required for a direct executable |
| Flash | Portable player executable and game arguments | Select the player from `Emulators` and the game file from `Games` or `Roms` | Treat the player like an emulator and keep both paths portable |

## Why CLC does not ship an “all games” ID list

- Publisher catalogs contain thousands of products, regional variants, demos, test applications, DLC, and retired editions.
- A store product identifier is not always the same identifier accepted by the installed launch protocol.
- Ubisoft, EA, Rockstar, and Amazon do not publish a stable, complete consumer launch-ID catalog comparable to Steam's App IDs.
- Installed manifests and shortcuts identify the edition the user actually owns and are therefore safer.

CLC maintains verified examples, while automatic local discovery handles the installed edition. Discovery is intentionally confirm-first because title matching alone cannot distinguish every remaster, test application, regional package, or store edition.

## Official references

- [Steam application documentation](https://partner.steamgames.com/doc/store/application)
- [Microsoft: Find the AUMID of an installed app](https://learn.microsoft.com/windows/configuration/store/find-aumid)
- [Epic Games Store support](https://www.epicgames.com/help/epic-games-store-c-202300000001639)
- [GOG support](https://support.gog.com/hc/en-us/categories/201400969)
- [Ubisoft connectivity and performance support](https://www.ubisoft.com/help/connectivity-and-performance)
- [Rockstar Games Launcher support](https://support.rockstargames.com/categories/200013306)
- [Amazon Games support](https://www.amazongames.com/en-us/support)
- [EA: Download and play games in the EA app](https://help.ea.com/en/articles/platforms/download-and-play-ea-app-games/)
