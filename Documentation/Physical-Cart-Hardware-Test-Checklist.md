# Physical Cart Hardware Test Checklist

Use a disposable removable drive with no irreplaceable files. Keep automatic launch disabled until the manual tests pass. Record the CLC commit, operating-system version, filesystem, connection type, and drive model for every run.

## Common preparation

- [ ] Format the test media using the filesystem normally recommended by the target operating system.
- [ ] Copy the expected `Cart`, `Games`, `Emulators`, and `Roms` structure.
- [ ] Confirm `cartlaunch.cartridge.json` exists only at the media root.
- [ ] Install Cart Launch Host for the current user and confirm every displayed installation/data path.
- [ ] Confirm an ordinary untrusted drive causes no prompt and launches nothing.
- [ ] Trust the test cart manually; leave automatic launch disabled.
- [ ] Reconnect and confirm detection completes without recursively scanning game data.
- [ ] Manually launch and verify CLC runs from the local Host session while loading data from the cart.

## Windows 11

- [ ] Test NTFS and exFAT on USB flash media and a USB SSD where available.
- [ ] Test both current-user and all-users Host installation; verify trust remains per user.
- [ ] Enable automatic launch, reinsert once, and confirm exactly one CLC instance starts.
- [ ] Start a second Host manually and confirm it exits without launching a duplicate session.
- [ ] Remove the cart during verification and confirm no launcher starts and no session remains.
- [ ] Revoke trust during verification and confirm final authorization blocks launch.
- [ ] Modify one staged runtime byte before launch in a controlled test build and confirm launch is rejected.
- [ ] Use **Eject Cart** and confirm Windows reports the device safe to remove.
- [ ] Hold a file open from another program, retry eject, and confirm CLC reports a busy-drive failure without claiming success.
- [ ] Remove the drive before eject completes and confirm the Host reports it already removed.
- [ ] Repair the Host and confirm trust, settings, and logs remain unchanged.
- [ ] Exercise uninstall retention options and confirm only selected data remains.

## SteamOS, Bazzite, ChimeraOS, and CachyOS

- [ ] Record the distribution image/version, desktop/game mode, and `udisks2` version.
- [ ] Test the filesystem recommended by that distribution; include exFAT when cross-platform carts are required.
- [ ] Confirm the user-installed Host starts without root, a system service, or a system-wide `udev` rule.
- [ ] Confirm the cart is discovered beneath the distribution's actual user mount path.
- [ ] Enable automatic launch, reinsert once, and confirm exactly one CLC instance starts.
- [ ] Confirm a second Host process cannot acquire the per-user instance lock.
- [ ] Remove the cart during verification and confirm staging is cancelled and cleaned.
- [ ] Revoke trust during staging and confirm the final gate blocks process creation.
- [ ] Use **Eject Cart** and confirm the exact block device is unmounted and powered off through `udisksctl`.
- [ ] Mount two removable drives simultaneously and confirm only the active cart is ejected.
- [ ] Hold a cart file open, verify eject fails visibly, close it, and verify retry succeeds.
- [ ] Repeat from desktop mode and game mode where Host startup behavior differs.

## Evidence and release gate

- [ ] Save sanitized Host audit logs; confirm they contain no paths, credentials, or raw cart IDs.
- [ ] Record detection-to-launch time; target less than 10 seconds on supported hardware.
- [ ] Confirm no incomplete files remain under Host `Sessions` or cart `*.tmp`/`*.download` paths.
- [ ] Confirm ordinary launching still works offline with the Host uninstalled.
- [ ] Do not enable automatic launch by default until every applicable item passes on Windows and at least SteamOS plus one additional supported gaming distribution.
