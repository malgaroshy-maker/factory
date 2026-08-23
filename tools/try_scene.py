#!/usr/bin/env python3
"""Run a shipped scene headless, drive it the way a PLC would, and check it
actually works -- before you write a real one against it.

    python tools/try_scene.py --list
    python tools/try_scene.py --scene sorting-by-height
    python tools/try_scene.py --scene tank-level-control --duration 20 --verbose

Spawns the engine itself (headless, the scene's own template if it has one)
and tears it down when done -- no separate `godot --headless ...` command to
remember, and no risk of `connect --driver mock` (§2.6), which connects a
client and then drives nothing: the most obvious "let me just try it" command
leaving the scene more dead than doing nothing at all.

One RESULT line, exit 0 on a real pass and 1 otherwise -- the same convention
tools/check_protocol.py and tools/check_force_while_paused.py already set.
Each scene's assertions are band-based rather than exact counts (except
sorting-by-height, run --deterministic to match tools/drive_engine.py's own
tall=5/short=5 contract): the four templates run real rigid-body physics, and
no template can promise an exact count the way the deterministic scene can
(§4). Each scene's driving logic mirrors its engine-side demo profile under
engine/src/Sim/DemoProfiles/ line for line, so a regression in either one
fails the same way, and this doubles as a check that the engine behaves
correctly when driven purely over the wire -- the same seam a real PLC uses.
"""
from __future__ import annotations

import argparse
import asyncio
import json
import logging
import os
import socket
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ENGINE = ROOT / "engine"
MANIFEST = ENGINE / "templates" / "manifest.json"
sys.path.insert(0, str(ROOT / "sidecar"))
logging.disable(logging.WARNING)

from factoryforge_sidecar.tagbus import TagBusClient  # noqa: E402

PORT = 7411


def find_godot() -> str | None:
    """$GODOT, then PATH, then the usual download locations.

    One implementation, in run.py, rather than a third copy drifting from the
    launcher's: they all have to agree about which Godot a machine has, or
    `run.py` opens one build while this script spawns another.
    """
    sys.path.insert(0, str(ROOT))
    from run import find_godot as locate     # noqa: PLC0415 — avoids a cycle at import time
    return locate()


def load_manifest() -> list[dict]:
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def port_listening() -> bool:
    with socket.socket() as probe:
        probe.settimeout(0.5)
        return probe.connect_ex(("127.0.0.1", PORT)) == 0


# --- engine lifecycle -------------------------------------------------

class Engine:
    """The headless subprocess. Output goes to a temp file rather than a
    pipe: an unread PIPE can deadlock the child once its buffer fills, and a
    file gives something to show the user if the connect step times out."""

    def __init__(self, godot: str, entry: dict, deterministic: bool) -> None:
        args = [godot, "--headless", "--path", str(ENGINE), "--"]
        if deterministic:
            args.append("--deterministic")
        if entry["path"]:
            args.append(f"--scene={entry['path']}")

        self._log = tempfile.NamedTemporaryFile(mode="w+", suffix=".log", delete=False)
        self.proc = subprocess.Popen(args, stdout=self._log, stderr=subprocess.STDOUT, text=True)

    def tail(self, lines: int = 20) -> str:
        self._log.flush()
        with open(self._log.name, encoding="utf-8", errors="replace") as f:
            return "".join(f.readlines()[-lines:])

    def stop(self) -> None:
        if self.proc.poll() is None:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.proc.kill()
                self.proc.wait(timeout=5)
        self._log.close()
        try:
            os.unlink(self._log.name)
        except OSError:
            pass


async def wait_for_port(timeout: float) -> bool:
    deadline = time.perf_counter() + timeout
    while time.perf_counter() < deadline:
        if port_listening():
            return True
        await asyncio.sleep(0.1)
    return False


async def connect(timeout: float = 15.0) -> tuple[TagBusClient, asyncio.Task]:
    bus = TagBusClient(f"ws://127.0.0.1:{PORT}/tagbus")
    runner = asyncio.create_task(bus.run())
    try:
        await asyncio.wait_for(bus.connected.wait(), timeout=timeout)
        deadline = time.perf_counter() + timeout
        while bus.scene is None or len(bus.table) == 0:
            if time.perf_counter() > deadline:
                raise RuntimeError("connected but never received a describe")
            await asyncio.sleep(0.05)
    except (asyncio.TimeoutError, RuntimeError):
        runner.cancel()
        raise
    return bus, runner


def bit(bus: TagBusClient, tag_id: str) -> bool:
    return bool(bus.read(tag_id)) if bus.table.get(tag_id) is not None else False


def num(bus: TagBusClient, tag_id: str) -> float:
    return float(bus.read(tag_id)) if bus.table.get(tag_id) is not None else 0.0


async def press(bus: TagBusClient, tag_id: str, hold: float = 0.15) -> None:
    """A momentary operator press: force the input high just long enough for
    the engine to see a rising edge, then release it. Real panel presses stay
    high for exactly one physics tick (ButtonPanel.Press); this holds it for
    several, which still reads as one edge, since nothing here is watching
    for it to drop and come back."""
    await bus.force({tag_id: True})
    await asyncio.sleep(hold)
    await bus.force(clear=[tag_id])


# --- per-scene drivers --------------------------------------------------
# Each mirrors its engine-side profile under engine/src/Sim/DemoProfiles/.

async def drive_sorting_by_height(bus: TagBusClient, duration: float, verbose: bool,
                                   deterministic: bool) -> tuple[bool, str]:
    EMIT_HALF_PERIOD = 1.5
    PUSH_DELAY = 0.9
    PUSH_HOLD = 0.5

    await bus.write("conveyor.rotate", True)
    await bus.write("stack_light.green", True)

    emit_flag = False
    next_toggle = time.perf_counter()
    high_mem = False
    extend_at: float | None = None
    retract_at: float | None = None
    start = time.perf_counter()

    while time.perf_counter() - start < duration:
        await asyncio.sleep(0.02)
        now = time.perf_counter()

        if now >= next_toggle:
            emit_flag = not emit_flag
            next_toggle = now + EMIT_HALF_PERIOD
            await bus.write("emitter.emit", emit_flag)

        high = bit(bus, "sensor_high.detect")
        if high and not high_mem:
            extend_at = now + PUSH_DELAY
        high_mem = high

        if extend_at is not None and now >= extend_at:
            await bus.write("pusher.extend", True)
            retract_at = extend_at + PUSH_HOLD
            extend_at = None
        if retract_at is not None and now >= retract_at:
            await bus.write("pusher.extend", False)
            retract_at = None

    tall, short = int(num(bus, "counter.tall")), int(num(bus, "counter.short"))
    print(f"RESULT tall={tall} short={short}")
    if deterministic:
        ok = tall == 5 and short == 5
        return ok, "" if ok else f"expected tall=5 short=5 (deterministic), got tall={tall} short={short}"
    # Attached to an engine already running non-deterministically (real Jolt
    # physics) -- an exact count is not a promise this scene can make outside
    # --deterministic, so this falls back to the same band-based check every
    # other scene uses.
    ok = tall > 0 and short > 0
    return ok, "" if ok else f"expected both counters to advance, got tall={tall} short={short}"


async def drive_start_stop_station(bus: TagBusClient, duration: float, verbose: bool,
                                  deterministic: bool) -> tuple[bool, str]:
    EMIT_HALF_PERIOD = 1.5
    state = {"running": False, "tripped": False, "produced": 0,
             "prev_start": False, "prev_stop": False, "prev_reset": False, "prev_present": False,
             "emit_flag": False, "elapsed": 0.0, "next_toggle": EMIT_HALF_PERIOD}

    async def tick(dt: float) -> None:
        s = state
        start_b, stop_b = bit(bus, "panel.start"), bit(bus, "panel.stop")
        reset_b, healthy = bit(bus, "panel.reset"), bit(bus, "panel.estop")
        present = bit(bus, "part_present.detect")

        start_edge = start_b and not s["prev_start"]
        stop_edge = stop_b and not s["prev_stop"]
        reset_edge = reset_b and not s["prev_reset"]
        present_edge = present and not s["prev_present"]
        s["prev_start"], s["prev_stop"] = start_b, stop_b
        s["prev_reset"], s["prev_present"] = reset_b, present

        if not healthy:
            s["tripped"] = True
        elif reset_edge:
            s["tripped"] = False

        if s["tripped"]:
            s["running"] = False
        elif stop_edge:
            s["running"] = False
        elif start_edge:
            s["running"] = True

        if present_edge:
            s["produced"] += 1

        s["elapsed"] += dt
        if s["running"]:
            if s["elapsed"] >= s["next_toggle"]:
                s["emit_flag"] = not s["emit_flag"]
                s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD
        else:
            s["emit_flag"] = False
            s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD

        await bus.write_many({
            "belt.rotate": s["running"],
            "emitter.emit": s["emit_flag"],
            "produced.value": s["produced"],
            "tower.green": s["running"],
            "tower.red": s["tripped"],
            "tower.yellow": (not s["running"]) and (not s["tripped"]),
        })

    stop_event = asyncio.Event()

    async def controller_loop() -> None:
        while not stop_event.is_set():
            await tick(0.02)
            await asyncio.sleep(0.02)

    task = asyncio.create_task(controller_loop())
    problems: list[str] = []

    def check(ok: bool, what: str) -> None:
        if not ok:
            problems.append(what)
        if verbose:
            print(f"  {'ok' if ok else 'FAIL'}  {what}")

    try:
        await asyncio.sleep(0.3)
        check(not bit(bus, "belt.rotate"), "initial: belt is off")
        check(bit(bus, "tower.yellow") and not bit(bus, "tower.green") and not bit(bus, "tower.red"),
              "initial: stopped-healthy shows yellow only")

        await press(bus, "panel.start")
        await asyncio.sleep(0.3)
        check(bit(bus, "belt.rotate"), "after Start: belt runs")
        check(bit(bus, "tower.green") and not bit(bus, "tower.yellow") and not bit(bus, "tower.red"),
              "after Start: green only")

        await bus.force({"panel.estop": False})   # mushroom struck (normally closed)
        await asyncio.sleep(0.3)
        check(not bit(bus, "belt.rotate"), "after E-stop: belt off")
        check(bit(bus, "tower.red") and not bit(bus, "tower.green") and not bit(bus, "tower.yellow"),
              "after E-stop: red only")

        await press(bus, "panel.start")
        await asyncio.sleep(0.3)
        check(not bit(bus, "belt.rotate"), "Start while tripped: does NOT restart the belt")
        check(bit(bus, "tower.red"), "Start while tripped: still red, not green")

        await bus.force({"panel.estop": True})    # mushroom released
        await press(bus, "panel.reset")
        await asyncio.sleep(0.3)
        check(not bit(bus, "tower.red"), "after Reset: fault cleared")
        check(bit(bus, "tower.yellow") and not bit(bus, "belt.rotate"),
              "after Reset: stopped-healthy again, belt still off until Start")

        await press(bus, "panel.start")
        await asyncio.sleep(0.3)
        check(bit(bus, "belt.rotate"), "Start after Reset: belt runs again")
        check(bit(bus, "tower.green"), "Start after Reset: green")
    finally:
        stop_event.set()
        await task

    produced = int(num(bus, "produced.value"))
    print(f"RESULT sequence={'PASS' if not problems else 'FAIL'} produced={produced}")
    return not problems, "; ".join(problems)


async def drive_tank_level_control(bus: TagBusClient, duration: float, verbose: bool,
                                  deterministic: bool) -> tuple[bool, str]:
    SETPOINT, GAIN, BAND_PERCENT = 55.0, 4.0, 5.0

    await bus.write("tank.fill", 0.0)
    await bus.write("tank.drain", 0.0)

    start = time.perf_counter()
    level = 0.0
    while time.perf_counter() - start < duration:
        await asyncio.sleep(0.02)
        level = num(bus, "tank.level")
        error = SETPOINT - level
        fill = min(max(error * GAIN, 0.0), 100.0) if error > 0 else 0.0
        drain = min(max(-error * GAIN, 0.0), 100.0) if error <= 0 else 0.0
        await bus.write_many({"tank.fill": fill, "tank.drain": drain,
                              "level_readout.value": round(level)})
        if verbose and int(time.perf_counter() - start) % 3 == 0:
            print(f"  t={time.perf_counter() - start:4.1f}s level={level:5.1f}")

    band = SETPOINT * BAND_PERCENT / 100.0
    ok = SETPOINT - band <= level <= SETPOINT + band
    # Plain ASCII, not "±": a piped Python child's stdout encoding on Windows
    # is not reliably UTF-8, and a non-ASCII RESULT line can come out the
    # other end as a replacement character instead of the real one -- exactly
    # the failure this line exists to report clearly, not obscure.
    print(f"RESULT level={level:.1f} setpoint={SETPOINT} band=+/-{band:.1f}")
    return ok, "" if ok else f"tank.level settled at {level:.1f}, outside +/-{band:.1f} of {SETPOINT}"


async def drive_light_curtain_sorting(bus: TagBusClient, duration: float, verbose: bool,
                                     deterministic: bool) -> tuple[bool, str]:
    EMIT_HALF_PERIOD = 1.5
    TALL_THRESHOLD = 0.15
    PUSH_DELAY = 2.0
    CLEAR_DWELL = 0.4

    await bus.write("belt.rotate", True)
    await bus.write("diverter.extend", False)

    emit_flag = False
    next_toggle = time.perf_counter()
    prev_blocked = False
    extend_at: float | None = None
    extending = False
    retract_at: float | None = None
    start = time.perf_counter()

    while time.perf_counter() - start < duration:
        await asyncio.sleep(0.02)
        now = time.perf_counter()

        if now >= next_toggle:
            emit_flag = not emit_flag
            next_toggle = now + EMIT_HALF_PERIOD
            await bus.write("emitter.emit", emit_flag)

        blocked = bit(bus, "height_gauge.blocked")
        if blocked and not prev_blocked:
            height = num(bus, "height_gauge.height")
            if height >= TALL_THRESHOLD:
                extend_at = now + PUSH_DELAY
        prev_blocked = blocked

        if not extending and extend_at is not None and now >= extend_at:
            await bus.write("diverter.extend", True)
            extending = True
            extend_at = None

        if extending and retract_at is None and bit(bus, "diverter.extended"):
            retract_at = now + CLEAR_DWELL
        if retract_at is not None and now >= retract_at:
            await bus.write("diverter.extend", False)
            extending = False
            retract_at = None

    tall, short = int(num(bus, "tall_count.count")), int(num(bus, "short_count.count"))
    print(f"RESULT tall={tall} short={short}")
    ok = tall > 0 and short > 0
    return ok, "" if ok else f"expected both counters to advance, got tall={tall} short={short}"


async def drive_roller_line_weighing(bus: TagBusClient, duration: float, verbose: bool,
                                    deterministic: bool) -> tuple[bool, str]:
    EMIT_HALF_PERIOD = 1.5

    await bus.write("infeed.rotate", True)
    await bus.write("scale.rotate", True)

    emit_flag = False
    next_toggle = time.perf_counter()
    saw_metal = False
    start = time.perf_counter()

    while time.perf_counter() - start < duration:
        await asyncio.sleep(0.02)
        now = time.perf_counter()

        if now >= next_toggle:
            emit_flag = not emit_flag
            next_toggle = now + EMIT_HALF_PERIOD
            await bus.write("emitter.emit", emit_flag)

        if bit(bus, "metal_check.detect"):
            saw_metal = True

        await bus.write("weight_readout.value", round(num(bus, "scale.weight")))

    outfeed = int(num(bus, "outfeed.count"))
    print(f"RESULT outfeed={outfeed} sawMetal={saw_metal}")
    ok = outfeed > 0 and saw_metal
    problem = [] if ok else []
    if outfeed <= 0:
        problem.append(f"outfeed.count never advanced (got {outfeed})")
    if not saw_metal:
        problem.append("metal_check.detect never fired for a metal carton")
    return ok, "; ".join(problem)


DRIVERS = {
    "sorting-by-height": drive_sorting_by_height,
    "start-stop-station": drive_start_stop_station,
    "tank-level-control": drive_tank_level_control,
    "light-curtain-sorting": drive_light_curtain_sorting,
    "roller-line-weighing": drive_roller_line_weighing,
}

DEFAULT_DURATION = {
    "sorting-by-height": 35.0,
    "start-stop-station": 30.0,   # a ceiling on the scripted sequence, not a knob
    "tank-level-control": 15.0,
    "light-curtain-sorting": 20.0,
    "roller-line-weighing": 20.0,
}


async def run_scene(godot: str, entry: dict, duration: float | None, verbose: bool) -> int:
    scene_id = entry["id"]
    run_duration = duration if duration is not None else DEFAULT_DURATION[scene_id]

    eng: Engine | None = None
    deterministic = False

    if port_listening():
        # Something is already on the port -- most likely the windowed engine
        # a "Try this scene" button in the editor itself would be talking to
        # (UX-31). Attach to it rather than refusing outright: a real PLC
        # test tool that insists on starting its own engine could never be
        # pressed from inside the one already open on screen.
        try:
            bus, runner = await connect(timeout=5)
        except (asyncio.TimeoutError, RuntimeError) as exc:
            print(f"RESULT port {PORT} is already in use, and connecting to it failed too: {exc}")
            return 1

        if bus.scene != scene_id:
            print(f"RESULT an engine is already running scene {bus.scene!r}, not {scene_id!r} -- "
                  f"load \"{entry['title']}\" there first, or close it and this will start its own")
            runner.cancel()
            return 1
        print(f"Attached to the already-running engine (scene {bus.scene!r}, {len(bus.table)} tags)")
    else:
        deterministic = scene_id == "sorting-by-height"
        print(f"Starting engine for '{entry['title']}'{' (--deterministic)' if deterministic else ''}...")
        eng = Engine(godot, entry, deterministic)

        if not await wait_for_port(20.0):
            print("RESULT engine never opened the tag bus port")
            print(eng.tail())
            eng.stop()
            return 1

        try:
            bus, runner = await connect()
        except (asyncio.TimeoutError, RuntimeError) as exc:
            print(f"RESULT could not connect: {exc}")
            print(eng.tail())
            eng.stop()
            return 1

        if bus.scene != scene_id:
            print(f"RESULT connected, but scene is {bus.scene!r}, expected {scene_id!r}")
            runner.cancel()
            eng.stop()
            return 1
        print(f"connected to scene {bus.scene!r}, {len(bus.table)} tags")

    try:
        ok, problem = await DRIVERS[scene_id](bus, run_duration, verbose, deterministic)
        if ok:
            print(f"PASS — {entry['title']}")
        else:
            print(f"FAIL — {entry['title']}: {problem}")
        return 0 if ok else 1
    finally:
        runner.cancel()
        if eng is not None:
            eng.stop()


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--scene", help="manifest id, e.g. sorting-by-height (see --list)")
    parser.add_argument("--duration", type=float, default=None,
                        help="seconds to run before checking (default varies per scene)")
    parser.add_argument("--verbose", action="store_true", help="print progress while running")
    parser.add_argument("--list", action="store_true", help="list scene ids and exit")
    args = parser.parse_args(argv)

    manifest = load_manifest()

    if args.list:
        for entry in manifest:
            print(f"{entry['id']:24s} {entry['title']}")
        return 0

    if not args.scene:
        parser.error("--scene is required (or pass --list to see the ids)")

    entry = next((e for e in manifest if e["id"] == args.scene), None)
    if entry is None:
        ids = ", ".join(e["id"] for e in manifest)
        print(f"RESULT unknown scene {args.scene!r} -- known ids: {ids}")
        return 1

    if entry["id"] not in DRIVERS:
        print(f"RESULT no driver for {entry['id']!r} yet")
        return 1

    godot = find_godot()
    if not godot:
        print("RESULT could not find a Godot .NET binary -- set $GODOT or put it on PATH")
        return 1

    return asyncio.run(run_scene(godot, entry, args.duration, args.verbose))


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
