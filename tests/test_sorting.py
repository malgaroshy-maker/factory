"""End-to-end: a control program actually sorts boxes by height.

`test_scene_sorts_by_height` is deterministic and synchronous -- no sockets, no
wall-clock. It is the regression scene referenced in the plan: when the real
Godot engine replaces the stub, this same controller and these same assertions
must still hold, which is what stops physics tuning from silently breaking
existing scenes.

`test_sorting_over_the_tag_bus` runs the same logic through the real bus to
prove the integration, and asserts outcomes rather than exact timing.
"""

from __future__ import annotations

import asyncio

from scene import (
    BELT_SPEED,
    PUSHER_POS,
    PUSHER_TRAVEL_TIME,
    SENSOR_HIGH_POS,
    SENSOR_WINDOW,
    SortingScene,
)

DT = 0.01
EMIT_INTERVAL = 3.0
PUSHER_HOLD = 0.5

#: A tall box breaks sensor_high when its leading edge enters the window, so it
#: still has this far to travel to reach the pusher. Deriving it from the scene
#: geometry rather than hardcoding keeps the controller honest if the layout moves.
TRAVEL_TO_PUSHER = (PUSHER_POS - (SENSOR_HIGH_POS - SENSOR_WINDOW / 2)) / BELT_SPEED
#: Start extending early enough that the pusher is fully out on arrival.
EXTEND_LEAD = TRAVEL_TO_PUSHER - PUSHER_TRAVEL_TIME


class Controller:
    """What a student's ladder program does, in Python.

    Runs the belt, pulses the emitter, and fires the pusher a fixed time after
    sensor_high sees a tall box.
    """

    def __init__(self, scene: SortingScene, boxes_to_emit: int) -> None:
        self.scene = scene
        self.boxes_to_emit = boxes_to_emit
        self.emitted = 0
        self.t = 0.0
        self._next_emit = 0.0
        self._high_was = False
        self._extend_at: float | None = None
        self._retract_at: float | None = None

    def step(self, dt: float) -> None:
        tags = self.scene.tags
        tags.set("conveyor.rotate", True)
        tags.set("stack_light.green", True)

        # Emit one box per interval, as a one-tick pulse (the scene triggers on
        # the rising edge, exactly like a real emitter).
        emit = False
        if self.emitted < self.boxes_to_emit and self.t >= self._next_emit:
            emit = True
            self.emitted += 1
            self._next_emit = self.t + EMIT_INTERVAL
        tags.set("emitter.emit", emit)

        # Rising edge on the high sensor means a tall box is coming.
        high = bool(tags.visible("sensor_high.detect"))
        if high and not self._high_was:
            self._extend_at = self.t + EXTEND_LEAD
        self._high_was = high

        if self._extend_at is not None and self.t >= self._extend_at:
            tags.set("pusher.extend", True)
            self._retract_at = self._extend_at + PUSHER_HOLD
            self._extend_at = None
        if self._retract_at is not None and self.t >= self._retract_at:
            tags.set("pusher.extend", False)
            self._retract_at = None

        self.t += dt


def test_scene_sorts_by_height():
    """Alternating short/tall boxes must end up in the right places."""
    scene = SortingScene(emit_pattern=[False, True])   # short, tall, short, tall
    controller = Controller(scene, boxes_to_emit=4)

    for _ in range(int(20.0 / DT)):
        controller.step(DT)
        scene.tick(DT)

    assert len(scene.sorted_tall) == 2, (
        f"expected 2 tall boxes diverted, got {len(scene.sorted_tall)}")
    assert len(scene.sorted_short) == 2, (
        f"expected 2 short boxes through, got {len(scene.sorted_short)}")
    assert all(b.is_tall for b in scene.sorted_tall)
    assert all(not b.is_tall for b in scene.sorted_short)
    assert not scene.boxes, "no boxes should be left on the belt"


def test_all_short_boxes_pass_through():
    scene = SortingScene(emit_pattern=[False])
    controller = Controller(scene, boxes_to_emit=3)
    for _ in range(int(16.0 / DT)):
        controller.step(DT)
        scene.tick(DT)
    assert len(scene.sorted_short) == 3
    assert not scene.sorted_tall


def test_all_tall_boxes_are_diverted():
    scene = SortingScene(emit_pattern=[True])
    controller = Controller(scene, boxes_to_emit=3)
    for _ in range(int(16.0 / DT)):
        controller.step(DT)
        scene.tick(DT)
    assert len(scene.sorted_tall) == 3
    assert not scene.sorted_short


def test_counters_track_the_sorted_totals():
    scene = SortingScene(emit_pattern=[False, True])
    controller = Controller(scene, boxes_to_emit=4)
    for _ in range(int(20.0 / DT)):
        controller.step(DT)
        scene.tick(DT)
    assert scene.tags.visible("counter.tall") == len(scene.sorted_tall)
    assert scene.tags.visible("counter.short") == len(scene.sorted_short)


async def test_sorting_over_the_tag_bus(engine, mock):
    """The same job, driven through the real bus by a driver."""
    scene = engine.scene
    scene.emit_pattern = [True]           # all tall, so one divert proves the path

    await mock.set("conveyor.rotate", True)
    await mock.set("emitter.emit", True)
    await asyncio.sleep(0.05)
    await mock.set("emitter.emit", False)

    # The box must break the low sensor on its way past.
    await mock.wait_for("sensor_low.detect", True, timeout=10)
    await mock.wait_for("sensor_high.detect", True, timeout=10)

    # Fire the pusher and confirm the engine reports it extended.
    await mock.set("pusher.extend", True)
    await mock.wait_for("pusher.extended", True, timeout=5)

    # Wait on the tag, not on a polling loop. On Windows, asyncio's clock
    # resolution is 15.6ms and any timer inside that window is treated as
    # already expired, so `await asyncio.sleep(0.01)` returns immediately and a
    # poll loop spins through without real time passing. See docs/tag-bus.md.
    await mock.wait_for("counter.tall", 1, timeout=10)

    assert scene.sorted_tall, "tall box was never diverted"
    assert mock.get("counter.tall") == len(scene.sorted_tall)
