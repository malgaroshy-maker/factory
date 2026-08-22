"""Run the whole FactoryForge test plan and report.

    python tools/test_plan.py              # everything that needs no display
    python tools/test_plan.py --gui        # add the checks that need one
    python tools/test_plan.py --only C,E   # just those sections

Exits non-zero if anything failed. See docs/TEST_PLAN.md for what each check is
for; the short reasons here are so a failure explains itself without the doc.

Why a runner rather than a CI yaml: the interesting failures in this project are
at the seam between the Python sidecar and the Godot engine, and checking that
means starting an engine, driving it, and reading what came back. That is
awkward to express as a list of shell steps and easy to express here.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ENGINE = ROOT / "engine"
SIDECAR = ROOT / "sidecar"

#: Where Godot writes user:// on this platform, for tests that read exports.
USER_DIR = Path(os.environ.get("APPDATA", Path.home())) / "Godot" / "app_userdata" / "FactoryForge"


def find_godot() -> str | None:
    """Locate a Godot .NET binary: $GODOT, then PATH, then the known install."""
    if (env := os.environ.get("GODOT")) and Path(env).exists():
        return env
    for name in ("godot", "godot-mono", "Godot_v4.7.1-stable_mono_win64_console"):
        if found := shutil.which(name):
            return found
    guess = Path(r"D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64"
                 r"\Godot_v4.7.1-stable_mono_win64_console.exe")
    return str(guess) if guess.exists() else None


GODOT = find_godot()


@dataclass
class Result:
    ident: str
    name: str
    ok: bool
    detail: str = ""
    skipped: bool = False


RESULTS: list[Result] = []


def record(ident: str, name: str, ok: bool, detail: str = "", skipped: bool = False) -> bool:
    RESULTS.append(Result(ident, name, ok, detail, skipped))
    mark = "SKIP" if skipped else ("PASS" if ok else "FAIL")
    print(f"  [{mark}] {ident} {name}" + (f"  — {detail}" if detail else ""), flush=True)
    return ok


def run(cmd: list[str], cwd: Path = ROOT, timeout: float = 180) -> tuple[int, str]:
    """Run a command, returning (exit code, stdout+stderr)."""
    try:
        # encoding is explicit: the sidecar prints an em dash, and letting
        # Windows decode as cp1252 turned "— 16 tags" into "â€” 16 tags" and
        # broke a regex that was looking for it.
        proc = subprocess.run(cmd, cwd=cwd, timeout=timeout, capture_output=True,
                              text=True, encoding="utf-8", errors="replace")
        return proc.returncode, (proc.stdout or "") + (proc.stderr or "")
    except subprocess.TimeoutExpired as exc:
        out = (exc.stdout or "") + (exc.stderr or "")
        return 124, (out if isinstance(out, str) else out.decode("utf-8", "replace")) + "\n[TIMEOUT]"


def engine(args: list[str], headless: bool = True, timeout: float = 90) -> tuple[int, str]:
    cmd = [GODOT or "godot"]
    if headless:
        cmd.append("--headless")
    cmd += ["--path", str(ENGINE), "--"] + args
    return run(cmd, timeout=timeout)


def port_in_use(port: int = 7411) -> bool:
    with socket.socket() as probe:
        probe.settimeout(0.5)
        return probe.connect_ex(("127.0.0.1", port)) == 0


def wait_for_port_free(timeout: float = 20.0) -> bool:
    """The previous engine's socket outlives its process by a moment, so back-to-back
    checks must wait rather than assume."""
    deadline = time.perf_counter() + timeout
    while time.perf_counter() < deadline:
        if not port_in_use():
            return True
        time.sleep(0.4)
    return False


class EngineProcess:
    """A running engine, for the checks that need something to connect to.

    Output goes to a file, never to ``subprocess.PIPE``. Godot blocks on a full
    stdout pipe that nobody is draining, and it fills that buffer *before* it
    binds the tag bus — so a piped engine starts, prints its banner, and then
    hangs forever without ever listening. Measured: with inherited stdout or a
    file the port opens in 0.25s; with a pipe it never opens at all.
    """

    def __init__(self, *args: str) -> None:
        self.args = list(args)
        self.proc: subprocess.Popen | None = None
        self.log = ROOT / ".test_plan_engine.log"
        self._handle = None

    def __enter__(self) -> "EngineProcess":
        # A leftover engine holds the port, and the new one then binds nothing
        # while looking healthy. Fail here with the real reason rather than
        # 30 seconds later with "never opened port".
        if not wait_for_port_free():
            raise RuntimeError(
                "port 7411 is still held after 20s — another engine is running. "
                "Close it before running the plan.")

        cmd = [GODOT or "godot", "--headless", "--path", str(ENGINE), "--"] + self.args
        self._handle = open(self.log, "w", encoding="utf-8", errors="replace")
        self.proc = subprocess.Popen(cmd, stdout=self._handle, stderr=subprocess.STDOUT)
        # Wait for the port rather than sleeping a guessed amount: on a cold
        # start the .NET runtime can take several seconds to come up.
        for _ in range(120):
            with socket.socket() as probe:
                probe.settimeout(0.25)
                if probe.connect_ex(("127.0.0.1", 7411)) == 0:
                    time.sleep(0.5)      # let the describe go out
                    return self
            time.sleep(0.25)
        raise RuntimeError("engine never opened port 7411")

    def output(self) -> str:
        return self.log.read_text(encoding="utf-8", errors="replace") if self.log.exists() else ""

    def __exit__(self, *exc: object) -> None:
        if self.proc and self.proc.poll() is None:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.proc.kill()
        if self._handle:
            self._handle.close()


def sidecar(args: list[str], timeout: float = 60) -> tuple[int, str]:
    return run([sys.executable, "-m", "factoryforge_sidecar"] + args, cwd=SIDECAR, timeout=timeout)


# --- A. build and static ----------------------------------------------------

def section_a() -> None:
    print("\nA. Build and static")

    code, out = run(["dotnet", "build", "-v", "q", "--nologo"], cwd=ENGINE, timeout=300)
    warnings = re.findall(r"warning [A-Z]+\d+", out)
    record("A1", "engine builds with zero warnings", code == 0 and not warnings,
           "build failed" if code else (f"{len(warnings)} warnings" if warnings else ""))

    code, out = run([sys.executable, "-c", "import factoryforge_sidecar"], cwd=SIDECAR, timeout=60)
    record("A2", "sidecar package imports", code == 0, out.strip().splitlines()[-1] if code else "")

    # Temporary verification hooks have escaped into commits before.
    stray = []
    for path in (ENGINE / "src").rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="replace")
        if "TempClickProbe" in text or "TempUiProbe" in text or "TempInspectorProbe" in text:
            stray.append(path.name)
        if re.search(r"\b(TODO|FIXME|HACK)\b", text):
            stray.append(f"{path.name}:marker")
    record("A3", "no temporary probes or TODO markers left in engine/src",
           not stray, ", ".join(sorted(set(stray))))

    # A4/A5: the C# and Python tag models, and the wire messages between them,
    # must agree with docs/tag-bus.md and with each other. See FF-29. The
    # Python side of the parity fixture runs as part of B1 (tests/test_tag_parity.py);
    # this is the C# side plus the wire-level conformance check.
    code, out = engine(["--self-test=parity", "--duration=20"], timeout=90)
    fails = [line.strip() for line in out.splitlines() if "FAIL" in line]
    passed = code == 0 and "self-test parity: PASS" in out
    record("A4", "C# tag model matches the shared parity fixture", passed,
           "; ".join(fails[:3]) if fails else ("no PASS line" if not passed else ""))

    with EngineProcess("--duration=30"):
        code, out = run([sys.executable, str(ROOT / "tools" / "check_protocol.py")], timeout=30)
    record("A5", "hello/describe/update carry exactly the fields docs/tag-bus.md names",
           code == 0 and "RESULT OK" in out,
           out.strip().splitlines()[-1] if out.strip() else "no output")


# --- B. python suite --------------------------------------------------------

def section_b() -> None:
    print("\nB. Python unit and integration")
    code, out = run([sys.executable, "-m", "pytest", "-q", "tests"], timeout=600)
    match = re.search(r"(\d+) passed", out)
    failed = re.search(r"(\d+) failed", out)
    record("B1", "pytest suite", code == 0,
           f"{match.group(1) if match else '?'} passed"
           + (f", {failed.group(1)} FAILED" if failed else ""))


# --- C/D. engine self-tests -------------------------------------------------

def _self_test(ident: str, name: str, which: str, headless: bool = True) -> None:
    code, out = engine([f"--self-test={which}", "--duration=40"], headless=headless, timeout=180)
    fails = [line.strip() for line in out.splitlines() if "FAIL" in line]
    passed = code == 0 and f"self-test {which}: PASS" in out
    record(ident, name, passed, "; ".join(fails[:3]) if fails else ("no PASS line" if not passed else ""))


def section_c() -> None:
    print("\nC. Engine self-tests (headless)")
    _self_test("C1", "panel buttons: one-scan pulse, latched E-stop, cap picking", "buttons")
    _self_test("C2", "rename and I/O export", "io")
    _self_test("C3", "scene save/load round-trip, every part type", "scene")
    _self_test("C4", "every shipped start-screen template loads and registers I/O", "templates")


def section_d(enabled: bool) -> None:
    print("\nD. Engine self-tests (need a display)")
    if not enabled:
        record("D1", "whole click path, synthesized mouse event to tag", True,
               "not run (pass --gui)", skipped=True)
        return
    _self_test("D1", "whole click path, synthesized mouse event to tag", "click", headless=False)


# --- E. determinism and the regression contract -----------------------------

def _driven_run(deterministic: bool) -> tuple[int, int] | None:
    """Start an engine and drive it with the real control logic, returning the
    sorted counts. Undriven runs are useless as a determinism check — nothing
    moves, so every run trivially agrees on zero."""
    args = ["--duration=70"] + (["--deterministic"] if deterministic else [])
    with EngineProcess(*args):
        code, out = run([sys.executable, str(ROOT / "tools" / "drive_engine.py")], timeout=150)
    got = re.search(r"RESULT tall=(\d+) short=(\d+)", out)
    return (int(got.group(1)), int(got.group(2))) if got else None


def section_e() -> None:
    print("\nE. Determinism and the regression contract")

    first = _driven_run(deterministic=True)
    record("E1", "drive_engine.py sorts 5 tall and 5 short", first == (5, 5),
           f"got {first}" if first else "no RESULT line")

    second = _driven_run(deterministic=True)
    record("E2", "a second driven run agrees exactly, and did real work",
           first is not None and first == second and sum(first) > 0,
           f"{first} vs {second}")

    code, slow = engine(["--deterministic", "--duration=8", "--time-scale=1"], timeout=90)
    code, fast = engine(["--deterministic", "--duration=8", "--time-scale=4"], timeout=90)
    slow_ticks = re.search(r"tick=(\d+)", slow)
    fast_ticks = re.search(r"tick=(\d+)", fast)
    ok = bool(slow_ticks and fast_ticks) and int(fast_ticks.group(1)) > int(slow_ticks.group(1)) * 1.5
    record("E3", "time-scale=4 advances the sim faster than 1x", ok,
           f"1x={slow_ticks.group(1) if slow_ticks else '?'} "
           f"4x={fast_ticks.group(1) if fast_ticks else '?'} ticks")


# --- F. the engine/sidecar seam ---------------------------------------------

def section_f() -> None:
    print("\nF. Engine and sidecar seam")

    with EngineProcess("--duration=90"):
        code, out = sidecar(["connect", "--driver", "mock", "--duration", "6"], timeout=90)
        # Deliberately not matching the em dash between them: what matters is
        # that it found the scene and its tags, not the punctuation.
        match = re.search(r"connected to scene '([^']+)'.*?(\d+) tags", out, re.S)
        record("F1", "connect attaches a driver to a running engine",
               bool(match) and match.group(1) != "None" and int(match.group(2)) > 0,
               f"scene={match.group(1)} tags={match.group(2)}" if match else "no connect line")

        code, out = sidecar(["connect", "--driver", "modbus-tcp", "--duration", "6"], timeout=90)
        record("F2", "modbus-tcp server starts and reports its address map",
               "Modbus address map" in out, "" if "Modbus address map" in out else out.strip()[-120:])

        code, out = sidecar(["connect", "--driver", "opcua-server", "--duration", "8"], timeout=120)
        record("F3", "opcua-server starts and reports an endpoint",
               "OPC UA server:" in out, "" if "OPC UA server:" in out else out.strip()[-120:])

    # No engine running now.
    started = time.perf_counter()
    code, out = sidecar(["connect", "--driver", "mock", "--timeout", "3"], timeout=60)
    elapsed = time.perf_counter() - started
    record("F4", "connect with no engine fails fast and says why",
           code != 0 and "no engine listening" in out and elapsed < 25,
           f"exit={code} after {elapsed:.1f}s")

    # The physics scene, driven by real control logic. This is the check that
    # proves emitter, sensors, pusher, chute and removers work together under
    # Jolt — the deterministic scene in E1 simulates all of that in code, so it
    # would pass even if every rigid body were broken.
    #
    # `--driver mock` alone would not do: the mock is a passthrough with no
    # logic of its own, so an engine driven by it correctly does nothing.
    # Counts are not asserted exactly — Jolt does not promise reproducible
    # numbers, which is the entire reason --deterministic exists.
    counts = _driven_run(deterministic=False)
    record("F5", "the rigid-body scene sorts cartons when driven for real",
           counts is not None and sum(counts) > 0, f"got {counts}")


# --- G. robustness ----------------------------------------------------------

def section_g() -> None:
    print("\nG. Robustness")

    bad = USER_DIR / "testplan_corrupt.json"
    bad.parent.mkdir(parents=True, exist_ok=True)
    bad.write_text("{ this is not json at all ]", encoding="utf-8")
    code, out = engine(["--duration=6"], timeout=90)
    record("G1", "a corrupt scene file on disk does not stop startup",
           code == 0 and "engine ready" in out, f"exit={code}")

    code, out = engine(["--duration=6", "--nonsense-flag=1"], timeout=90)
    record("G2", "an unknown CLI flag is ignored rather than fatal",
           code == 0 and "engine ready" in out, f"exit={code}")

    # G3 is covered inside --self-test=scene, which plants an unknown property
    # key; recorded here so the plan's table matches what actually ran.
    code, out = engine(["--self-test=scene", "--duration=40"], timeout=180)
    record("G3", "a scene with unknown property keys still loads",
           code == 0 and "self-test scene: PASS" in out)

    # A second instance cannot bind the bus. It must say so rather than
    # reporting "ready" and leaving someone to debug their PLC.
    with EngineProcess("--duration=40"):
        code, out = engine(["--duration=6"], timeout=90)
    record("G4", "a second engine says its tag bus is dead instead of claiming ready",
           "NO TAG BUS" in out, "still reports a healthy start" if "NO TAG BUS" not in out else "")

    # G5: kill the engine out from under a connected sidecar and confirm it
    # notices and starts retrying, rather than serving the last-known values
    # forever as if the line were still running. See FF-03.
    g5_log = ROOT / ".test_plan_sidecar_g5.log"
    with EngineProcess("--duration=30") as eng:
        handle = open(g5_log, "w", encoding="utf-8", errors="replace")
        sidecar_proc = subprocess.Popen(
            [sys.executable, "-m", "factoryforge_sidecar", "connect", "--driver", "mock", "--duration", "20"],
            cwd=SIDECAR, stdout=handle, stderr=subprocess.STDOUT)
        time.sleep(2.0)   # let it connect and receive the describe
        eng.proc.terminate()
        try:
            eng.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            eng.proc.kill()
        time.sleep(3.0)   # give the sidecar a moment to notice and log it
        sidecar_proc.terminate()
        try:
            sidecar_proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            sidecar_proc.kill()
        handle.close()
    g5_out = g5_log.read_text(encoding="utf-8", errors="replace") if g5_log.exists() else ""
    record("G5", "the sidecar notices when the engine dies mid-run and retries",
           "connection lost" in g5_out and "retrying" in g5_out,
           "" if "connection lost" in g5_out else g5_out.strip()[-160:])

    # G6: forcing an input tag while paused must still reach the bus. Pausing
    # used to zero the fixed-timestep accumulator that gated SendUpdates(), so
    # a forced sensor sat in the tag table and never left the engine until you
    # un-paused it — exactly the workflow pause exists for. See FF-14.
    with EngineProcess("--duration=30", "--paused"):
        code, out = run([sys.executable, str(ROOT / "tools" / "check_force_while_paused.py")], timeout=30)
    record("G6", "forcing a tag while paused still reaches the bus",
           code == 0 and "RESULT update received" in out,
           out.strip().splitlines()[-1] if out.strip() else "no output")


SECTIONS = {
    "A": section_a, "B": section_b, "C": section_c,
    "E": section_e, "F": section_f, "G": section_g,
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--gui", action="store_true", help="also run checks needing a display")
    parser.add_argument("--only", help="comma-separated section letters, e.g. C,E")
    args = parser.parse_args()

    if GODOT is None:
        print("Godot not found. Set $GODOT to the .NET build's executable.", file=sys.stderr)
        return 2

    wanted = [s.strip().upper() for s in args.only.split(",")] if args.only else None
    print(f"FactoryForge test plan\n  godot:  {GODOT}\n  python: {sys.executable}")

    started = time.perf_counter()
    for letter, fn in SECTIONS.items():
        if wanted and letter not in wanted:
            continue
        fn()
    if not wanted or "D" in wanted:
        section_d(args.gui)

    failed = [r for r in RESULTS if not r.ok and not r.skipped]
    skipped = [r for r in RESULTS if r.skipped]
    print(f"\n{'=' * 62}")
    print(f"{len(RESULTS) - len(failed) - len(skipped)} passed, "
          f"{len(failed)} failed, {len(skipped)} skipped "
          f"in {time.perf_counter() - started:.0f}s")
    for r in failed:
        print(f"  FAILED  {r.ident} {r.name}" + (f"  — {r.detail}" if r.detail else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
