"""Modbus TCP driver, exercised by a real pymodbus master.

This is the acceptance test for the milestone: a Modbus master (standing in for
OpenPLC or a PLC) writes a coil, the simulation reacts, and the master reads the
result back from a discrete input -- with no 3D and no Siemens software.
"""

from __future__ import annotations

import asyncio

import pytest
import pytest_asyncio
from pymodbus.client import AsyncModbusTcpClient

from factoryforge_sidecar import drivers
from factoryforge_sidecar.modbus import DataStore, handle_pdu
from factoryforge_sidecar.modbus.server import ILLEGAL_ADDRESS, ILLEGAL_FUNCTION


# --- protocol unit tests (no sockets) ---

def test_read_coils_packs_bits_lsb_first():
    store = DataStore()
    store.coils[0:3] = [True, False, True]
    # FC1, address 0, count 3
    resp = handle_pdu(store, bytes([1]) + (0).to_bytes(2, "big") + (3).to_bytes(2, "big"))
    assert resp == bytes([1, 1, 0b101])


def test_write_single_coil_round_trips():
    store = DataStore()
    resp = handle_pdu(store, bytes([5]) + (7).to_bytes(2, "big") + (0xFF00).to_bytes(2, "big"))
    assert store.coils[7] is True
    assert resp[0] == 5


def test_write_multiple_coils():
    store = DataStore()
    # FC15, address 0, count 3, 1 byte, 0b011
    pdu = bytes([15]) + (0).to_bytes(2, "big") + (3).to_bytes(2, "big") + bytes([1, 0b011])
    handle_pdu(store, pdu)
    assert store.coils[0:3] == [True, True, False]


def test_out_of_range_read_is_an_exception():
    store = DataStore(size=10)
    with pytest.raises(Exception) as exc:
        handle_pdu(store, bytes([1]) + (5).to_bytes(2, "big") + (100).to_bytes(2, "big"))
    assert exc.value.code == ILLEGAL_ADDRESS


def test_unknown_function_code_is_rejected():
    with pytest.raises(Exception) as exc:
        handle_pdu(DataStore(), bytes([99, 0, 0, 0, 1]))
    assert exc.value.code == ILLEGAL_FUNCTION


def test_write_callback_reports_the_master_write():
    store = DataStore()
    seen = []
    store.on_write = lambda kind, addr, values: seen.append((kind, addr, values))
    handle_pdu(store, bytes([5]) + (2).to_bytes(2, "big") + (0xFF00).to_bytes(2, "big"))
    assert seen == [("coils", 2, [True])]


# --- driver integration ---

@pytest_asyncio.fixture
async def modbus(bus):
    """A Modbus TCP driver on an ephemeral port, mapped to the scene."""
    driver = drivers.create("modbus-tcp", bus, host="127.0.0.1", port=0)
    await driver.start()
    for _ in range(100):                      # wait for the describe to map tags
        if driver._by_tag:
            break
        await asyncio.sleep(0.05)
    assert driver._by_tag, "driver never received a describe"
    try:
        yield driver
    finally:
        await driver.stop()


@pytest_asyncio.fixture
async def master(modbus):
    client = AsyncModbusTcpClient("127.0.0.1", port=modbus.port)
    await client.connect()
    assert client.connected
    try:
        yield client
    finally:
        client.close()


def _addr(driver, tag_id: str) -> int:
    return driver._by_tag[tag_id].address


async def test_address_map_is_deterministic(modbus):
    """Sorted by tag id, so a student can write the map down once."""
    coils = sorted(
        (m.address, m.tag_id) for m in modbus._by_tag.values() if m.block == "coils"
    )
    assert [t for _, t in coils] == [
        "conveyor.rotate", "emitter.emit", "pusher.extend", "stack_light.green",
    ]


async def test_master_write_drives_the_simulation(engine, modbus, master):
    """The core round-trip: coil write -> tag bus -> engine."""
    await master.write_coil(_addr(modbus, "conveyor.rotate"), True)
    for _ in range(100):
        if engine.scene.tags.visible("conveyor.rotate"):
            break
        await asyncio.sleep(0.05)
    assert engine.scene.tags.visible("conveyor.rotate") is True


async def test_master_reads_a_sensor(engine, modbus, master):
    """The other direction: engine input -> tag bus -> discrete input."""
    from scene import SENSOR_LOW_POS, SHORT_HEIGHT, Box

    engine.scene.boxes.append(Box(height=SHORT_HEIGHT, position=SENSOR_LOW_POS))
    address = _addr(modbus, "sensor_low.detect")
    for _ in range(100):
        result = await master.read_discrete_inputs(address, count=1)
        if result.bits[0]:
            break
        await asyncio.sleep(0.05)
    assert result.bits[0] is True


async def test_int_tags_map_to_input_registers(engine, modbus, master):
    engine.scene.sorted_tall.extend([object()] * 3)
    address = _addr(modbus, "counter.tall")
    for _ in range(100):
        result = await master.read_input_registers(address, count=1)
        if result.registers[0] == 3:
            break
        await asyncio.sleep(0.05)
    assert result.registers[0] == 3


async def test_initial_state_is_seeded_before_any_update(modbus, master):
    """A master polling before the first delta must not read all-zeros."""
    result = await master.read_discrete_inputs(_addr(modbus, "pusher.retracted"), count=1)
    assert result.bits[0] is True
