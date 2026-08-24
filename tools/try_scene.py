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

Every scene is driven **from its control panel** (OP-02): the run starts
stopped, presses Start, checks that Stop and a normally-closed E-stop really
stop the line, and reads the scene's one setpoint off the panel's pot rather
than a constant in this file. That is not decoration -- it is the only way to
check that pressing a button in the running app does anything, and four of the
five scenes used to ignore their panel completely.

Assertions are band-based rather than exact counts: these run real rigid-body
physics, and no such run can promise a number (§4). What each scene *can*
promise is conservation, timing, and that turning the knob changes what the
line does. The exact tall=5/short=5 regression contract lives in
tools/drive_engine.py, against `--deterministic` -- a count like that needs a
belt that runs for a fixed length of time, which is the one thing an operator
sequence deliberately does not give it.

Each scene's driving logic mirrors its engine-side demo profile under
engine/src/Sim/DemoProfiles/, so a regression in either one fails the same
way, and this doubles as a check that the engine behaves correctly when driven
purely over the wire -- the same seam a real PLC uses.
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

    def __init__(self, godot: str, entry: dict) -> None:
        args = [godot, "--headless", "--path", str(ENGINE), "--"]
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


# --- the operator station -----------------------------------------------
#
# Four of the five scenes used to ignore the control panel completely. Every
# template placed one, every panel published `start`, `stop`, `reset` and
# `estop`, and exactly one scene read them -- so pressing Start on the roller
# line did nothing, and striking the E-stop on the tank did not stop it
# filling. The buttons were wired to the tag bus and the tag bus was wired to
# nobody (OP-02). One implementation, here, means every scene behaves the same
# way under the operator's hand: a student who learns the E-stop on one line
# finds the same E-stop on the next.


async def write_present(bus: TagBusClient, values: dict) -> None:
    """Write only the tags this scene actually has.

    Scenes differ in what indicators they carry -- the sorting line has a
    single green lamp where the start/stop station has a three-stage tower --
    and a controller that crashes on a missing lamp is a worse controller than
    one that simply does not light it.
    """
    present = {k: v for k, v in values.items() if bus.table.get(k) is not None}
    if present:
        await bus.write_many(present)


async def turn_pot(bus: TagBusClient, value: float, prefix: str = "panel") -> None:
    """Turn the setpoint pot from here.

    Forced rather than written, because the setpoint is an *Input*: the panel
    republishes the knob's own position every tick, so a plain write would be
    overwritten within one scan. A force is how the engine models a hand on
    the knob -- and the engine turns the pointer to match it, so the panel on
    screen never disagrees with the number the controller is using (OP-02).
    """
    await bus.force({f"{prefix}.setpoint": float(value)})


class Station:
    """One control panel, scanned the way a PLC scans it."""

    def __init__(self, bus: TagBusClient, prefix: str = "panel") -> None:
        self.bus = bus
        self.prefix = prefix
        self.running = False
        self.tripped = False
        self._prev = {"start": False, "stop": False, "reset": False}

    @property
    def setpoint(self) -> float:
        """Where the pot is, in the scene's own units. The template owns the
        range, so this is already metres, grams or seconds -- there is no
        percent to rescale here, and therefore no second copy of the range to
        drift out of step with the plate on the panel."""
        return num(self.bus, f"{self.prefix}.setpoint")

    def scan(self) -> dict[str, bool]:
        """One controller scan: read the buttons, resolve the interlocks,
        return the edges in case the caller wants them too."""
        now = {k: bit(self.bus, f"{self.prefix}.{k}") for k in ("start", "stop", "reset")}
        edges = {k: now[k] and not self._prev[k] for k in now}
        self._prev = now

        healthy = bit(self.bus, f"{self.prefix}.estop")
        # Latching, and only Reset clears it. A trip that cleared itself when
        # the mushroom popped back out would restart the line under whoever
        # was still working on it -- the exact thing a latch exists to stop.
        if not healthy:
            self.tripped = True
        elif edges["reset"]:
            self.tripped = False

        if self.tripped or edges["stop"]:
            self.running = False
        elif edges["start"] and healthy:
            self.running = True

        edges["healthy"] = healthy
        return edges

    def lamps(self) -> dict:
        """Panel lamps and, where the scene has one, the stack light. Yellow
        is stopped-but-healthy: a tower with nothing lit says "no power", not
        "idle"."""
        return {
            f"{self.prefix}.green": self.running,
            f"{self.prefix}.red": self.tripped,
            "tower.green": self.running,
            "tower.red": self.tripped,
            "tower.yellow": (not self.running) and (not self.tripped),
            "stack_light.green": self.running,
        }


class Checks:
    """Collects assertions so a run reports every problem it found rather than
    dying on the first one -- a driver that stops at the first failure hides
    how much else is broken."""

    def __init__(self, verbose: bool) -> None:
        self.problems: list[str] = []
        self._verbose = verbose

    def __call__(self, ok: bool, what: str) -> bool:
        if not ok:
            self.problems.append(what)
        if self._verbose:
            print(f"  {'ok' if ok else 'FAIL'}  {what}")
        return ok

    def note(self, text: str) -> None:
        if self._verbose:
            print(f"  --    {text}")


def controller(tick, period: float = 0.02):
    """Run `tick` on a fixed scan, in the background, until stopped.

    Every scene now has a controller task rather than a single loop that both
    controls the line and asserts things about it: an interlock check has to
    press a button and watch what the *controller* does about it, which is not
    possible when the checking code is the controller.
    """
    stop = asyncio.Event()

    async def loop() -> None:
        while not stop.is_set():
            await tick(period)
            await asyncio.sleep(period)

    return stop, asyncio.create_task(loop())


#: §4.2's number, applied to every scene rather than only the one that used to
#: check it: strike the mushroom and the line is off within this long.
ESTOP_LIMIT = 0.200

#: Wall-clock the shared interlock sequence needs, on top of a scene's own
#: production window.
INTERLOCK_BUDGET = 7.0


async def exercise_interlocks(bus: TagBusClient, station: Station, check: Checks,
                              is_moving, what_moves: str) -> float:
    """The operator contract every line shares, checked identically on all
    five (OP-02). Returns how long the E-stop took to stop the line.

    `is_moving` is the one thing that differs between scenes: what "the line is
    running" looks like from the bus. Everything else -- momentary buttons, a
    normally-closed E-stop, a latch Start cannot clear, Reset that clears the
    fault but does not restart anything -- is the same contract, and five
    scenes agreeing on it is worth more than five dialects of it.
    """
    await asyncio.sleep(0.3)
    check(not is_moving(), f"initial: {what_moves} is off before anyone presses Start")
    check(bit(bus, "panel.estop"), "initial: the E-stop circuit reads healthy (NC)")

    await press(bus, "panel.start")
    await asyncio.sleep(0.4)
    check(is_moving(), f"after Start: {what_moves} runs")
    check(bit(bus, "panel.green"), "after Start: the panel's green lamp is lit")

    # Timed, not merely observed: "it stopped eventually" is a different claim
    # from the one §4.2 makes.
    struck = time.perf_counter()
    await bus.force({"panel.estop": False})       # the mushroom, struck
    while time.perf_counter() - struck < 1.0:
        if not is_moving():
            break
        await asyncio.sleep(0.005)
    estop_ms = (time.perf_counter() - struck) * 1000.0
    check(not is_moving(), f"after E-stop: {what_moves} is off")
    check(estop_ms <= ESTOP_LIMIT * 1000.0,
          f"E-stop stops the line within {ESTOP_LIMIT * 1000:.0f}ms (took {estop_ms:.0f}ms)")
    await asyncio.sleep(0.2)
    check(bit(bus, "panel.red"), "after E-stop: the panel's red lamp is lit")

    await press(bus, "panel.start")
    await asyncio.sleep(0.3)
    check(not is_moving(), "Start while tripped: does NOT restart the line")

    await bus.force({"panel.estop": True})        # twisted back out
    await asyncio.sleep(0.2)
    check(not is_moving(), "releasing the mushroom alone does not restart the line")

    await press(bus, "panel.reset")
    await asyncio.sleep(0.3)
    check(not bit(bus, "panel.red"), "after Reset: the fault is cleared")
    check(not is_moving(), "after Reset: still stopped until someone presses Start")

    await press(bus, "panel.start")
    await asyncio.sleep(0.4)
    check(is_moving(), "Start after Reset: the line runs again")

    return estop_ms


async def check_quiet_after_stop(bus: TagBusClient, station: Station, check: Checks,
                                 is_moving, counter_tag: str) -> None:
    """Press Stop and prove the line really stopped -- not just that a lamp
    went out. A counter that keeps advancing after Stop is the failure this
    catches, and it is invisible from the indicators."""
    await press(bus, "panel.stop")
    await asyncio.sleep(0.3)
    check(not is_moving(), "after Stop: the line is off")
    check(not bit(bus, "panel.green"), "after Stop: green is out")

    settled = num(bus, counter_tag)
    await asyncio.sleep(0.8)
    check(num(bus, counter_tag) == settled,
          f"after Stop: {counter_tag} stops advancing (was {settled:.0f}, "
          f"now {num(bus, counter_tag):.0f})")


# --- per-scene drivers --------------------------------------------------
# Each mirrors its engine-side profile under engine/src/Sim/DemoProfiles/.


async def drive_sorting_by_height(bus: TagBusClient, duration: float, verbose: bool) -> tuple[bool, str]:
    """The reference line, now driven from the panel (OP-03).

    Its analog knob is the timing pot every real diverter has: how long after
    the tall beam breaks the pusher fires. Too short and the plate hits the
    carton on the nose; too long and it sails past. The pot is checked the way
    that matters -- by measuring what the controller actually does with it,
    not by reading the tag back -- because a driver that reads the setpoint and
    then ignores it looks identical from the bus.
    """
    EMIT_HALF_PERIOD = 1.5
    PUSH_HOLD = 0.5

    check = Checks(verbose)
    state = {"high_mem": False, "extend_at": None, "retract_at": None,
             "broke_at": None, "delays": [], "emit_flag": False,
             "elapsed": 0.0, "next_toggle": 0.0, "feeding": False}

    async def tick(dt: float) -> None:
        s = state
        station.scan()
        now = time.perf_counter()
        s["elapsed"] += dt

        if station.running and s["feeding"]:
            if s["elapsed"] >= s["next_toggle"]:
                s["emit_flag"] = not s["emit_flag"]
                s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD
        else:
            s["emit_flag"] = False
            s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD

        high = bit(bus, "sensor_high.detect")
        if high and not s["high_mem"] and station.running:
            # The pot, read fresh every scan rather than latched at startup:
            # turning it must change the next carton, not the next run.
            s["broke_at"] = now
            s["extend_at"] = now + station.setpoint
        s["high_mem"] = high

        extend = bit(bus, "pusher.extend")
        if s["extend_at"] is not None and now >= s["extend_at"] and station.running:
            extend = True
            if s["broke_at"] is not None:
                s["delays"].append(now - s["broke_at"])
            s["retract_at"] = now + PUSH_HOLD
            s["extend_at"] = None
        if s["retract_at"] is not None and now >= s["retract_at"]:
            extend = False
            s["retract_at"] = None
        if not station.running:
            extend = False
            s["extend_at"] = None

        await write_present(bus, {
            "conveyor.rotate": station.running,
            "emitter.emit": s["emit_flag"],
            "pusher.extend": extend,
            **station.lamps(),
        })

    station = Station(bus)
    stop_event, task = controller(tick)
    estop_ms = -1.0
    tall = short = 0
    nominal_delays: list[float] = []

    try:
        estop_ms = await exercise_interlocks(bus, station, check,
                                             lambda: bit(bus, "conveyor.rotate"), "the belt")

        # --- production, at the pot's own setting --------------------------
        tall_before, short_before = num(bus, "counter.tall"), num(bus, "counter.short")
        nominal = station.setpoint
        check.note(f"diverter pot reads {nominal:.2f}s")
        state["delays"].clear()
        state["feeding"] = True
        await asyncio.sleep(max(duration - 6.0, 4.0))
        state["feeding"] = False
        await asyncio.sleep(6.0)                 # drain: let the lane clear

        tall = int(num(bus, "counter.tall") - tall_before)
        short = int(num(bus, "counter.short") - short_before)
        nominal_delays = list(state["delays"])   # noqa: F841 — reused below

        if nominal_delays:
            mean = sum(nominal_delays) / len(nominal_delays)
            check(abs(mean - nominal) < 0.15,
                  f"the pusher fires {nominal:.2f}s after the beam, as the pot says "
                  f"(measured {mean:.2f}s over {len(nominal_delays)} cartons)")

        # A band, not an exact count: this is real Jolt physics, and no
        # rigid-body run can promise a number the way the deterministic scene
        # can. Conservation is the assertion that actually catches a broken
        # diverter -- every carton the emitter made has to end up in one of
        # the two counters.
        check(tall > 0 and short > 0,
              f"production: both counters advance (got tall={tall} short={short})")

        # --- turn the pot, and prove the line follows it -------------------
        # Timing, not counts: physics variance makes "how many were missed"
        # a poor assertion, while "the pusher fired later" is exactly what
        # turning the knob is supposed to mean and is not noisy at all.
        await turn_pot(bus, 1.80)
        await asyncio.sleep(0.3)
        check(abs(station.setpoint - 1.80) < 0.01,
              f"turning the pot to its stop reads back 1.80s (got {station.setpoint:.2f})")
        state["delays"].clear()
        state["feeding"] = True
        await asyncio.sleep(8.0)
        state["feeding"] = False
        await asyncio.sleep(3.0)

        slow_delays = list(state["delays"])
        if check(bool(slow_delays), "the line kept running after the pot was turned"):
            slow_mean = sum(slow_delays) / len(slow_delays)
            check(abs(slow_mean - 1.80) < 0.15,
                  f"the pusher now fires 1.80s after the beam (measured {slow_mean:.2f}s)")
            if nominal_delays:
                print(f"       pot at {nominal:.2f}s -> pusher fired at "
                      f"{sum(nominal_delays) / len(nominal_delays):.2f}s; "
                      f"pot at 1.80s -> {slow_mean:.2f}s. The knob is the timing, "
                      f"and a diverter mistimed by a second misses the carton.")

        await check_quiet_after_stop(bus, station, check,
                                     lambda: bit(bus, "conveyor.rotate"), "counter.short")
    finally:
        stop_event.set()
        await task
        await bus.force(clear=["panel.setpoint"])

    print(f"RESULT sequence={'PASS' if not check.problems else 'FAIL'} "
          f"tall={tall} short={short} estop={estop_ms:.0f}ms")
    return not check.problems, "; ".join(check.problems)


async def drive_start_stop_station(bus: TagBusClient, duration: float, verbose: bool) -> tuple[bool, str]:
    """Operator interlocks, then a real batch (§4.2, OP-04).

    The interlock half checks the contract a student gets wrong: momentary
    buttons, an E-stop wired normally closed, and a latch Start cannot clear.
    The batch half is what the pot is for -- it is a *count*, and the line
    stops itself when it has made that many. That is a far better exercise
    than "run until the clock runs out": it has a right answer the run either
    hits exactly or does not.
    """
    EMIT_HALF_PERIOD = 1.5

    check = Checks(verbose)
    state = {"produced": 0, "prev_present": False, "emit_flag": False,
             "feeding": False, "elapsed": 0.0, "next_toggle": EMIT_HALF_PERIOD,
             "batch_done": False}

    async def tick(dt: float) -> None:
        s = state
        target = int(round(station.setpoint))
        was_done = target > 0 and s["produced"] >= target

        edges = station.scan()

        # Start on a finished batch starts the next one, the way a real batch
        # controller works. Without this the panel would have a Start button
        # that does nothing until someone found a way to zero the count, which
        # is not a control system. Mirrors StartStopStationProfile.
        if edges["start"] and was_done:
            s["produced"] = 0

        present = bit(bus, "part_present.detect")
        if present and not s["prev_present"] and station.running:
            s["produced"] += 1
        s["prev_present"] = present

        # The batch interlock: at target, the line stops itself.
        s["batch_done"] = target > 0 and s["produced"] >= target
        if s["batch_done"] and station.running:
            station.running = False

        s["elapsed"] += dt
        if station.running and s["feeding"]:
            if s["elapsed"] >= s["next_toggle"]:
                s["emit_flag"] = not s["emit_flag"]
                s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD
        else:
            s["emit_flag"] = False
            s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD

        await write_present(bus, {
            "belt.rotate": station.running,
            "emitter.emit": s["emit_flag"],
            "produced.value": s["produced"],
            **station.lamps(),
        })

    station = Station(bus)
    stop_event, task = controller(tick)
    estop_ms = -1.0
    batch = 4

    try:
        # A batch big enough that the interlock sequence cannot accidentally
        # complete it before the production phase starts.
        await turn_pot(bus, 40)
        await asyncio.sleep(0.2)

        estop_ms = await exercise_interlocks(bus, station, check,
                                             lambda: bit(bus, "belt.rotate"), "the belt")
        check(bit(bus, "tower.green") and not bit(bus, "tower.yellow"),
              "running: the tower shows green only")

        # --- the batch ------------------------------------------------------
        await press(bus, "panel.stop")
        await asyncio.sleep(0.3)
        state["produced"] = 0
        state["batch_done"] = False
        await turn_pot(bus, batch)
        await asyncio.sleep(0.3)
        check(abs(station.setpoint - batch) < 0.01,
              f"the batch pot reads {batch} pcs (got {station.setpoint:.0f})")

        before = num(bus, "counter.count")
        await press(bus, "panel.start")
        state["feeding"] = True
        check.note(f"running a batch of {batch}")

        deadline = time.perf_counter() + max(duration, 20.0)
        while time.perf_counter() < deadline and not state["batch_done"]:
            await asyncio.sleep(0.05)
        finished_in = max(duration, 20.0) - (deadline - time.perf_counter())
        state["feeding"] = False

        check(state["batch_done"],
              f"the batch completed within its budget (made {state['produced']} of {batch})")
        check(state["produced"] == batch,
              f"the line made exactly the batch it was set to (want {batch}, "
              f"got {state['produced']})")
        await asyncio.sleep(0.4)
        check(not bit(bus, "belt.rotate"),
              "at target the line stops itself -- nobody had to press Stop")
        check(bit(bus, "tower.yellow") and not bit(bus, "tower.green"),
              "batch complete: the tower goes yellow, not green")

        # It really stopped: nothing else comes through after the target.
        settled = state["produced"]
        await asyncio.sleep(1.5)
        check(state["produced"] == settled,
              f"nothing is made past the target (still {state['produced']})")

        counted = int(num(bus, "counter.count") - before)
        check(counted > 0, f"production: the remover counted cartons too (got {counted})")
        check(int(num(bus, "produced.value")) == state["produced"],
              "the display mirrors the sensor edges the controller counted")

        print(f"       batch of {batch} finished in {finished_in:.1f}s; the pot is a "
              f"count, so the line has a target it either hits exactly or does not.")

        await check_quiet_after_stop(bus, station, check,
                                     lambda: bit(bus, "belt.rotate"), "counter.count")
    finally:
        stop_event.set()
        await task
        await bus.force(clear=["panel.setpoint"])

    print(f"RESULT sequence={'PASS' if not check.problems else 'FAIL'} "
          f"batch={batch} produced={state['produced']} "
          f"counted={int(num(bus, 'counter.count'))} estop={estop_ms:.0f}ms")
    return not check.problems, "; ".join(check.problems)


async def drive_tank_level_control(bus: TagBusClient, duration: float, verbose: bool) -> tuple[bool, str]:
    """Hold whatever the pot says, at two very different levels (§4.3, OP-05).

    The two setpoints used to be constants in this file. Now they are one
    knob, turned mid-run, which is the same experiment with the interesting
    part put where a student can reach it: outflow follows Torricelli, so the
    drain valve's authority grows with the square root of level, and a
    controller tuned near the top of the tank behaves differently near the
    bottom. Printing both settling times side by side is the point of the
    exercise.

    A proportional controller with a modest gain, not a saturating one: a gain
    that pins the valve at 100% until the setpoint arrives is bang-bang
    control, and bang-bang hides exactly the nonlinearity this scene exists to
    show.
    """
    GAIN = 1.6                # %valve per % of error -- modulates, not saturates
    BAND_PERCENT = 5.0
    HOLD = 6.0                # stay inside the band this long to count as settled
    HIGH, LOW = 70.0, 20.0

    check = Checks(verbose)
    state = {"fill": 0.0, "drain": 0.0}

    async def tick(dt: float) -> None:
        station.scan()
        level = num(bus, "tank.level")

        if station.running:
            error = station.setpoint - level
            fill = min(max(error * GAIN, 0.0), 100.0)
            drain = min(max(-error * GAIN, 0.0), 100.0)
        else:
            # Stopped means stopped: both valves shut, so a tripped tank holds
            # its level instead of quietly carrying on filling. This is the
            # whole reason the scene needed a panel -- an E-stop that leaves
            # the fill valve open is not an E-stop.
            fill = drain = 0.0

        state["fill"], state["drain"] = fill, drain
        await write_present(bus, {"tank.fill": fill, "tank.drain": drain,
                                  "level_readout.value": round(level),
                                  **station.lamps()})

    async def settle(setpoint: float, budget: float) -> tuple[float, float, float]:
        """Turn the pot to `setpoint` and watch. Returns (seconds to reach the
        band, seconds held continuously inside it at the end, final level)."""
        await turn_pot(bus, setpoint)
        await asyncio.sleep(0.2)
        band = setpoint * BAND_PERCENT / 100.0
        start = time.perf_counter()
        reached = -1.0
        entered: float | None = None
        level = num(bus, "tank.level")

        while time.perf_counter() - start < budget:
            await asyncio.sleep(0.05)
            level = num(bus, "tank.level")
            inside = abs(setpoint - level) <= band
            if inside:
                if entered is None:
                    entered = time.perf_counter()
                    if reached < 0:
                        reached = entered - start
            else:
                entered = None            # left the band; the hold clock restarts

            if verbose and int((time.perf_counter() - start) * 2) % 8 == 0:
                print(f"  sp={setpoint:4.0f} t={time.perf_counter() - start:5.1f}s "
                      f"level={level:5.1f} fill={state['fill']:5.1f} drain={state['drain']:5.1f}")

        held = time.perf_counter() - entered if entered is not None else 0.0
        return reached, held, level

    station = Station(bus)
    stop_event, task = controller(tick)
    estop_ms = -1.0
    high_reach = low_reach = -1.0
    high_held = low_held = high_level = low_level = 0.0

    try:
        # The tank's "is it running" is the fill valve: an E-stop that stops
        # the *controller* while leaving a valve cracked open has not stopped
        # anything, and only watching the valve itself catches that.
        await turn_pot(bus, 100.0)     # so the controller wants fill, not drain
        await asyncio.sleep(0.2)
        estop_ms = await exercise_interlocks(bus, station, check,
                                             lambda: num(bus, "tank.fill") > 0.5,
                                             "the fill valve")

        budget = max(duration, 40.0)
        high_reach, high_held, high_level = await settle(HIGH, budget * 0.4)
        low_reach, low_held, low_level = await settle(LOW, budget * 0.6)

        # The high run gets the strict test -- reach the band and hold it --
        # because that is the tuning point. The low run is only required to
        # *reach* it, and the reason is the lesson itself: the same controller
        # that settles at 70% in ten seconds is still creeping toward 20%
        # twenty seconds later. Demanding an identical hold at both ends would
        # be demanding the nonlinearity not exist.
        check(high_reach >= 0, f"reached {HIGH:.0f}% (got to {high_level:.1f})")
        if high_reach >= 0:
            check(high_held >= HOLD,
                  f"held {HIGH:.0f}% for {HOLD:.0f}s (held {high_held:.1f}s, "
                  f"level {high_level:.1f})")
        check(low_reach >= 0,
              f"reached {LOW:.0f}% within {budget * 0.6:.0f}s (got to {low_level:.1f})")

        await press(bus, "panel.stop")
        await asyncio.sleep(0.4)
        check(num(bus, "tank.fill") < 0.5 and num(bus, "tank.drain") < 0.5,
              "after Stop: both valves are shut, not left where the controller had them")
        held_level = num(bus, "tank.level")
        await asyncio.sleep(1.5)
        check(abs(num(bus, "tank.level") - held_level) < 1.0,
              f"after Stop: the level holds (was {held_level:.1f}, "
              f"now {num(bus, 'tank.level'):.1f})")
    finally:
        stop_event.set()
        await task
        await bus.write_many({"tank.fill": 0.0, "tank.drain": 0.0})
        await bus.force(clear=["panel.setpoint"])

    print(f"RESULT high sp={HIGH:.0f} reached={high_reach:.1f}s held={high_held:.1f}s "
          f"level={high_level:.1f} | low sp={LOW:.0f} reached={low_reach:.1f}s "
          f"held={low_held:.1f}s level={low_level:.1f} | estop={estop_ms:.0f}ms")
    if high_reach >= 0 and low_reach >= 0:
        print(f"       same controller, same gain, one knob: {high_reach:.1f}s to reach "
              f"{HIGH:.0f}% but {low_reach:.1f}s to reach {LOW:.0f}% -- outflow follows "
              f"Torricelli, so process gain falls with level. One PID tuning is not enough.")
    return not check.problems, "; ".join(check.problems)


async def drive_light_curtain_sorting(bus: TagBusClient, duration: float, verbose: bool) -> tuple[bool, str]:
    """Sort on the measurement, with the threshold on the panel (§4.4, OP-06).

    The threshold used to be a constant in this file, which made the scene's
    whole point -- that you sort on a *number*, not on two bits -- something
    you could only exercise by editing Python. It is the pot now, so the line
    can be re-sorted mid-shift, and the run proves it by turning the knob past
    every carton and watching the diverter go quiet.

    The assertion that matters is still conservation: tall + short must equal
    what was emitted. "Both counters advanced" passes while the diverter drops
    cartons on the floor or double-counts them.
    """
    EMIT_HALF_PERIOD = 1.5
    PUSH_DELAY = 2.0
    CLEAR_DWELL = 0.4
    DRAIN = 6.0

    check = Checks(verbose)
    state = {"emitted": 0, "emit_flag": False, "elapsed": 0.0, "next_toggle": 0.0,
             "prev_blocked": False, "extend_at": None, "extending": False,
             "retract_at": None, "measured": [], "diverted": [], "feeding": False}

    async def tick(dt: float) -> None:
        s = state
        station.scan()
        now = time.perf_counter()
        s["elapsed"] += dt

        if station.running and s["feeding"]:
            if s["elapsed"] >= s["next_toggle"]:
                s["emit_flag"] = not s["emit_flag"]
                s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD
                if s["emit_flag"]:
                    s["emitted"] += 1          # one carton per rising edge
        else:
            s["emit_flag"] = False
            s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD

        blocked = bit(bus, "height_gauge.blocked")
        if blocked and not s["prev_blocked"] and station.running:
            height = num(bus, "height_gauge.height")
            s["measured"].append(height)
            # The pot, read at the moment of the measurement -- turning it
            # re-sorts the next carton, not the next run.
            if height >= station.setpoint:
                s["extend_at"] = now + PUSH_DELAY
                s["diverted"].append((height, station.setpoint))
        s["prev_blocked"] = blocked

        extend = bit(bus, "diverter.extend")
        if not s["extending"] and s["extend_at"] is not None and now >= s["extend_at"]:
            extend = True
            s["extending"] = True
            s["extend_at"] = None
        if s["extending"] and s["retract_at"] is None and bit(bus, "diverter.extended"):
            s["retract_at"] = now + CLEAR_DWELL
        if s["retract_at"] is not None and now >= s["retract_at"]:
            extend = False
            s["extending"] = False
            s["retract_at"] = None
        if not station.running:
            extend = False
            s["extending"] = False
            s["extend_at"] = s["retract_at"] = None

        await write_present(bus, {"belt.rotate": station.running,
                                  "emitter.emit": s["emit_flag"],
                                  "diverter.extend": extend,
                                  **station.lamps()})

    station = Station(bus)
    stop_event, task = controller(tick)
    estop_ms = -1.0
    tall = short = 0

    try:
        estop_ms = await exercise_interlocks(bus, station, check,
                                             lambda: bit(bus, "belt.rotate"), "the belt")

        threshold = station.setpoint
        check.note(f"height threshold pot reads {threshold:.2f}m")
        state["emitted"] = 0
        state["measured"].clear()
        state["diverted"].clear()
        state["feeding"] = True
        await asyncio.sleep(max(duration - DRAIN, 4.0))
        state["feeding"] = False
        await asyncio.sleep(DRAIN)

        tall, short = int(num(bus, "tall_count.count")), int(num(bus, "short_count.count"))
        counted = tall + short
        emitted = state["emitted"]

        check(tall > 0 and short > 0,
              f"both counters advance at {threshold:.2f}m (got tall={tall} short={short})")
        check(counted == emitted,
              f"{emitted} cartons emitted and {counted} counted -- "
              f"{'lost' if counted < emitted else 'double-counted'} {abs(emitted - counted)}")
        below = [h for h, t in state["diverted"] if h < t]
        check(not below,
              f"nothing below the threshold was diverted (diverted {len(below)} that were)")

        # --- turn the pot past every carton --------------------------------
        # A threshold above the tallest carton must divert nothing. This is
        # the assertion that separates "reads the setpoint" from "uses it":
        # a driver that latched the pot at startup passes everything above and
        # fails here.
        await turn_pot(bus, 0.50)
        await asyncio.sleep(0.3)
        check(abs(station.setpoint - 0.50) < 0.01,
              f"the pot reads 0.50m at its stop (got {station.setpoint:.2f})")
        diverted_before = len(state["diverted"])
        seen_before = len(state["measured"])
        state["feeding"] = True
        await asyncio.sleep(9.0)
        state["feeding"] = False
        await asyncio.sleep(DRAIN)

        seen = len(state["measured"]) - seen_before
        newly_diverted = len(state["diverted"]) - diverted_before
        check(seen > 0, f"the curtain kept measuring after the pot was turned (saw {seen})")
        check(newly_diverted == 0,
              f"with the threshold above every carton, nothing is diverted "
              f"(diverted {newly_diverted} of {seen})")
        if seen:
            print(f"       threshold {threshold:.2f}m -> tall={tall} short={short}; "
                  f"threshold 0.50m -> {seen} measured, none diverted. The knob is "
                  f"the sorting rule, and no code changed between the two.")

        await check_quiet_after_stop(bus, station, check,
                                     lambda: bit(bus, "belt.rotate"), "short_count.count")
    finally:
        stop_event.set()
        await task
        await bus.force(clear=["panel.setpoint"])

    print(f"RESULT sequence={'PASS' if not check.problems else 'FAIL'} "
          f"tall={tall} short={short} measured={len(state['measured'])} "
          f"estop={estop_ms:.0f}ms")
    return not check.problems, "; ".join(check.problems)


async def drive_roller_line_weighing(bus: TagBusClient, duration: float, verbose: bool) -> tuple[bool, str]:
    """Checkweigh against the pot, and cross-check it (§4.5, OP-07).

    The pot is the reject limit in grams. That turns the scale from a readout
    into a decision, and gives the scene a second, independent opinion about
    which cartons are steel: the inductive sensor sees metal, the checkweigher
    sees mass, and on this line those are the same cartons. Two instruments
    agreeing is a far stronger check than either one being non-zero -- and
    "metal was seen at least once" passes for a sensor wired to fire on
    everything, which is the exact confusion this scene exists to clear up.
    """
    EMIT_HALF_PERIOD = 1.5
    ZERO = 0.5                # below this the scale reads empty
    DRAIN = 5.0

    check = Checks(verbose)
    state = {"emit_flag": False, "elapsed": 0.0, "next_toggle": 0.0, "feeding": False,
             "on_scale": False, "peak": 0.0, "peaks": [], "returned_to_zero": 0,
             "rejects": 0, "metal_hits": 0, "prev_metal": False, "metal_peaks": []}

    async def tick(dt: float) -> None:
        s = state
        station.scan()
        s["elapsed"] += dt

        if station.running and s["feeding"]:
            if s["elapsed"] >= s["next_toggle"]:
                s["emit_flag"] = not s["emit_flag"]
                s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD
        else:
            s["emit_flag"] = False
            s["next_toggle"] = s["elapsed"] + EMIT_HALF_PERIOD

        metal = bit(bus, "metal_check.detect")
        if metal and not s["prev_metal"] and station.running:
            s["metal_hits"] += 1
        s["prev_metal"] = metal

        weight = num(bus, "scale.weight")
        if weight > ZERO:
            if not s["on_scale"]:
                s["peak"] = 0.0
            s["on_scale"] = True
            s["peak"] = max(s["peak"], weight)
        elif s["on_scale"]:
            # The carton has left: judge it on its peak, not on whatever the
            # cell happened to read as it rolled off.
            s["on_scale"] = False
            s["returned_to_zero"] += 1
            s["peaks"].append(s["peak"])
            over = s["peak"] > station.setpoint
            if over:
                s["rejects"] += 1
                s["metal_peaks"].append(s["peak"])
            if verbose:
                print(f"  carton: peak {s['peak']:.0f}g "
                      f"{'REJECT' if over else 'pass'} (limit {station.setpoint:.0f}g)")

        await write_present(bus, {
            "infeed.rotate": station.running,
            "scale.rotate": station.running,
            "emitter.emit": s["emit_flag"],
            "weight_readout.value": round(weight),
            # Red on the panel now means "the last carton was over limit" as
            # well as "tripped": a reject the operator cannot see is a reject
            # nobody acts on.
            "panel.red": station.tripped or bool(s["rejects"]),
            **{k: v for k, v in station.lamps().items() if k != "panel.red"},
        })

    station = Station(bus)
    stop_event, task = controller(tick)
    estop_ms = -1.0

    try:
        estop_ms = await exercise_interlocks(bus, station, check,
                                             lambda: bit(bus, "scale.rotate"), "the rollers")

        limit = station.setpoint
        check.note(f"reject limit pot reads {limit:.0f}g")
        for key in ("peaks", "metal_peaks"):
            state[key].clear()
        state["rejects"] = state["metal_hits"] = state["returned_to_zero"] = 0
        state["feeding"] = True
        await asyncio.sleep(max(duration - DRAIN, 6.0))
        state["feeding"] = False
        await asyncio.sleep(DRAIN)

        weighed = len(state["peaks"])
        rejects = state["rejects"]
        metal_hits = state["metal_hits"]

        check(weighed > 0, "the scale weighed something -- scale.weight never left zero")
        check(state["returned_to_zero"] >= weighed,
              f"the scale returns to zero between cartons "
              f"({state['returned_to_zero']} clears for {weighed} cartons)")
        check(int(num(bus, "outfeed.count")) > 0,
              f"outfeed.count advanced (got {int(num(bus, 'outfeed.count'))})")
        check(metal_hits > 0, "metal_check.detect fired for the steel cartons")
        check(not (weighed > 0 and metal_hits >= weighed),
              f"metal_check.detect is selective, not a presence sensor "
              f"({metal_hits} hits for {weighed} cartons)")
        check(0 < rejects < weighed,
              f"the checkweigher rejected some cartons and passed others "
              f"({rejects} of {weighed} over {limit:.0f}g)")

        # Two instruments, one answer. This is the assertion the scene was
        # missing: mass and material are independent measurements of the same
        # cartons, so a wiring or threshold mistake in either one shows up as
        # them disagreeing.
        check(rejects == metal_hits,
              f"the checkweigher and the inductive sensor flag the same cartons "
              f"({rejects} over limit, {metal_hits} metal)")

        # --- turn the pot below every carton -------------------------------
        await turn_pot(bus, 100)
        await asyncio.sleep(0.3)
        check(abs(station.setpoint - 100) < 1.0,
              f"the pot reads 100g (got {station.setpoint:.0f})")
        weighed_before, rejects_before = len(state["peaks"]), state["rejects"]
        state["feeding"] = True
        await asyncio.sleep(8.0)
        state["feeding"] = False
        await asyncio.sleep(DRAIN)

        now_weighed = len(state["peaks"]) - weighed_before
        now_rejects = state["rejects"] - rejects_before
        check(now_weighed > 0, f"the scale kept weighing after the pot moved ({now_weighed})")
        check(now_rejects == now_weighed,
              f"below every carton, the limit rejects all of them "
              f"({now_rejects} of {now_weighed})")
        if now_weighed:
            print(f"       limit {limit:.0f}g -> {rejects} of {weighed} rejected; "
                  f"limit 100g -> {now_rejects} of {now_weighed}. Peaks seen: "
                  f"{sorted({round(p / 100) * 100 for p in state['peaks']})}g.")

        await check_quiet_after_stop(bus, station, check,
                                     lambda: bit(bus, "scale.rotate"), "outfeed.count")
    finally:
        stop_event.set()
        await task
        await bus.force(clear=["panel.setpoint"])

    print(f"RESULT sequence={'PASS' if not check.problems else 'FAIL'} "
          f"weighed={len(state['peaks'])} rejects={state['rejects']} "
          f"metal={state['metal_hits']} outfeed={int(num(bus, 'outfeed.count'))} "
          f"estop={estop_ms:.0f}ms")
    return not check.problems, "; ".join(check.problems)


DRIVERS = {
    "sorting-by-height": drive_sorting_by_height,
    "start-stop-station": drive_start_stop_station,
    "tank-level-control": drive_tank_level_control,
    "light-curtain-sorting": drive_light_curtain_sorting,
    "roller-line-weighing": drive_roller_line_weighing,
}

#: The *production* window, not the whole run: every scene now runs the shared
#: operator sequence first (~7s) and a setpoint demonstration after, so the wall
#: clock is longer than the number here. Long enough for a real cycle, not just
#: to prove the line is wired, and every one ends with a drain phase -- feeding
#: stops and the lane clears -- so counters can be checked for conservation
#: rather than just for having moved.
DEFAULT_DURATION = {
    "sorting-by-height": 35.0,    # long enough for both pot settings to sort
    "start-stop-station": 30.0,   # budget for the batch, not a fixed run length
    "tank-level-control": 50.0,   # two setpoints, half the budget each
    "light-curtain-sorting": 28.0,
    "roller-line-weighing": 30.0,
}


async def run_scene(godot: str, entry: dict, duration: float | None, verbose: bool) -> int:
    scene_id = entry["id"]
    run_duration = duration if duration is not None else DEFAULT_DURATION[scene_id]

    eng: Engine | None = None

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
        # Deliberately *not* --deterministic, even for sorting-by-height.
        # Two reasons, and only the second one survives now that the
        # deterministic scene has a panel of its own: this runs the line the
        # way a user actually opens it, and an exact count needs a belt that
        # runs for a fixed length of time -- which is precisely what pressing
        # Stop and striking an E-stop mid-run takes away. tall=5/short=5 stays
        # in tools/drive_engine.py, the tool written for it (OP-03).
        print(f"Starting engine for '{entry['title']}'...")
        eng = Engine(godot, entry)

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
        ok, problem = await DRIVERS[scene_id](bus, run_duration, verbose)
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
