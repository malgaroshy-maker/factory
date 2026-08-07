"""OPC UA **server** driver: the simulator exposes its tags, others connect.

This is the higher-leverage half of OPC UA support. Node-RED, Ignition, and most
SCADA packages speak OPC UA client-side, so exposing the scene as a server
reaches MQTT, HTTP, dashboards, and cloud services through one Node-RED flow --
without this project writing a single integration. It also inverts who has to be
configured, which is what reduces support burden long term.

Node ids are `ns=2;s=<tag_id>` -- stable, readable, and derived from the tag id
so nobody has to maintain a mapping.

    sim `output` tag  (PLC writes it) -> writable node; client writes reach the bus
    sim `input`  tag  (PLC reads it)  -> read-only node, updated from push()

Client writes are detected with a server-side subscription to our own address
space, which is asyncua's supported way of doing this.
"""

from __future__ import annotations

import asyncio
import logging

from asyncua import Server, ua

from ..tags import TagTable, TagValue
from . import Driver, register

log = logging.getLogger(__name__)

NAMESPACE_URI = "urn:factoryforge:scene"
DEFAULT_ENDPOINT = "opc.tcp://127.0.0.1:4841/factoryforge/"

_VARIANT = {
    "bit": ua.VariantType.Boolean,
    "int": ua.VariantType.Int32,
    "float": ua.VariantType.Float,
}


class _WriteHandler:
    """Receives client writes to the tags the controller owns."""

    def __init__(self, driver: "OpcUaServerDriver") -> None:
        self._driver = driver

    def datachange_notification(self, node, val, data) -> None:
        tag_id = self._driver._by_node.get(node.nodeid.to_string())
        if tag_id is not None:
            self._driver._queue_write(tag_id, val)


@register("opcua-server")
class OpcUaServerDriver(Driver):
    def __init__(self, bus, endpoint: str = DEFAULT_ENDPOINT,
                 name: str = "FactoryForge", publish_interval: int = 50,
                 **config) -> None:
        super().__init__(bus, endpoint=endpoint, **config)
        self.endpoint = endpoint
        self.name = name
        self.publish_interval = publish_interval

        self.server: Server | None = None
        self.idx: int | None = None
        self._folder = None
        self._nodes: dict[str, object] = {}     # tag_id -> node
        self._by_node: dict[str, str] = {}      # nodeid string -> tag_id
        self._subscription = None
        self._table: TagTable | None = None
        self._started = False
        #: Set while applying an engine update, so echoing our own write back
        #: onto the bus is suppressed.
        self._applying: set[str] = set()

    @property
    def actual_endpoint(self) -> str:
        return self.endpoint

    # --- lifecycle ---

    async def start(self) -> None:
        self.server = Server()
        await self.server.init()
        self.server.set_endpoint(self.endpoint)
        self.server.set_server_name(self.name)
        # Anonymous, unencrypted by default: this is a teaching tool bound to
        # loopback. Deployments that need certificates can configure them.
        self.server.set_security_policy([ua.SecurityPolicyType.NoSecurity])
        self.idx = await self.server.register_namespace(NAMESPACE_URI)

        await self.server.start()
        self._started = True
        await self._report("info", "server_started",
                           f"OPC UA server listening on {self.endpoint}")
        if self._table is not None:
            await self._publish(self._table)

    async def stop(self) -> None:
        if self.server is not None and self._started:
            try:
                await self.server.stop()
            except Exception:
                log.debug("error stopping OPC UA server", exc_info=True)
        self._started = False
        self.server = None
        self._nodes.clear()
        self._by_node.clear()

    # --- address space ---

    async def rebuild(self, scene: str, epoch: int, table: TagTable) -> None:
        self._table = table
        if self._started:
            await self._publish(table)

    async def _publish(self, table: TagTable) -> None:
        """(Re)build the address space for this tag set."""
        assert self.server is not None and self.idx is not None

        if self._folder is not None:
            try:
                await self.server.delete_nodes([self._folder], recursive=True)
            except Exception:
                log.debug("could not delete previous folder", exc_info=True)
        self._nodes.clear()
        self._by_node.clear()

        self._folder = await self.server.nodes.objects.add_folder(
            ua.NodeId("tags", self.idx), "Tags")

        writable = []
        for tag in table:
            node_id = ua.NodeId(tag.id, self.idx)
            node = await self._folder.add_variable(
                node_id, tag.name, table.visible(tag.id), _VARIANT[tag.type])
            self._nodes[tag.id] = node
            self._by_node[node.nodeid.to_string()] = tag.id
            if tag.kind == "output":
                # The controller owns it, so clients may write it.
                await node.set_writable()
                writable.append(node)

        # Subscribe to our own address space to catch client writes.
        if writable:
            self._subscription = await self.server.create_subscription(
                self.publish_interval, _WriteHandler(self))
            await self._subscription.subscribe_data_change(writable)

        log.info("published %d tags at %s", len(self._nodes), self.endpoint)

    # --- data flow ---

    async def push(self, values: dict[str, TagValue]) -> None:
        """Sensor changes from the engine -> update the read-only nodes."""
        if not self._started or self._table is None:
            return
        for tag_id, value in values.items():
            node = self._nodes.get(tag_id)
            tag = self._table.get(tag_id)
            if node is None or tag is None:
                continue
            self._applying.add(tag_id)
            try:
                await node.write_value(
                    ua.DataValue(ua.Variant(value, _VARIANT[tag.type])))
            except Exception as exc:
                log.warning("could not update %s: %s", tag_id, exc)
            finally:
                self._applying.discard(tag_id)

    def _queue_write(self, tag_id: str, value) -> None:
        if tag_id in self._applying:
            return          # our own update echoing back
        tag = self._table.get(tag_id) if self._table else None
        if tag is None:
            return
        try:
            coerced = tag.coerce(value)
        except Exception:
            log.warning("client wrote %r to %s, which is not a %s",
                        value, tag_id, tag.type)
            return
        asyncio.get_event_loop().create_task(self.bus.write(tag_id, coerced))

    async def _report(self, level: str, code: str, message: str) -> None:
        log.log({"info": logging.INFO, "warn": logging.WARNING}.get(level, logging.ERROR),
                "%s", message)
        try:
            await self.bus.status(level, code, message)
        except Exception:
            log.debug("could not report status upstream", exc_info=True)
