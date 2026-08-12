"""Siemens S7 Protocol driver via snap7.

Communicates with physical S7-300, S7-400, S7-1200, and S7-1500 PLCs directly over
ISO-on-TCP (port 102) without requiring an OPC UA licence.
"""

from __future__ import annotations

import asyncio
import json
import logging
import re
from pathlib import Path
from typing import Any

from ..tagbus import TagBusClient
from ..tags import TagTable, TagValue
from . import Driver, register

log = logging.getLogger(__name__)

try:
    import snap7
    HAS_SNAP7 = True
except ImportError:
    HAS_SNAP7 = False


_BIT = re.compile(r"^DBX(\d+)\.([0-7])$", re.I)
_DWORD = re.compile(r"^DBD(\d+)$", re.I)


def _parse_address(address: str) -> tuple[str, int, int] | None:
    """Parse a Siemens absolute DB address.

    ``DBX4.2`` is bit 2 of byte 4; ``DBD6`` is the doubleword at byte 6, which
    is how a DInt or a Real is addressed. These are the spellings that appear in
    TIA next to a non-optimized DB member, so they can be copied straight across
    rather than counting bytes by hand.
    """
    address = address.strip()
    if (m := _BIT.match(address)):
        return "bit", int(m.group(1)), int(m.group(2))
    if (m := _DWORD.match(address)):
        return "word", int(m.group(1)), 0
    return None


@register("s7-snap7")
class S7Snap7Driver(Driver):
    """Siemens S7 protocol driver connecting directly to S7 PLCs via Snap7."""

    def __init__(self, bus: TagBusClient, host: str = "192.168.1.20", rack: int = 0,
                 slot: int = 1, db: int = 1, mapping: dict[str, str] | None = None,
                 mapping_file: str | None = None, **config: Any) -> None:
        super().__init__(bus, **config)
        self.host = host
        # Driver options arrive from the CLI as strings (`-o db 1` gives "1"),
        # and the type hints above are only hints. Passing a str straight into
        # snap7's ctypes call fails with "required argument is not an integer",
        # which says nothing about where the string came from.
        self.rack = int(rack)
        self.slot = int(slot)
        self.db_number = int(db)

        # tag id -> ("bit"|"word", byte offset, bit index)
        #
        # Required, and it replaces a scheme that guessed: the old code packed
        # every bit into byte 0 in tag order, wrapped at 8 with `% 8` so the
        # ninth silently overwrote the first, and ignored the DInt counters
        # entirely. Tag order has nothing to do with a DB's layout.
        self._addresses: dict[str, tuple[str, int, int]] = {}
        self._warned: set[str] = set()

        raw = dict(mapping or {})
        if mapping_file:
            loaded = json.loads(Path(mapping_file).read_text())
            raw.update({k: v for k, v in loaded.items() if not k.startswith("_") and v})
        for tag_id, address in raw.items():
            parsed = _parse_address(address)
            if parsed is None:
                log.warning("%s: cannot parse S7 address %r, ignoring", tag_id, address)
                continue
            self._addresses[tag_id] = parsed
        self._client: Any = None
        self._running = False
        self._task: asyncio.Task | None = None
        self._table: TagTable | None = None

    async def start(self) -> None:
        self._running = True
        if not HAS_SNAP7:
            # Loudly, not as a warning that scrolls past: a driver that reports
            # itself started and then does nothing sends people to debug their
            # PLC instead of their pip install.
            raise RuntimeError(
                "the s7-snap7 driver needs python-snap7: pip install python-snap7 "
                '(or pip install -e "sidecar[siemens]")'
            )

        try:
            self._client = snap7.client.Client()
            self._client.connect(self.host, self.rack, self.slot)
            log.info("Connected to S7 PLC at %s (rack %d, slot %d)", self.host, self.rack, self.slot)
            self._task = asyncio.create_task(self._poll_loop())
        except Exception as err:
            log.error("Failed to connect to S7 PLC at %s: %s", self.host, err)

    async def stop(self) -> None:
        self._running = False
        if self._task:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
        if self._client and HAS_SNAP7:
            try:
                self._client.disconnect()
            except Exception:
                pass

    async def rebuild(self, scene: str, epoch: int, table: TagTable) -> None:
        self._table = table

    async def push(self, values: dict[str, TagValue]) -> None:
        if not self._client or not HAS_SNAP7 or not self._table:
            return

        # Only the bytes that actually hold simulator-written values, and only
        # those. Writing the whole DB back would also rewrite the bytes the PLC
        # owns with a copy read a moment earlier — quietly reverting an output
        # the program changed in between.
        #
        # This still cannot be made perfectly safe when a single byte holds both
        # directions, as FF_IO's byte 0 does: bits are not individually
        # addressable on the wire, so setting a sim bit means read-modify-write
        # of a byte containing PLC bits. Keeping the window to one byte makes it
        # unlikely rather than impossible; putting the two directions in
        # different bytes of the DB makes it impossible, and is worth doing in
        # any DB you control.
        try:
            wanted: dict[int, list[tuple[str, int, TagValue, str]]] = {}
            for tag_id, value in values.items():
                tag = self._table.get(tag_id)
                addr = self._addresses.get(tag_id)
                if tag is None or addr is None or tag.kind != "input":
                    continue
                kind, byte, bit = addr
                wanted.setdefault(byte, []).append((kind, bit, value, tag.type))

            for byte, items in wanted.items():
                width = 1 if all(k == "bit" for k, _, _, _ in items) else 4
                block = bytearray(self._client.db_read(self.db_number, byte, width))
                for kind, bit, value, tag_type in items:
                    if kind == "bit":
                        snap7.util.set_bool(block, 0, bit, bool(value))
                    elif tag_type == "float":
                        snap7.util.set_real(block, 0, float(value))
                    else:
                        snap7.util.set_dint(block, 0, int(value))
                self._client.db_write(self.db_number, byte, block)
        except Exception as err:
            log.error("writing DB%d: %s", self.db_number, err)

    async def _poll_loop(self) -> None:
        while self._running:
            if self._client and HAS_SNAP7 and self._table:
                try:
                    span = self._span()
                    data = bytearray(self._client.db_read(self.db_number, 0, span))
                    changes: dict[str, TagValue] = {}

                    for tag in self._table.by_kind("output"):
                        addr = self._addresses.get(tag.id)
                        if addr is None:
                            continue
                        kind, byte, bit = addr
                        if kind == "bit":
                            changes[tag.id] = snap7.util.get_bool(data, byte, bit)
                        elif tag.type == "float":
                            changes[tag.id] = snap7.util.get_real(data, byte)
                        else:
                            changes[tag.id] = snap7.util.get_dint(data, byte)

                    if changes:
                        await self.bus.write_many(changes)
                except Exception as err:
                    self._explain(err)
            await asyncio.sleep(0.05)

    def _span(self) -> int:
        """Bytes to read to cover every mapped address."""
        end = 0
        for kind, byte, _bit in self._addresses.values():
            end = max(end, byte + (1 if kind == "bit" else 4))
        # snap7 reads are cheap; round up so a DB that grows a member does not
        # need the driver restarting.
        return max(end, 2)

    def _explain(self, err: Exception) -> None:
        """Turn snap7's terse errors into the thing you actually have to change."""
        text = str(err)
        if "Invalid address" in text and "optimized" not in self._warned:
            self._warned.add("optimized")
            log.error(
                "DB%d exists but has no absolute addresses — it is an "
                "'optimized block access' block, which snap7 cannot read. In TIA, "
                "right-click the DB -> Properties -> Attributes, uncheck "
                "'Optimized block access', recompile and download. (%s)",
                self.db_number, text.strip(),
            )
            return
        if "does not exist" in text and "missing" not in self._warned:
            self._warned.add("missing")
            log.error("DB%d does not exist on this CPU — check -o db <number>. (%s)",
                      self.db_number, text.strip())
            return
        if "generic" not in self._warned:
            self._warned.add("generic")
            log.error("polling DB%d: %s", self.db_number, text.strip())
