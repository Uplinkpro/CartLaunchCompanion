from __future__ import annotations

import pathlib
import sys
import zipfile


def mode_for(path: pathlib.Path, relative: pathlib.PurePosixPath) -> int:
    if path.is_dir():
        return 0o40755
    if relative.name in {"Start Cart Launch Companion.sh", "Game Configurator.sh"}:
        return 0o100755
    if relative.name == "CartLaunchCompanion.Updater" or (
        "System/Linux-x64" in relative.as_posix()
        and relative.name
        in {
            "CartLaunchCompanion.Desktop",
            "CartLaunchCompanion.Configurator",
            "createdump",
        }
    ):
        return 0o100755
    if relative.name in {"CartLaunchCompanion.Host", "CartLaunchCompanion.HostCleanup"}:
        return 0o100755
    return 0o100644


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit("usage: CreatePortableZip.py <source-folder> <archive.zip>")

    source = pathlib.Path(sys.argv[1]).resolve()
    archive = pathlib.Path(sys.argv[2]).resolve()
    archive.parent.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for path in [source, *source.rglob("*")]:
            relative = pathlib.PurePosixPath(source.name) / pathlib.PurePosixPath(
                path.relative_to(source).as_posix()
            )
            name = relative.as_posix() + ("/" if path.is_dir() else "")
            info = zipfile.ZipInfo.from_file(path, arcname=name)
            info.create_system = 3
            info.external_attr = mode_for(path, relative) << 16
            if path.is_dir():
                output.writestr(info, b"")
            else:
                with path.open("rb") as input_file:
                    output.writestr(info, input_file.read(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
