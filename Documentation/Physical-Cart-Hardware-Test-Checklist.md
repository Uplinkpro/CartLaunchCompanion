# Physical Cart Hardware Test Checklist

Use a disposable removable drive with no irreplaceable files. Keep automatic launch disabled until the manual tests pass. Record the CLC commit, operating-system version, filesystem, connection type, and drive model for every run.

## Recorded validation runs

| Date | CLC version | Environment | Media | Result | Notes |
| --- | --- | --- | --- | --- | --- |
| 2026-08-29 | 2.5.1 release candidate (`72b1764`) | Windows 11 Pro; current-user CLC-Cart Monitor | 500 GB NTFS GPT USB SSD, JMicron Tech bridge; GTA Collection test cart | Passed | Completed trust and auto-launch, busy-drive rejection with immediate progress feedback, retry, safe removal, physical reinsertion, protected relaunch, and keyboard/controller prompt validation. Windows and Linux cart runtimes were then hash-verified against the release candidate without replacing cart-specific content. |
| 2026-08-27 | 2.5.0 validation build | Windows 11 Pro 23H2, build 22631; current-user CLC-Cart Monitor | 500 GB NTFS GPT USB SSD, JMicron Tech bridge | Passed | Trusted cart safely ejected and disappeared from Windows, physical reinsertion was detected, the approved runtime verified, and exactly one protected local CLC session launched. Repair preserved trust and logs and restored Windows startup registration. Detection-to-launch measured approximately 19.3 seconds, so the sub-10-second performance target remains open. |

## Common preparation

- [ ] Format the test media using the filesystem normally recommended by the target operating system.
- [ ] Copy the expected `Cart`, `Games`, `Emulators`, and `Roms` structure.
- [ ] Confirm the identity exists only at `.cartlaunch/cartridge.json` beneath the media root and that `.cartlaunch` is hidden in the normal file-browser view.
- [ ] Install CLC-Cart Monitor for the current user and confirm every displayed installation/data path.
- [ ] Confirm an ordinary untrusted drive causes no prompt and launches nothing.
- [ ] Trust the test cart manually; leave automatic launch disabled.
- [ ] Reconnect and confirm detection completes without recursively scanning game data.
- [ ] Manually launch and verify CLC runs from the local Monitor session while loading data from the cart.

## Windows 11

- [ ] Test NTFS and exFAT on USB flash media and a USB SSD where available.
- [ ] Test both current-user and all-users Monitor installation; verify trust remains per user.
- [ ] Enable automatic launch, reinsert once, and confirm exactly one CLC instance starts.
- [ ] Start a second Monitor manually and confirm it exits without launching a duplicate session.
- [ ] Remove the cart during verification and confirm no launcher starts and no session remains.
- [ ] Revoke trust during verification and confirm final authorization blocks launch.
- [ ] Modify one staged runtime byte before launch in a controlled test build and confirm launch is rejected.
- [ ] Use **Eject Cart** and confirm Windows reports the device safe to remove.
- [ ] Hold a file open from another program, retry eject, and confirm CLC reports a busy-drive failure without claiming success.
- [ ] Remove the drive before eject completes and confirm the Monitor reports it already removed.
- [ ] Repair CLC-Cart Monitor and confirm trust, settings, and logs remain unchanged.
- [ ] Exercise uninstall retention options and confirm only selected data remains.

## SteamOS, Bazzite, ChimeraOS, and CachyOS

- [ ] Record the distribution image/version, desktop/game mode, and `udisks2` version.
- [ ] Test the filesystem recommended by that distribution; include exFAT when cross-platform carts are required.
- [ ] Confirm the user-installed Monitor starts without root, a system service, or a system-wide `udev` rule.
- [ ] Confirm the cart is discovered beneath the distribution's actual user mount path.
- [ ] Enable automatic launch, reinsert once, and confirm exactly one CLC instance starts.
- [ ] Confirm a second Monitor process cannot acquire the per-user instance lock.
- [ ] Remove the cart during verification and confirm staging is cancelled and cleaned.
- [ ] Revoke trust during staging and confirm the final gate blocks process creation.
- [ ] Use **Eject Cart** and confirm the exact block device is unmounted and powered off through `udisksctl`.
- [ ] Mount two removable drives simultaneously and confirm only the active cart is ejected.
- [ ] Hold a cart file open, verify eject fails visibly, close it, and verify retry succeeds.
- [ ] Repeat from desktop mode and game mode where Monitor startup behavior differs.

## Evidence and release gate

- [ ] Save sanitized Monitor audit logs; confirm they contain no paths, credentials, or raw cart IDs.
- [ ] Record detection-to-launch time; target less than 10 seconds on supported hardware.
- [ ] Confirm no incomplete files remain under Monitor `Sessions` or cart `*.tmp`/`*.download` paths.
- [ ] Confirm ordinary launching still works offline with CLC-Cart Monitor uninstalled.
- [ ] Do not enable automatic launch by default until every applicable item passes on Windows and at least SteamOS plus one additional supported gaming distribution.
