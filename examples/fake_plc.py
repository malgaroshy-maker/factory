"""A fake S7-1500, for testing the OPC UA client path without TIA Portal.

Exposes the same `FF_IO` address space a real S7-1500 would, and runs a faithful
Python port of `tia/Sorting.scl`. Use it to prove the driver, the mapping file,
and the control timings all work before spending time in TIA Portal -- and as a
way for contributors with no Siemens licence to work on the OPC UA driver.

    Terminal 1:  python examples/fake_plc.py
    Terminal 2:  python -m factoryforge_sidecar demo \
                     --driver opcua-client --mapping examples/opcua_mapping.json

If you change the timings here, change `Sorting.scl` to match. The SCL file is
the source of truth; this is the mirror.
"""

from __future__ import annotations

import asyncio
import logging
import time

from asyncua import Server, ua

ENDPOINT = "opc.tcp://127.0.0.1:4840/"
SCAN = 0.01                     # 10 ms, a plausible S7 cycle

# Must match Sorting.scl
#: One box per *rising* edge, so the box interval is twice this.
EMIT_HALF_PERIOD = 1.5
PUSH_DELAY = 0.9
PUSH_HOLD = 0.5

BOOLS = ["ConveyorRotate", "EmitterEmit", "PusherExtend", "StackLightGreen",
         "SensorLow", "SensorHigh", "PusherExtended", "PusherRetracted"]
DINTS = ["CounterTall", "CounterShort"]

log = logging.getLogger("fake_plc")


class TON:
    """IEC on-delay timer, scan-evaluated like the real thing."""

    def __init__(self, pt: float) -> None:
        self.pt = pt
        self._start: float | None = None
        self.q = False

    def __call__(self, enable: bool, now: float) -> bool:
        if enable:
            if self._start is None:
                self._start = now
            self.q = (now - self._start) >= self.pt
        else:
            self._start = None
            self.q = False
        return self.q


class Sorting:
    """Python port of Sorting.scl v0.2."""

    def __init__(self) -> None:
        self.t_emit = TON(EMIT_HALF_PERIOD)
        self.emit_flag = False
        self.t_delay = TON(PUSH_DELAY)
        self.t_push = TON(PUSH_HOLD)
        self.high_mem = False
        self.push_req = False

    def scan(self, io: dict, now: float) -> None:
        io["ConveyorRotate"] = True
        io["StackLightGreen"] = True

        # Square wave: held high for a full half-period, so a sampled transport
        # cannot miss the rising edge. One box per rising edge.
        #
        # IN held TRUE so Q latches and stays latched until reset explicitly.
        # The obvious `IN := NOT Q` form makes Q high for a single scan and did
        # not work on a real S7-1500. See Sorting.scl v0.3.
        self.t_emit(True, now)
        if self.t_emit.q:
            self.emit_flag = not self.emit_flag
            self.t_emit(False, now)          # reset; restarts next scan
        io["EmitterEmit"] = self.emit_flag

        # Latch on the rising edge -- the box clears the sensor long before it
        # reaches the pusher, so the request must outlive the signal.
        if io["SensorHigh"] and not self.high_mem:
            self.push_req = True
        self.high_mem = io["SensorHigh"]

        self.t_delay(self.push_req, now)
        self.t_push(self.t_delay.q, now)

        io["PusherExtend"] = self.t_delay.q and not self.t_push.q

        if self.t_push.q:
            self.push_req = False


async def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    logging.getLogger("asyncua").setLevel(logging.ERROR)

    server = Server()
    await server.init()
    server.set_endpoint(ENDPOINT)
    server.set_server_name("Fake S7-1500")
    server.set_security_policy([ua.SecurityPolicyType.NoSecurity])

    # A real S7-1500 puts its own tags at ns=3. asyncua would hand out ns=2 for
    # the first namespace we register, so burn one to line the indices up and
    # keep the shipped mapping file valid against both.
    await server.register_namespace("urn:fakeplc:padding")
    idx = await server.register_namespace("urn:fakeplc:plc")

    db = await server.nodes.objects.add_folder(ua.NodeId("FF_IO", idx), "FF_IO")
    nodes: dict[str, object] = {}
    for name in BOOLS:
        node = await db.add_variable(
            ua.NodeId(f'"FF_IO"."{name}"', idx), name, False, ua.VariantType.Boolean)
        await node.set_writable()
        nodes[name] = node
    for name in DINTS:
        node = await db.add_variable(
            ua.NodeId(f'"FF_IO"."{name}"', idx), name, 0, ua.VariantType.Int32)
        await node.set_writable()
        nodes[name] = node

    await server.start()
    print(f"fake S7-1500 listening on {ENDPOINT}")
    print(f"  namespace index {idx}, e.g.  ns={idx};s=\"FF_IO\".\"ConveyorRotate\"")
    print("  Ctrl-C to stop\n", flush=True)

    program = Sorting()
    io = {name: False for name in BOOLS} | {name: 0 for name in DINTS}
    last_report = 0.0

    try:
        while True:
            await asyncio.sleep(SCAN)
            now = time.perf_counter()

            # Read the inputs the simulator writes.
            for name in ("SensorLow", "SensorHigh", "PusherExtended", "PusherRetracted"):
                io[name] = bool(await nodes[name].read_value())
            for name in DINTS:
                io[name] = int(await nodes[name].read_value())

            program.scan(io, now)

            # Write the outputs the simulator reads.
            for name in ("ConveyorRotate", "EmitterEmit", "PusherExtend",
                         "StackLightGreen"):
                await nodes[name].write_value(
                    ua.DataValue(ua.Variant(io[name], ua.VariantType.Boolean)))

            if now - last_report > 1.0:
                last_report = now
                print(f"tall={io['CounterTall']:<4} short={io['CounterShort']:<4} "
                      f"low={int(io['SensorLow'])} high={int(io['SensorHigh'])} "
                      f"push={int(io['PusherExtend'])}", flush=True)
    except (KeyboardInterrupt, asyncio.CancelledError):
        pass
    finally:
        await server.stop()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
