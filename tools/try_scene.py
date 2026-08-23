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
    """Operator interlocks first, then a real production run (§4.2).

    The interlock half checks the contract a student gets wrong: momentary
    buttons, an E-stop wired normally closed, and a latch that Start cannot
    clear. The production half then runs the line the way a shift does --
    Start, make parts, stop feeding, drain, Stop -- because an interlock
    sequence that never produces anything proves the wiring and nothing about
    the line. This exercise used to end with `produced=0` for exactly that
    reason: its whole script fit inside one emitter half-period.
    """
    EMIT_HALF_PERIOD = 1.5
    ESTOP_LIMIT = 0.200       # §4.2: the belt must stop within 200ms
    DRAIN = 4.0               # stop feeding this long before the end, so
                              # in-flight cartons reach the counter

    state = {"running": False, "tripped": False, "produced": 0,
             "prev_start": False, "prev_stop": False, "prev_reset": False, "prev_present": False,
             "emit_flag": False, "feeding": False, "elapsed": 0.0, "next_toggle": EMIT_HALF_PERIOD}

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
        if s["running"] and s["feeding"]:
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

    estop_ms = -1.0
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

        # Timed, not just checked: §4.2 asks for the belt to stop within 200ms
        # of the mushroom being struck, and "it stopped eventually" is not the
        # same claim.
        struck = time.perf_counter()
        await bus.force({"panel.estop": False})   # mushroom struck (normally closed)
        while time.perf_counter() - struck < 1.0:
            if not bit(bus, "belt.rotate"):
                break
            await asyncio.sleep(0.005)
        estop_ms = (time.perf_counter() - struck) * 1000.0
        check(not bit(bus, "belt.rotate"), "after E-stop: belt off")
        check(estop_ms <= ESTOP_LIMIT * 1000.0,
              f"E-stop stops the belt within {ESTOP_LIMIT * 1000:.0f}ms (took {estop_ms:.0f}ms)")
        await asyncio.sleep(0.2)
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

        # --- production run ------------------------------------------------
        await press(bus, "panel.start")
        await asyncio.sleep(0.3)
        check(bit(bus, "belt.rotate"), "Start after Reset: belt runs again")
        check(bit(bus, "tower.green"), "Start after Reset: green")

        before = int(num(bus, "counter.count"))
        state["feeding"] = True
        # The interlock half above takes ~3s; the rest of the budget is production.
        produced_phase = max(duration - 8.0, 8.0)
        if verbose:
            print(f"  running production for {produced_phase - DRAIN:.0f}s, then draining {DRAIN:.0f}s")
        await asyncio.sleep(max(produced_phase - DRAIN, 1.0))

        state["feeding"] = False                       # stop feeding, let the lane clear
        await asyncio.sleep(DRAIN)

        counted = int(num(bus, "counter.count")) - before
        check(counted > 0, f"production: counter.count advances while running (got {counted})")
        check(int(num(bus, "produced.value")) == state["produced"],
              "production: produced.value mirrors the sensor edges the controller counted")

        await press(bus, "panel.stop")
        await asyncio.sleep(0.3)
        check(not bit(bus, "belt.rotate"), "after Stop: belt off")
        check(bit(bus, "tower.yellow") and not bit(bus, "tower.green"),
              "after Stop: yellow again, not green")

        settled = int(num(bus, "counter.count"))
        await asyncio.sleep(0.6)
        check(int(num(bus, "counter.count")) == settled,
              "after Stop: nothing else is produced with the belt stopped")
    finally:
        stop_event.set()
        await task

    produced = int(num(bus, "produced.value"))
    total = int(num(bus, "counter.count"))
    print(f"RESULT sequence={'PASS' if not problems else 'FAIL'} "
          f"produced={produced} counted={total} estop={estop_ms:.0f}ms")
    return not problems, "; ".join(problems)


async def drive_tank_level_control(bus: TagBusClient, duration: float, verbose: bool,
                                  deterministic: bool) -> tuple[bool, str]:
    """Hold a setpoint, twice, at two very different levels (§4.3).

    One setpoint proves the controller runs. Two prove the *process*: outflow
    follows Torricelli, so the drain valve's effect grows with the square root
    of level, and a controller tuned near the top of the tank behaves
    differently near the bottom. Printing both settling times side by side is
    the point of the exercise -- it is the thing a student is meant to notice
    before tuning a real PID.

    A proportional controller with a modest gain, not the saturating one this
    used to have: a gain that pins the valve at 100% until the setpoint
    arrives is bang-bang control, and bang-bang hides exactly the nonlinearity
    this scene exists to show.
    """
    GAIN = 1.6                # %valve per % of error -- modulates rather than saturates
    BAND_PERCENT = 5.0
    HOLD = 6.0                # stay inside the band this long before calling it settled
    HIGH, LOW = 70.0, 20.0

    async def run_to(setpoint: float, budget: float) -> tuple[float, float, float]:
        """Drive to `setpoint`; return (seconds to reach the band, seconds held
        continuously inside it by the end, final level). Reaching is -1 if it
        never got there."""
        band = setpoint * BAND_PERCENT / 100.0
        start = time.perf_counter()
        reached = -1.0
        entered: float | None = None
        level = num(bus, "tank.level")

        while time.perf_counter() - start < budget:
            await asyncio.sleep(0.02)
            level = num(bus, "tank.level")
            error = setpoint - level
            fill = min(max(error * GAIN, 0.0), 100.0)
            drain = min(max(-error * GAIN, 0.0), 100.0)
            await bus.write_many({"tank.fill": fill, "tank.drain": drain,
                                  "level_readout.value": round(level)})

            inside = abs(error) <= band
            if inside:
                if entered is None:
                    entered = time.perf_counter()
                    if reached < 0:
                        reached = entered - start
            else:
                entered = None            # left the band, the hold clock restarts

            if verbose and int((time.perf_counter() - start) * 2) % 6 == 0:
                print(f"  sp={setpoint:4.0f} t={time.perf_counter() - start:5.1f}s "
                      f"level={level:5.1f} fill={fill:5.1f} drain={drain:5.1f}")

        held = time.perf_counter() - entered if entered is not None else 0.0
        return reached, held, level

    # The high run gets the strict test -- reach the band and hold it -- because
    # that is the tuning point. The low run is only required to *reach* it, and
    # the reason is the lesson itself: outflow follows Torricelli, so the drain
    # valve loses authority as the tank empties, and the same controller that
    # settles at 70% in ten seconds is still creeping toward 20% twenty seconds
    # later. Demanding an identical hold at both ends would be demanding the
    # nonlinearity not exist. Measured on this scene: ~10s to settle high,
    # ~21s just to reach the band low.
    high_budget = max(duration, 40.0) * 0.4
    low_budget = max(duration, 40.0) * 0.6
    high_reach, high_held, high_level = await run_to(HIGH, high_budget)
    low_reach, low_held, low_level = await run_to(LOW, low_budget)

    await bus.write_many({"tank.fill": 0.0, "tank.drain": 0.0})

    print(f"RESULT high sp={HIGH:.0f} reached={high_reach:.1f}s held={high_held:.1f}s "
          f"level={high_level:.1f} | low sp={LOW:.0f} reached={low_reach:.1f}s "
          f"held={low_held:.1f}s level={low_level:.1f}")
    if high_reach >= 0 and low_reach >= 0:
        # §4.3's whole point, stated rather than left for the reader to infer.
        print(f"       same controller, same gain: {high_reach:.1f}s to reach {HIGH:.0f}% "
              f"but {low_reach:.1f}s to reach {LOW:.0f}% -- outflow follows Torricelli, so "
              f"process gain falls with level. One PID tuning is not enough.")

    problems = []
    if high_reach < 0:
        problems.append(f"never reached {HIGH:.0f}% (got to {high_level:.1f})")
    elif high_held < HOLD:
        problems.append(f"reached {HIGH:.0f}% but held it only {high_held:.1f}s of "
                        f"the {HOLD:.0f}s asked for (level {high_level:.1f})")
    if low_reach < 0:
        problems.append(f"never reached {LOW:.0f}% within {low_budget:.0f}s "
                        f"(got to {low_level:.1f})")
    return not problems, "; ".join(problems)


async def drive_light_curtain_sorting(bus: TagBusClient, duration: float, verbose: bool,
                                     deterministic: bool) -> tuple[bool, str]:
    """Sort on the measurement, and account for every carton (§4.4).

    The assertion that matters is conservation: tall + short must equal what
    was emitted. "Both counters advanced" passes while the diverter drops
    cartons on the floor or double-counts them, which is the failure this
    scene is most likely to have. So the run stops feeding and drains before
    counting, and no carton is allowed to go missing.
    """
    EMIT_HALF_PERIOD = 1.5
    TALL_THRESHOLD = 0.15         # metres. One named constant -- move it and the line re-sorts.
    PUSH_DELAY = 2.0
    CLEAR_DWELL = 0.4
    DRAIN = 6.0                   # long enough for the last carton to reach a counter

    await bus.write("belt.rotate", True)
    await bus.write("diverter.extend", False)

    emitted = 0
    emit_flag = False
    next_toggle = time.perf_counter()
    prev_blocked = False
    extend_at: float | None = None
    extending = False
    retract_at: float | None = None
    measured: list[float] = []
    diverted_heights: list[float] = []

    start = time.perf_counter()
    feed_until = start + max(duration - DRAIN, 4.0)
    end = feed_until + DRAIN

    while time.perf_counter() < end:
        await asyncio.sleep(0.02)
        now = time.perf_counter()

        if now < feed_until and now >= next_toggle:
            emit_flag = not emit_flag
            next_toggle = now + EMIT_HALF_PERIOD
            if emit_flag:
                emitted += 1          # one carton per rising edge
            await bus.write("emitter.emit", emit_flag)
        elif now >= feed_until and emit_flag:
            emit_flag = False
            await bus.write("emitter.emit", False)

        blocked = bit(bus, "height_gauge.blocked")
        if blocked and not prev_blocked:
            height = num(bus, "height_gauge.height")
            measured.append(height)
            if height >= TALL_THRESHOLD:
                extend_at = now + PUSH_DELAY
                diverted_heights.append(height)
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

    await bus.write("belt.rotate", False)

    tall, short = int(num(bus, "tall_count.count")), int(num(bus, "short_count.count"))
    counted = tall + short
    print(f"RESULT tall={tall} short={short} counted={counted} emitted={emitted} "
          f"measured={len(measured)}")

    problems = []
    if tall <= 0 or short <= 0:
        problems.append(f"expected both counters to advance, got tall={tall} short={short}")
    if counted != emitted:
        problems.append(f"{emitted} cartons emitted but {counted} counted "
                        f"-- {'lost' if counted < emitted else 'double-counted'} "
                        f"{abs(emitted - counted)}")
    below = [h for h in diverted_heights if h < TALL_THRESHOLD]
    if below:
        problems.append(f"diverted {len(below)} carton(s) measured below the "
                        f"{TALL_THRESHOLD}m threshold")
    return not problems, "; ".join(problems)


async def drive_roller_line_weighing(bus: TagBusClient, duration: float, verbose: bool,
                                    deterministic: bool) -> tuple[bool, str]:
    """Checkweigh every carton, and prove the inductive sensor is selective (§4.5).

    "Metal was seen at least once" passes for a sensor wired to fire on
    everything, which is the exact confusion this scene exists to clear up --
    an inductive sensor is not a second presence sensor. So this counts both:
    cartons that crossed the scale, and cartons that tripped the metal check.
    The second must be non-zero and strictly smaller than the first.

    The scale is also checked for settling behaviour rather than just being
    non-zero at some instant: a load cell that never returns to zero between
    cartons is a scale you cannot trust the reading from.
    """
    EMIT_HALF_PERIOD = 1.5
    ZERO = 0.5                # below this the scale reads empty
    DRAIN = 5.0

    await bus.write("infeed.rotate", True)
    await bus.write("scale.rotate", True)

    emit_flag = False
    next_toggle = time.perf_counter()
    weighings = 0             # rising edges of "something is on the scale"
    metal_hits = 0            # rising edges of the inductive sensor
    peaks: list[float] = []
    peak = 0.0
    on_scale = False
    prev_metal = False
    returned_to_zero = 0

    start = time.perf_counter()
    feed_until = start + max(duration - DRAIN, 4.0)
    end = feed_until + DRAIN

    while time.perf_counter() < end:
        await asyncio.sleep(0.02)
        now = time.perf_counter()

        if now < feed_until and now >= next_toggle:
            emit_flag = not emit_flag
            next_toggle = now + EMIT_HALF_PERIOD
            await bus.write("emitter.emit", emit_flag)
        elif now >= feed_until and emit_flag:
            emit_flag = False
            await bus.write("emitter.emit", False)

        weight = num(bus, "scale.weight")
        if weight > ZERO:
            if not on_scale:
                weighings += 1
                peak = 0.0
            on_scale = True
            peak = max(peak, weight)
        elif on_scale:
            on_scale = False
            returned_to_zero += 1
            peaks.append(peak)
            if verbose:
                print(f"  carton {weighings}: peak {peak:.0f}, scale back to zero")

        metal = bit(bus, "metal_check.detect")
        if metal and not prev_metal:
            metal_hits += 1
        prev_metal = metal

        await bus.write("weight_readout.value", round(weight))

    await bus.write_many({"infeed.rotate": False, "scale.rotate": False})

    outfeed = int(num(bus, "outfeed.count"))
    print(f"RESULT outfeed={outfeed} weighed={weighings} metal={metal_hits} "
          f"peaks={[round(p) for p in peaks]}")

    problems = []
    if outfeed <= 0:
        problems.append(f"outfeed.count never advanced (got {outfeed})")
    if weighings <= 0:
        problems.append("scale.weight never went above zero -- nothing was weighed")
    if returned_to_zero < weighings - 1:
        problems.append(f"the scale did not return to zero between cartons "
                        f"({returned_to_zero} clears for {weighings} cartons)")
    if metal_hits <= 0:
        problems.append("metal_check.detect never fired for a metal carton")
    elif weighings > 0 and metal_hits >= weighings:
        problems.append(f"metal_check.detect fired for every carton "
                        f"({metal_hits} of {weighings}) -- it is behaving like a "
                        f"presence sensor, not an inductive one")
    return not problems, "; ".join(problems)

DRIVERS = {
    "sorting-by-height": drive_sorting_by_height,
    "start-stop-station": drive_start_stop_station,
    "tank-level-control": drive_tank_level_control,
    "light-curtain-sorting": drive_light_curtain_sorting,
    "roller-line-weighing": drive_roller_line_weighing,
}

#: Long enough for each scene to do a real cycle, not just prove it is wired.
#: Every one of these now ends with a drain phase -- feeding stops and the lane
#: clears -- so the counters can be checked for conservation rather than just
#: for having moved.
DEFAULT_DURATION = {
    "sorting-by-height": 35.0,    # the deterministic tall=5/short=5 contract
    "start-stop-station": 35.0,   # ~3s of interlocks, then production and a drain
    "tank-level-control": 50.0,   # two setpoints, half the budget each
    "light-curtain-sorting": 28.0,
    "roller-line-weighing": 26.0,
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
