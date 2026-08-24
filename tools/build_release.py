#!/usr/bin/env python3
"""Build a downloadable FactoryForge release: engine, sidecar, examples, docs.

    python tools/build_release.py                 # this platform
    python tools/build_release.py --no-sidecar    # engine only, much faster
    python tools/build_release.py --skip-archive  # leave the staged tree

The point of a release is that someone can extract one archive and connect to a
PLC without installing Godot, the .NET SDK, or Python. That means three things
have to be true, and each is checked here rather than assumed:

* the engine is exported with its .NET assemblies (an export with no solution
  file produces a binary whose every script silently fails -- it looks fine),
* the sidecar is frozen, so no Python is needed on the target machine,
* the engine can find that frozen sidecar (SidecarLocator looks beside the
  binary first, which is exactly where this puts it).
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ENGINE = ROOT / "engine"
DIST = ROOT / "dist"

sys.path.insert(0, str(ROOT))
from run import find_godot  # noqa: E402

#: Preset name in engine/export_presets.cfg -> (staging dir, binary name).
TARGETS = {
    "windows": ("Windows Desktop", "FactoryForge.exe"),
    "linux": ("Linux", "FactoryForge.x86_64"),
}

#: Everything that is not the engine itself. templates/ are res:// resources
#: and travel inside the binary, so they are deliberately absent here.
PAYLOAD = [
    ("examples", "examples"),
    ("docs/GETTING_STARTED.md", "docs/GETTING_STARTED.md"),
    ("docs/tag-bus.md", "docs/tag-bus.md"),
    ("docs/DRIVER_AUTHORING.md", "docs/DRIVER_AUTHORING.md"),
    ("docs/PART_AUTHORING.md", "docs/PART_AUTHORING.md"),
    ("docs/TEST_PLAN.md", "docs/TEST_PLAN.md"),
    ("README.md", "README.md"),
    ("LICENSE", "LICENSE"),
]


def platform_key() -> str:
    return "windows" if sys.platform.startswith("win") else "linux"


def export_engine(godot: str, target: str, staging: Path) -> Path:
    preset, binary = TARGETS[target]
    out = staging / binary
    staging.mkdir(parents=True, exist_ok=True)

    if not (ENGINE / "FactoryForge.sln").exists():
        raise SystemExit(
            "engine/FactoryForge.sln is missing. Godot refuses to export the .NET\n"
            "assemblies without it and still writes a binary, so the export looks\n"
            "like it worked and every C# script in it fails at runtime. Create it\n"
            "with:  dotnet new sln -n FactoryForge --format sln && dotnet sln add FactoryForge.csproj")

    print(f"[engine] exporting {preset} -> {out}")
    result = subprocess.run(
        [godot, "--headless", "--path", str(ENGINE), "--export-release", preset, str(out)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=900)
    # Godot exits 0 on an export that "completed with warnings", and a missing
    # .NET solution is one of those warnings, so the exit code alone is not
    # enough to trust.
    for line in (result.stdout + result.stderr).splitlines():
        if "ERROR:" in line and "Export" in line:
            print("  " + line.strip())
    if not out.exists():
        raise SystemExit(f"export produced no {out}")
    return out


def freeze_sidecar(staging: Path) -> Path | None:
    """PyInstaller the sidecar into one executable beside the engine."""
    entry = ROOT / "tools" / "packaging" / "sidecar_entry.py"
    work = DIST / "_pyinstaller"
    print("[sidecar] freezing with PyInstaller (a few minutes)")
    result = subprocess.run(
        [sys.executable, "-m", "PyInstaller", "--noconfirm", "--onefile",
         "--name", "factoryforge-sidecar",
         "--distpath", str(work / "dist"), "--workpath", str(work / "build"),
         "--specpath", str(work),
         "--paths", str(ROOT / "sidecar"),
         "--collect-submodules", "factoryforge_sidecar",
         str(entry)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
    if result.returncode != 0:
        print(result.stdout[-2000:])
        print(result.stderr[-2000:], file=sys.stderr)
        raise SystemExit("PyInstaller failed")

    built = next((work / "dist").glob("factoryforge-sidecar*"), None)
    if built is None:
        raise SystemExit("PyInstaller reported success but produced no executable")
    dest = staging / built.name
    shutil.copy2(built, dest)
    print(f"[sidecar] {dest.name} ({dest.stat().st_size / 1e6:.1f} MB)")
    return dest


def copy_payload(staging: Path) -> None:
    for src_rel, dest_rel in PAYLOAD:
        src, dest = ROOT / src_rel, staging / dest_rel
        if not src.exists():
            print(f"[payload] skipped, missing: {src_rel}")
            continue
        dest.parent.mkdir(parents=True, exist_ok=True)
        if src.is_dir():
            shutil.copytree(src, dest, dirs_exist_ok=True)
        else:
            shutil.copy2(src, dest)
    print(f"[payload] {len(PAYLOAD)} entries")


def make_archive(staging: Path, target: str) -> Path:
    archive = DIST / f"FactoryForge-{target}.zip"
    if archive.exists():
        archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
        for path in sorted(staging.rglob("*")):
            if path.is_file():
                z.write(path, path.relative_to(staging.parent))
    print(f"[archive] {archive.name} ({archive.stat().st_size / 1e6:.1f} MB)")
    return archive


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--target", choices=sorted(TARGETS), default=platform_key(),
                        help="which preset to export (default: this platform)")
    parser.add_argument("--no-sidecar", action="store_true",
                        help="skip the PyInstaller step")
    parser.add_argument("--skip-archive", action="store_true",
                        help="leave the staged tree without zipping it")
    args = parser.parse_args(argv)

    godot = find_godot()
    if godot is None:
        print("[ERROR] no Godot .NET build found; see run.py's message.", file=sys.stderr)
        return 1

    staging = DIST / args.target
    if staging.exists():
        shutil.rmtree(staging)

    export_engine(godot, args.target, staging)
    if not args.no_sidecar:
        freeze_sidecar(staging)
    copy_payload(staging)

    if not args.skip_archive:
        make_archive(staging, args.target)

    print(f"\nStaged in {staging}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
