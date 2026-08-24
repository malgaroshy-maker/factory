#!/usr/bin/env python3
"""Install a Godot .NET editor and its export templates, headlessly.

    python tools/packaging/install_godot.py --version 4.7.2
    python tools/packaging/install_godot.py --print-path --version 4.7.2

Exists because the editor's "Manage Export Templates > Download" needs a GUI,
and a release CI job does not have one. Both downloads land where Godot itself
looks for them, so an export afterwards needs no further configuration.
"""
from __future__ import annotations

import argparse
import os
import platform
import shutil
import stat
import sys
import urllib.request
import zipfile
from pathlib import Path

RELEASES = "https://github.com/godotengine/godot/releases/download"


def editor_archive(version: str) -> tuple[str, str]:
    """(archive name, executable name inside it) for this platform."""
    if platform.system() == "Windows":
        return (f"Godot_v{version}-stable_mono_win64.zip",
                f"Godot_v{version}-stable_mono_win64_console.exe")
    return (f"Godot_v{version}-stable_mono_linux_x86_64.zip",
            f"Godot_v{version}-stable_mono_linux.x86_64")


def godot_home() -> Path:
    return Path.home() / "godot"


def templates_dir() -> Path:
    if platform.system() == "Windows":
        return Path(os.environ["APPDATA"]) / "Godot" / "export_templates"
    return Path.home() / ".local" / "share" / "godot" / "export_templates"


def find_editor(version: str) -> Path | None:
    _, exe = editor_archive(version)
    return next(godot_home().rglob(exe), None)


def download(url: str, dest: Path) -> Path:
    dest.parent.mkdir(parents=True, exist_ok=True)
    print(f"  downloading {url}")
    with urllib.request.urlopen(url) as response, open(dest, "wb") as out:
        shutil.copyfileobj(response, out)
    print(f"  {dest.name} ({dest.stat().st_size / 1e6:.0f} MB)")
    return dest


def install_editor(version: str) -> Path:
    archive, exe = editor_archive(version)
    home = godot_home()
    home.mkdir(parents=True, exist_ok=True)
    zip_path = download(f"{RELEASES}/{version}-stable/{archive}", home / archive)
    with zipfile.ZipFile(zip_path) as z:
        z.extractall(home)
    zip_path.unlink()

    found = find_editor(version)
    if found is None:
        raise SystemExit(f"{archive} did not contain {exe} -- archive layout changed")
    found.chmod(found.stat().st_mode | stat.S_IEXEC)
    return found


def install_templates(version: str) -> Path:
    """Unpack the .tpz flat into <templates>/<version.txt contents>/.

    The archive nests everything under templates/, and Godot wants the files
    directly in a directory named by the version string inside it -- not by the
    version you asked for, which is why version.txt is read rather than assumed.
    """
    tpz = download(f"{RELEASES}/{version}-stable/Godot_v{version}-stable_mono_export_templates.tpz",
                   godot_home() / "templates.tpz")
    with zipfile.ZipFile(tpz) as z:
        label = z.read("templates/version.txt").decode().strip()
        dest = templates_dir() / label
        dest.mkdir(parents=True, exist_ok=True)
        for name in z.namelist():
            if name.endswith("/"):
                continue
            with z.open(name) as src, open(dest / Path(name).name, "wb") as out:
                shutil.copyfileobj(src, out)
    tpz.unlink()
    print(f"  templates -> {dest}")
    return dest


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", default="4.7.2")
    parser.add_argument("--print-path", action="store_true",
                        help="print the editor path and exit, installing nothing")
    args = parser.parse_args(argv)

    if args.print_path:
        found = find_editor(args.version)
        if found is None:
            print(f"no Godot {args.version} under {godot_home()}", file=sys.stderr)
            return 1
        print(found)
        return 0

    print(f"Godot {args.version} (.NET)")
    editor = find_editor(args.version) or install_editor(args.version)
    print(f"  editor -> {editor}")
    install_templates(args.version)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
