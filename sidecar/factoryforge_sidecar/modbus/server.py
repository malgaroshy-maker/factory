"""A minimal asyncio Modbus TCP server.

Why not pymodbus's server? As of pymodbus 3.14 the `ModbusDeviceContext` /
`ModbusSequentialDataBlock` datastore is deprecated and slated for removal in
v4, and its replacement (`SimData`/`SimDevice`) stores coils as packed 16-bit
registers, which makes per-bit read/write mapping awkward and couples us to
internals that are visibly still in flux. Implementing the eight function codes
a simulator needs is less code than that adapter layer, has no moving
dependency, and gives exact control over the tag/address mapping.

pymodbus is still used as the *test master* -- that half of its API is stable.

Supports FC 1, 2, 3, 4, 5, 6, 15, 16.
"""

from __future__ import annotations

import asyncio
import logging
import struct
from typing import Callable

log = logging.getLogger(__name__)

# Exception codes
ILLEGAL_FUNCTION = 0x01
ILLEGAL_ADDRESS = 0x02
ILLEGAL_VALUE = 0x03
SERVER_FAILURE = 0x04

MAX_BITS = 2000
MAX_REGS = 125


class ModbusError(Exception):
    def __init__(self, code: int) -> None:
        super().__init__(f"modbus exception {code}")
        self.code = code


class DataStore:
    """Coils, discrete inputs, holding and input registers.

    Naming is from the *master's* point of view, per the Modbus spec:
      coils            -- read/write bits   (master writes these)
      discrete_inputs  -- read-only bits    (master reads these)
      holding_registers-- read/write words  (master writes these)
      input_registers  -- read-only words   (master reads these)
    """

    def __init__(self, size: int = 1024) -> None:
        self.size = size
        self.coils = [False] * size
        self.discrete_inputs = [False] * size
        self.holding_registers = [0] * size
        self.input_registers = [0] * size
        #: Called with (kind, address, values) after a master write.
        #: kind is "coils" or "holding_registers".
        self.on_write: Callable[[str, int, list], None] | None = None

    def _check(self, block: list, address: int, count: int, limit: int) -> None:
        if count < 1 or count > limit:
            raise ModbusError(ILLEGAL_VALUE)
        if address < 0 or address + count > len(block):
            raise ModbusError(ILLEGAL_ADDRESS)

    def read_bits(self, block: list[bool], address: int, count: int) -> list[bool]:
        self._check(block, address, count, MAX_BITS)
        return block[address:address + count]

    def read_regs(self, block: list[int], address: int, count: int) -> list[int]:
        self._check(block, address, count, MAX_REGS)
        return block[address:address + count]

    def write_coils(self, address: int, values: list[bool]) -> None:
        self._check(self.coils, address, len(values), MAX_BITS)
        self.coils[address:address + len(values)] = values
        if self.on_write:
            self.on_write("coils", address, values)

    def write_registers(self, address: int, values: list[int]) -> None:
        self._check(self.holding_registers, address, len(values), MAX_REGS)
        self.holding_registers[address:address + len(values)] = values
        if self.on_write:
            self.on_write("holding_registers", address, values)


def _pack_bits(bits: list[bool]) -> bytes:
    """Pack bits LSB-first into bytes, as the Modbus spec requires."""
    out = bytearray((len(bits) + 7) // 8)
    for i, bit in enumerate(bits):
        if bit:
            out[i // 8] |= 1 << (i % 8)
    return bytes(out)


def _unpack_bits(data: bytes, count: int) -> list[bool]:
    return [bool(data[i // 8] & (1 << (i % 8))) for i in range(count)]


def handle_pdu(store: DataStore, pdu: bytes) -> bytes:
    """Process one request PDU and return the response PDU."""
    if not pdu:
        raise ModbusError(ILLEGAL_FUNCTION)
    fc = pdu[0]
    body = pdu[1:]

    try:
        if fc in (1, 2, 3, 4):
            address, count = struct.unpack(">HH", body[:4])
            if fc in (1, 2):
                block = store.coils if fc == 1 else store.discrete_inputs
                data = _pack_bits(store.read_bits(block, address, count))
                return bytes([fc, len(data)]) + data
            block = store.holding_registers if fc == 3 else store.input_registers
            regs = store.read_regs(block, address, count)
            return bytes([fc, len(regs) * 2]) + struct.pack(f">{len(regs)}H", *regs)

        if fc == 5:
            address, raw = struct.unpack(">HH", body[:4])
            if raw not in (0x0000, 0xFF00):
                raise ModbusError(ILLEGAL_VALUE)
            store.write_coils(address, [raw == 0xFF00])
            return bytes([fc]) + struct.pack(">HH", address, raw)

        if fc == 6:
            address, value = struct.unpack(">HH", body[:4])
            store.write_registers(address, [value])
            return bytes([fc]) + struct.pack(">HH", address, value)

        if fc == 15:
            address, count, nbytes = struct.unpack(">HHB", body[:5])
            data = body[5:5 + nbytes]
            if len(data) != nbytes or nbytes != (count + 7) // 8:
                raise ModbusError(ILLEGAL_VALUE)
            store.write_coils(address, _unpack_bits(data, count))
            return bytes([fc]) + struct.pack(">HH", address, count)

        if fc == 16:
            address, count, nbytes = struct.unpack(">HHB", body[:5])
            if nbytes != count * 2 or len(body) < 5 + nbytes:
                raise ModbusError(ILLEGAL_VALUE)
            values = list(struct.unpack(f">{count}H", body[5:5 + nbytes]))
            store.write_registers(address, values)
            return bytes([fc]) + struct.pack(">HH", address, count)

    except struct.error as exc:
        raise ModbusError(ILLEGAL_VALUE) from exc

    raise ModbusError(ILLEGAL_FUNCTION)


class ModbusTcpServer:
    def __init__(self, store: DataStore, host: str = "127.0.0.1", port: int = 502) -> None:
        self.store = store
        self.host = host
        self.port = port
        self._server: asyncio.AbstractServer | None = None

    @property
    def actual_port(self) -> int:
        """The bound port. Differs from `port` when 0 was requested."""
        if not self._server:
            raise RuntimeError("server is not running")
        return self._server.sockets[0].getsockname()[1]

    async def start(self) -> None:
        self._server = await asyncio.start_server(self._client, self.host, self.port)
        log.info("Modbus TCP listening on %s:%d", self.host, self.actual_port)

    async def stop(self) -> None:
        if self._server:
            self._server.close()
            await self._server.wait_closed()
            self._server = None

    async def _client(self, reader: asyncio.StreamReader,
                      writer: asyncio.StreamWriter) -> None:
        peer = writer.get_extra_info("peername")
        log.info("master connected from %s", peer)
        try:
            while True:
                header = await reader.readexactly(7)
                txn, proto_id, length, unit = struct.unpack(">HHHB", header)
                if proto_id != 0:
                    log.warning("bad protocol id %d from %s", proto_id, peer)
                    return
                pdu = await reader.readexactly(length - 1)

                try:
                    response = handle_pdu(self.store, pdu)
                except ModbusError as exc:
                    response = bytes([pdu[0] | 0x80, exc.code])
                except Exception:
                    log.exception("handler failed")
                    response = bytes([pdu[0] | 0x80, SERVER_FAILURE])

                writer.write(
                    struct.pack(">HHHB", txn, 0, len(response) + 1, unit) + response
                )
                await writer.drain()
        except (asyncio.IncompleteReadError, ConnectionResetError):
            pass
        finally:
            log.info("master disconnected from %s", peer)
            writer.close()
