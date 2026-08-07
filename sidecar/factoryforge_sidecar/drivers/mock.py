"""Mock driver: a programmable stand-in for a PLC.

Used by CI and by anyone developing a part who does not want to start TIA Portal
to see whether their conveyor turns. Exposes the tag bus directly as an async
API and records everything the simulator reported.
"""

from __future__ import annotations

import asyncio
import logging

from ..tags import TagTable, TagValue
from . import Driver, register

log = logging.getLogger(__name__)


@register("mock")
class MockDriver(Driver):
    def __init__(self, bus, **config) -> None:
        super().__init__(bus, **config)
        self.running = False
        #: Every value the engine has reported, in order. Test assertions read this.
        self.history: list[tuple[str, TagValue]] = []
        self.tags: TagTable | None = None
        self._ready = asyncio.Event()
        self._waiters: list[tuple[str, TagValue, asyncio.Future]] = []

    async def start(self) -> None:
        self.running = True

    async def stop(self) -> None:
        self.running = False
        for _, _, fut in self._waiters:
            if not fut.done():
                fut.cancel()
        self._waiters.clear()

    async def rebuild(self, scene: str, epoch: int, table: TagTable) -> None:
        self.tags = table
        self._ready.set()

    async def push(self, values: dict[str, TagValue]) -> None:
        for tag_id, value in values.items():
            self.history.append((tag_id, value))
        for waiter in list(self._waiters):
            tag_id, expected, fut = waiter
            if tag_id in values and values[tag_id] == expected and not fut.done():
                fut.set_result(value := values[tag_id])
                self._waiters.remove(waiter)

    # --- test-facing API ---

    async def ready(self, timeout: float = 5.0) -> TagTable:
        """Block until the engine has sent a describe."""
        await asyncio.wait_for(self._ready.wait(), timeout)
        assert self.tags is not None
        return self.tags

    async def set(self, tag_id: str, value: TagValue) -> None:
        """Write a PLC output, as a real PLC would."""
        await self.bus.write(tag_id, value)

    def get(self, tag_id: str) -> TagValue:
        """Read the sidecar's cached value."""
        return self.bus.read(tag_id)

    async def wait_for(self, tag_id: str, value: TagValue, timeout: float = 5.0) -> TagValue:
        """Block until *tag_id* reaches *value*.

        Returns immediately if it is already there, which avoids a race where
        the change lands between the caller's check and this call.
        """
        if self.bus.read(tag_id) == value:
            return value
        fut: asyncio.Future = asyncio.get_running_loop().create_future()
        self._waiters.append((tag_id, value, fut))
        try:
            return await asyncio.wait_for(fut, timeout)
        except asyncio.TimeoutError:
            raise AssertionError(
                f"{tag_id} did not reach {value!r} within {timeout}s "
                f"(last seen {self.bus.read(tag_id)!r})"
            ) from None
