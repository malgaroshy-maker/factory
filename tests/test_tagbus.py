"""Tag bus round-trip: the integration this whole milestone exists to de-risk."""

from __future__ import annotations

import asyncio

import pytest

from factoryforge_sidecar.tags import Tag, TagError, TagTable


# --- tag model ---

def test_kind_and_type_are_validated():
    with pytest.raises(TagError):
        Tag("x", "X", "bit", "sideways")       # type: ignore[arg-type]
    with pytest.raises(TagError):
        Tag("x", "X", "quantum", "input")      # type: ignore[arg-type]


def test_bool_is_not_accepted_as_int():
    """bool subclasses int in Python; silently coercing would hide a real bug."""
    tag = Tag("c", "Counter", "int", "input")
    with pytest.raises(TagError):
        tag.coerce(True)


def test_float_epsilon_suppresses_jitter():
    tag = Tag("l", "Level", "float", "input", 1.0)
    assert not tag.differs(1.0 + 1e-9)
    assert tag.differs(1.5)


def test_forcing_pins_the_visible_value():
    table = TagTable([Tag("s", "Sensor", "bit", "input")])
    table.force("s", True)
    assert table.visible("s") is True
    # A simulator write is absorbed, not observed...
    assert table.set("s", False) is False
    assert table.visible("s") is True
    # ...but is revealed once the force is cleared.
    table.clear_force("s")
    assert table.visible("s") is False


# --- round-trip ---

async def test_engine_describes_scene_on_connect(mock):
    table = await mock.ready()
    assert "conveyor.rotate" in table
    assert table["conveyor.rotate"].kind == "output"
    assert table["sensor_low.detect"].kind == "input"


async def test_write_reaches_the_engine(engine, mock):
    await mock.set("conveyor.rotate", True)
    await asyncio.sleep(0.1)
    assert engine.scene.tags.visible("conveyor.rotate") is True


async def test_sensor_change_reaches_the_sidecar(engine, mock):
    """Engine raises an input; the sidecar must observe it."""
    from scene import SENSOR_LOW_POS, SHORT_HEIGHT, Box

    engine.scene.boxes.append(Box(height=SHORT_HEIGHT, position=SENSOR_LOW_POS))
    await mock.wait_for("sensor_low.detect", True, timeout=2)


async def test_writing_an_input_tag_is_rejected(bus, mock):
    """Direction discipline: only force() may override a simulator-owned input."""
    with pytest.raises(ValueError, match="force"):
        await bus.write("sensor_low.detect", True)


async def test_force_overrides_a_simulator_input(engine, mock):
    await mock.bus.force({"sensor_high.detect": True})
    await mock.wait_for("sensor_high.detect", True, timeout=2)
    assert engine.scene.tags.is_forced("sensor_high.detect")

    await mock.bus.force(clear=["sensor_high.detect"])
    await mock.wait_for("sensor_high.detect", False, timeout=2)


async def test_updates_are_delta_only(engine, mock):
    """An idle scene must produce no traffic at all."""
    await asyncio.sleep(0.05)
    before = len(mock.history)
    await asyncio.sleep(0.15)
    assert len(mock.history) == before


async def test_stale_epoch_writes_are_dropped(engine, bus, mock):
    await mock.set("conveyor.rotate", True)
    await asyncio.sleep(0.05)

    # Simulate a scene reload landing between a write being queued and delivered.
    stale_epoch = bus.epoch
    await engine.send_describe()
    await asyncio.sleep(0.05)

    from factoryforge_sidecar import protocol as proto
    await bus._send(proto.write(stale_epoch, {"conveyor.rotate": False}))
    await asyncio.sleep(0.1)

    assert engine.scene.tags.visible("conveyor.rotate") is True
