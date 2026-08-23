#!/usr/bin/env python3
"""Cross-platform launcher: find Godot, build the engine, launch it.

    python run.py                  # build + launch, nothing else
    python run.py --live-driver    # also start tools/live_driver.py once the engine is up
    python run.py --no-build       # skip `dotnet build` (engine already built)
    python run.py -- --deterministic   # pass args through to the engine

See docs/GETTING_STARTED.md for what to install first.
"""
from __future__ import annotations

import argparse
import os
import platform
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ENGINE = ROOT / "engine"

#: Any 4.7.x .NET build runs this project — `project.godot` declares the feature
#: as "4.7", not a patch version. Newest first, so a machine holding several
#: picks the newest rather than whichever was released first.
GODOT_VERSIONS = ["4.7.2", "4.7.1"]
GODOT_NAMES = ["godot", "godot-mono"] + [
    name
    for version in GODOT_VERSIONS
    for name in (f"Godot_v{version}-stable_mono_win64_console",
                 f"Godot_v{version}-stable_mono_win64_console.exe",
                 f"Godot_v{version}-stable_mono_linux.x86_64")
]
GODOT_DOWNLOAD = "https://godotengine.org/download/ (the .NET build for your OS, 4.7.2 or newer 4.7.x)"


def find_godot() -> str | None:
    if (env := os.environ.get("GODOT")) and Path(env).exists():
        return env
    for name in GODOT_NAMES:
        if found := shutil.which(name):
            return found
    return None


def port_holder(port: int = 7411) -> str | None:
    """Best-effort description of whatever holds `port`, or None if it is free."""
    with socket.socket() as probe:
        probe.settimeout(0.5)
        if probe.connect_ex(("127.0.0.1", port)) != 0:
            return None
    try:
        if platform.system() == "Windows":
            out = subprocess.run(
                ["powershell", "-NoProfile", "-Command",
                 f"(Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue "
                 "| Select-Object -First 1 -ExpandProperty OwningProcess "
                 "| ForEach-Object { (Get-Process -Id $_).ProcessName })"],
                capture_output=True, text=True, timeout=5,
            )
            return out.stdout.strip() or "an unidentified process"
        out = subprocess.run(["lsof", "-t", "-i", f":{port}", "-sTCP:LISTEN"],
                             capture_output=True, text=True, timeout=5)
        pid = out.stdout.strip().splitlines()[0] if out.stdout.strip() else None
        if not pid:
            return "an unidentified process"
        name = subprocess.run(["ps", "-p", pid, "-o", "comm="],
                              capture_output=True, text=True, timeout=5).stdout.strip()
        return f"{name or 'process'} (pid {pid})"
    except Exception:
        return "an unidentified process"


def main(argv: list[str]) -> int:
    if "--" in argv:
        i = argv.index("--")
        own_argv, engine_args = argv[:i], argv[i + 1:]
    else:
        own_argv, engine_args = argv, []

    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--live-driver", action="store_true",
                        help="also start tools/live_driver.py once the engine is up")
    parser.add_argument("--no-build", action="store_true", help="skip `dotnet build`")
    args = parser.parse_args(own_argv)

    print("FactoryForge launcher")

    godot = find_godot()
    if not godot:
        print("[ERROR] Could not find a Godot 4.7 .NET build.")
        print(f"  Download it from: {GODOT_DOWNLOAD}")
        print("  Then either put it on PATH, or set the GODOT environment variable")
        print("  to its full executable path.")
        return 1
    print(f"[OK] Godot: {godot}")

    if not shutil.which("dotnet"):
        print("[ERROR] Could not find `dotnet` on PATH.")
        print("  Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0")
        return 1
    print("[OK] .NET SDK found")

    if sys.version_info < (3, 11):
        print(f"[WARN] Python {platform.python_version()} found; the sidecar needs 3.11+.")
        print("       The 3D engine itself does not need Python and will still launch.")
    else:
        print(f"[OK] Python {platform.python_version()}")

    if not args.no_build:
        print("Building engine (dotnet build)...")
        result = subprocess.run(["dotnet", "build"], cwd=ENGINE)
        if result.returncode != 0:
            print("[ERROR] Engine build failed.")
            return result.returncode
        print("[OK] Build succeeded")

    holder = port_holder(7411)
    if holder:
        print(f"[WARN] Port 7411 is already in use by {holder}.")
        try:
            answer = input("        Launch anyway? The engine's tag bus will fail to bind. [y/N] ")
        except EOFError:
            answer = "n"
        if answer.strip().lower() not in ("y", "yes"):
            print("Aborted. Stop that process first, or close the other FactoryForge instance.")
            return 1

    print("Launching Godot...")
    cmd = [godot, "--path", str(ENGINE)]
    if engine_args:
        cmd += ["--"] + engine_args
    subprocess.Popen(cmd)

    if args.live_driver:
        print("Waiting 4 seconds for the tag bus, then starting tools/live_driver.py...")
        time.sleep(4)
        subprocess.run([sys.executable, str(ROOT / "tools" / "live_driver.py")])

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
