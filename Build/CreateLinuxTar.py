from __future__ import annotations

import pathlib
import sys
import tarfile


def normalized_permissions(info: tarfile.TarInfo) -> tarfile.TarInfo:
    path = pathlib.PurePosixPath(info.name)
    if info.isdir():
        info.mode = 0o755
    elif path.name in {"Start Cart Launch Companion.sh", "Game Configurator.sh"}:
        info.mode = 0o755
    elif path.name == "CartLaunchCompanion.Updater" or (
        "System/Linux-x64" in path.as_posix()
        and path.name
        in {
            "CartLaunchCompanion.Desktop",
            "CartLaunchCompanion.Configurator",
            "createdump",
        }
    ):
        info.mode = 0o755
    elif path.name in {"CartLaunchCompanion.Host", "CartLaunchCompanion.HostCleanup"}:
        info.mode = 0o755
    else:
        info.mode = 0o644
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    return info


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit("usage: CreateLinuxTar.py <source-folder> <archive.tar.gz>")

    source = pathlib.Path(sys.argv[1]).resolve()
    archive = pathlib.Path(sys.argv[2]).resolve()
    archive.parent.mkdir(parents=True, exist_ok=True)

    with tarfile.open(archive, "w:gz", format=tarfile.PAX_FORMAT) as output:
        output.add(source, arcname=source.name, filter=normalized_permissions)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
