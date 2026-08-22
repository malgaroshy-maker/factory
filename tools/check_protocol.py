"""Connect to a running engine and check its wire messages against docs/tag-bus.md.

    python tools/check_protocol.py [--timeout SECONDS]

Prints "RESULT OK" and exits 0 if `hello`, `describe`, and `update` each carry
exactly the fields the spec names -- no more, no less. Otherwise prints
"RESULT <problem>; <problem>; ..." and exits 1.

This is what would have caught FF-10 before it shipped: `hello` silently
missing `tick_ms` while the sidecar quietly filled in a default and nobody
noticed the two had drifted. Connects raw (not through TagBusClient), since the
client's own parsing would tolerate exactly the kind of drift this exists to
catch.
"""
from __future__ import annotations

import argparse
import asyncio
import json
import sys

import websockets

HELLO_FIELDS = {"t", "protocol", "engine", "tick_ms"}
DESCRIBE_FIELDS = {"t", "scene", "epoch", "tags"}
TAG_REQUIRED_FIELDS = {"id", "name", "type", "kind", "value"}
TAG_OPTIONAL_FIELDS = {"forced"}
UPDATE_FIELDS = {"t", "tick", "values"}


async def check(url: str, timeout: float) -> list[str]:
    problems: list[str] = []
    async with websockets.connect(url, max_queue=64) as ws:
        hello = json.loads(await asyncio.wait_for(ws.recv(), timeout))
        if hello.get("t") != "hello":
            problems.append(f"first message was {hello.get('t')!r}, not 'hello'")
        elif set(hello) != HELLO_FIELDS:
            problems.append(f"hello fields {sorted(hello)} != {sorted(HELLO_FIELDS)}")

        describe = json.loads(await asyncio.wait_for(ws.recv(), timeout))
        if describe.get("t") != "describe":
            problems.append(f"second message was {describe.get('t')!r}, not 'describe'")
        elif set(describe) != DESCRIBE_FIELDS:
            problems.append(f"describe fields {sorted(describe)} != {sorted(DESCRIBE_FIELDS)}")
        else:
            for tag in describe["tags"]:
                extra = set(tag) - TAG_REQUIRED_FIELDS - TAG_OPTIONAL_FIELDS
                missing = TAG_REQUIRED_FIELDS - set(tag)
                if extra or missing:
                    problems.append(f"tag {tag.get('id')} fields off: extra={extra} missing={missing}")
                    break

        target = next((t for t in describe.get("tags", []) if t["kind"] == "input"
                       and t["type"] == "bit"), None)
        if target is None:
            problems.append("no bit-typed input tag to force -- cannot check `update` shape")
        else:
            await ws.send(json.dumps({
                "t": "force", "epoch": describe["epoch"],
                "values": {target["id"]: not target["value"]},
            }))
            update = None
            deadline = asyncio.get_event_loop().time() + timeout
            while asyncio.get_event_loop().time() < deadline:
                remaining = deadline - asyncio.get_event_loop().time()
                try:
                    msg = json.loads(await asyncio.wait_for(ws.recv(), max(remaining, 0.01)))
                except asyncio.TimeoutError:
                    break
                if msg.get("t") == "update":
                    update = msg
                    break
            if update is None:
                problems.append("forcing an input tag produced no `update` message")
            elif set(update) != UPDATE_FIELDS:
                problems.append(f"update fields {sorted(update)} != {sorted(UPDATE_FIELDS)}")

    return problems


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="ws://127.0.0.1:7411/tagbus")
    parser.add_argument("--timeout", type=float, default=10.0)
    args = parser.parse_args()

    try:
        problems = asyncio.run(check(args.url, args.timeout))
    except (OSError, asyncio.TimeoutError) as exc:
        print(f"RESULT could not reach {args.url}: {exc}")
        return 1

    if problems:
        print("RESULT " + "; ".join(problems))
        return 1
    print("RESULT OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
