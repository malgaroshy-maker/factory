"""Siemens PLCSIM Advanced Simulation Runtime API driver.

Connects directly to S7-PLCSIM Advanced virtual CPUs via Siemens' native C#/.NET
Simulation Runtime API DLL (Siemens.Simatic.Simulation.Runtime.Api.x64).
Direct shared memory I/O access — lowest latency, zero network overhead, no OPC UA licence needed.
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
    import clr  # pythonnet
    HAS_PYTHONNET = True
except ImportError:
    HAS_PYTHONNET = False


@register("plcsim-advanced")
class PLCSIMAdvancedDriver(Driver):
    """Siemens PLCSIM Advanced native API driver."""

    def __init__(self, bus: TagBusClient, instance: str = "Sorting_PLC", **config: Any) -> None:
        super().__init__(bus, **config)
        self.instance_name = instance
        self._instance: Any = None
        self._running = False
        self._task: asyncio.Task | None = None
        self._table: TagTable | None = None

    async def start(self) -> None:
        self._running = True
        if not HAS_PYTHONNET:
            log.warning("pythonnet not installed — PLCSIM Advanced driver running in mock mode")
            return

        try:
            clr.AddReference("Siemens.Simatic.Simulation.Runtime.Api.x64")
            from Siemens.Simatic.Simulation.Runtime import SimulationRuntimeManager  # type: ignore

            self._instance = SimulationRuntimeManager.CreateInterface(self.instance_name)
            self._instance.PowerOn()
            self._instance.Run()
            log.info("Connected to PLCSIM Advanced instance '%s'", self.instance_name)
            self._task = asyncio.create_task(self._poll_loop())
        except Exception as err:
            log.warning("Could not connect to PLCSIM Advanced instance '%s' (fallback mode): %s", self.instance_name, err)

    async def stop(self) -> None:
        self._running = False
        if self._task:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
        if self._instance:
            try:
                self._instance.PowerOff()
            except Exception:
                pass

    async def rebuild(self, scene: str, epoch: int, table: TagTable) -> None:
        self._table = table

    async def push(self, values: dict[str, TagValue]) -> None:
        if not self._instance or not self._table:
            return

        for tag_id, val in values.items():
            try:
                tag = self._table.get(tag_id)
                if tag and tag.kind.value == "input":
                    if tag.type.value == "bit":
                        self._instance.WriteBool(tag_id, bool(val))
                    elif tag.type.value == "int":
                        self._instance.WriteInt32(tag_id, int(val))
            except Exception as err:
                log.error("Error writing PLCSIM Advanced input '%s': %s", tag_id, err)

    async def _poll_loop(self) -> None:
        while self._running:
            if self._instance and self._table:
                changes: dict[str, TagValue] = {}
                for tag in self._table.outputs():
                    try:
                        if tag.type.value == "bit":
                            changes[tag.id] = bool(self._instance.ReadBool(tag.id))
                        elif tag.type.value == "int":
                            changes[tag.id] = int(self._instance.ReadInt32(tag.id))
                    except Exception:
                        pass
                if changes:
                    await self.bus.update(changes)
            await asyncio.sleep(0.05)
