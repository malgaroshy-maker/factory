"""Trace what the driver actually writes to, and receives from, the real PLC."""
import asyncio, json, logging, sys, time
from pathlib import Path

ROOT = Path(r"C:\Users\masal\source\factoryforge")
sys.path[:0] = [str(ROOT / "sidecar"), str(ROOT / "harness")]
logging.disable(logging.WARNING)

from engine_stub import EngineStub
from scene import SortingScene
from factoryforge_sidecar import drivers
from factoryforge_sidecar.tagbus import TagBusClient

T0 = time.perf_counter()


def ts():
    return f"{time.perf_counter() - T0:6.2f}"


async def main():
    sc = SortingScene()
    stub = EngineStub(sc, port=7437, tick_ms=10)
    await stub.start()
    ticker = asyncio.create_task(stub._tick_loop())
    bus = TagBusClient(stub.url)
    runner = asyncio.create_task(bus.run())
    await asyncio.wait_for(bus.connected.wait(), 5)

    mapping = {k: v for k, v in
               json.loads((ROOT / "examples/opcua_mapping.json").read_text()).items()
               if not k.startswith("_")}
    d = drivers.create("opcua-client", bus, url="opc.tcp://192.168.1.20:4840",
                       mapping=mapping)
    await d.start()
    for _ in range(80):
        if d._nodes:
            break
        await asyncio.sleep(0.25)
    print(f"{ts()} bound {len(d._nodes)} tags; subscription={d._subscription is not None}")

    orig_write = d._write_node
    async def traced_write(tag_id, value):
        t = time.perf_counter()
        await orig_write(tag_id, value)
        if "sensor" in tag_id:
            print(f"{ts()} WRITE-> {tag_id}={value} ({(time.perf_counter()-t)*1000:.0f}ms)")
    d._write_node = traced_write

    orig_queue = d._queue_write
    def traced_queue(tag_id, value):
        print(f"{ts()} RECV<- {tag_id}={value}")
        orig_queue(tag_id, value)
    d._queue_write = traced_queue

    try:
        deadline = time.perf_counter() + 45
        while time.perf_counter() < deadline:
            await asyncio.sleep(1.0)
            print(f"{ts()} sim: boxes={[('T' if b.is_tall else 's', round(b.position,2)) for b in sc.boxes]} "
                  f"hi={int(sc.tags.visible('sensor_high.detect'))} "
                  f"ext={int(sc.tags.visible('pusher.extend'))} "
                  f"tall={len(sc.sorted_tall)} thru={len(sc.sorted_short)}")
    finally:
        print(f"\n{ts()} RESULT tall={len(sc.sorted_tall)} thru={len(sc.sorted_short)}")
        await d.stop()              # clean disconnect -- do not leak the session
        ticker.cancel()
        runner.cancel()
        await asyncio.sleep(0.2)
        await stub.stop()


asyncio.run(main())
