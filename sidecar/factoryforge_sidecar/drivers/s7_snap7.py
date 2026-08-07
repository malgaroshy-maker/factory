"""Siemens S7 Protocol driver via snap7.

Communicates with physical S7-300, S7-400, S7-1200, and S7-1500 PLCs directly over
ISO-on-TCP (port 102) without requiring an OPC UA licence.
"""

from __future__ import annotations

import asyncio
import logging
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


@register("s7-snap7")
class S7Snap7Driver(Driver):
    """Siemens S7 protocol driver connecting directly to S7 PLCs via Snap7."""

    def __init__(self, bus: TagBusClient, host: str = "192.168.1.20", rack: int = 0, slot: int = 1, db: int = 1, **config: Any) -> None:
        super().__init__(bus, **config)
        self.host = host
        self.rack = rack
        self.slot = slot
        self.db_number = db
        self._client: Any = None
        self._running = False
        self._task: asyncio.Task | None = None
        self._table: TagTable | None = None

    async def start(self) -> None:
        self._running = True
        if not HAS_SNAP7:
            log.warning("python-snap7 not installed — S7 Snap7 driver running in mock mode")
            return

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

        try:
            data = self._client.db_read(self.db_number, 1, 4)
            byte_val = data[0]

            offset = 0
            for tag_id, value in values.items():
                tag = self._table.get(tag_id)
                if tag and tag.kind.value == "input":
                    if bool(value):
                        byte_val |= (1 << offset)
                    else:
                        byte_val &= ~(1 << offset)
                    offset = (offset + 1) % 8

            data[0] = byte_val
            self._client.db_write(self.db_number, 1, data)
        except Exception as err:
            log.error("Error writing S7 inputs via snap7: %s", err)

    async def _poll_loop(self) -> None:
        while self._running:
            if self._client and HAS_SNAP7 and self._table:
                try:
                    data = self._client.db_read(self.db_number, 0, 4)
                    byte_val = data[0]
                    changes: dict[str, TagValue] = {}
                    offset = 0
                    for tag in self._table.outputs():
                        if tag.type.value == "bit":
                            changes[tag.id] = bool((byte_val >> offset) & 1)
                            offset = (offset + 1) % 8
                    if changes:
                        await self.bus.update(changes)
                except Exception as err:
                    log.error("Error polling S7 outputs via snap7: %s", err)
            await asyncio.sleep(0.05)
