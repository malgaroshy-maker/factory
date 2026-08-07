# Tag Bus Protocol v0

The tag bus is the seam between the **simulation engine** (3D, physics, parts) and the
**driver sidecar** (PLC communication). It exists so that a contributor can add a driver in
Python without touching the engine, and add a part in the engine without touching Python.

Everything in this document is versioned. Breaking changes bump `protocol` in the `hello`
message.

## Roles

| Role | Who | Responsibility |
|---|---|---|
| **Engine** | Godot 4.6 / C# (Python stub for tests) | Owns the authoritative tag table. Acts as the **server**. |
| **Sidecar** | Python | Owns PLC protocol connections. Acts as the **client**. |

The engine is authoritative. If the two ever disagree about a tag's value, the engine wins.
The sidecar holds a cache purely so its drivers can serve reads without a round-trip.

Only one sidecar may be connected at a time. A second connection is rejected with
`status.error`. This is deliberate — two drivers writing the same output tag is a bug, not a
feature.

## Transport

JSON text frames over a WebSocket on `ws://127.0.0.1:7411/tagbus`.

Port 7411 sits next to Factory I/O's 7410 so the two can run side by side during the mod-spike
phase without a clash.

JSON was chosen over MessagePack for v0 because it is debuggable from a browser console and
tag counts are small (a full sorting scene is under 200 tags at a 10 ms tick — roughly 20 kB/s
worst case, and far less in practice because updates are delta-only). If profiling ever shows
this matters, swap the codec; the message shapes stay identical.

Bind to loopback only. There is no authentication, and there must never be a reason to expose
this to a network.

## Tag model

```json
{
  "id": "conveyor_1.rotate",
  "name": "Belt Conveyor 1 (Rotate)",
  "type": "bit",
  "kind": "output",
  "value": false
}
```

| Field | Notes |
|---|---|
| `id` | Stable, machine-readable, unique within a scene. Format `<part_id>.<tag_name>`. Survives renaming. |
| `name` | Human-readable, shown in UI. May change freely; never key off this. |
| `type` | `bit` \| `int` \| `float` |
| `kind` | `input` \| `output` |
| `value` | `bool` for `bit`, `int` for `int`, `float` for `float` |

### `kind` is from the controller's point of view

This trips people up constantly, so it is stated once, loudly, and never varies:

- **`output`** — the PLC writes it, the simulator reads it. A motor, a valve, a lamp.
- **`input`** — the simulator writes it, the PLC reads it. A sensor, a button, a counter.

This matches Factory I/O's convention. Students already have this model in their heads and
inverting it would be gratuitous.

Consequence for message direction:

- Sidecar sends `write` for `output` tags only.
- Engine sends `update` for `input` tags only.

A `write` naming an `input` tag, or vice versa, is a protocol error and is rejected. The one
exception is **forcing** (see below).

## Messages

Every message is a JSON object with a `t` field naming its kind.

### `hello` — engine → sidecar, on connect

```json
{ "t": "hello", "protocol": 0, "engine": "factoryforge-engine/0.1.0", "tick_ms": 10 }
```

Sent immediately on connection, before any `describe`. The sidecar must check `protocol` and
disconnect on mismatch rather than guessing.

### `describe` — engine → sidecar

```json
{
  "t": "describe",
  "scene": "sorting-by-height",
  "epoch": 3,
  "tags": [ { "id": "...", "name": "...", "type": "bit", "kind": "output", "value": false } ]
}
```

The complete tag list. Sent after `hello`, and again on every scene load or edit that changes
the tag set.

`epoch` increments on each `describe`. It is the mechanism that makes scene reloads safe:

- The sidecar must stamp every `write` with the `epoch` it was based on.
- The engine drops any `write` carrying a stale `epoch`.

Without this, a `write` in flight during a scene change lands on whatever tag inherited that
id in the new scene. Drivers should rebuild their address maps whenever `epoch` changes.

### `write` — sidecar → engine

```json
{ "t": "write", "epoch": 3, "values": { "conveyor_1.rotate": true, "pusher_1.extend": false } }
```

Batched. Send at most one per tick; coalesce multiple driver writes within a tick into one
message. Unknown ids are ignored with a `status.warn`, not an error — a driver's address map
briefly lagging a scene edit is normal.

### `update` — engine → sidecar

```json
{ "t": "update", "tick": 14203, "values": { "sensor_low.detect": true } }
```

**Delta-only.** Contains solely the `input` tags whose values changed since the last `update`.
A tick with no changes sends nothing at all — an idle scene should produce zero traffic.

`tick` is a monotonically increasing counter, useful for diagnosing latency and dropped frames.

Float comparison uses an epsilon (default `1e-6`) so that physics jitter in the last bits does
not generate a message every single tick.

### `force` — sidecar → engine

```json
{ "t": "force", "epoch": 3, "values": { "sensor_low.detect": true }, "clear": ["sensor_high.detect"] }
```

Overrides a tag's value regardless of `kind`, including simulator-owned `input` tags. This is
the deliberate exception to the direction rule, and it is what makes fault injection and
automated testing possible — you can assert a PLC program's response to a sensor that is
stuck on without physically arranging boxes.

Forced tags keep their forced value until cleared. The engine echoes forced state in
`describe`. Do not use `force` as a shortcut for `write`.

### `status` — either direction

```json
{ "t": "status", "level": "info", "code": "driver_connected", "message": "Modbus TCP server listening on 0.0.0.0:502" }
```

`level` is `info` | `warn` | `error`. Surfaced in the engine's UI so a student can see why
their PLC will not connect without reading a log file.

## Timing

The engine ticks at `tick_ms` (default 10 ms) and sends at most one `update` per tick. The
sidecar sends at most one `write` per tick.

This bus is **not** hard real-time and does not pretend to be. A tag written by the PLC lands
in the simulation within one to two ticks. That is well inside the tolerance of every teaching
scenario, and matches what Factory I/O's own drivers achieve over TCP.

Drivers run on their own asyncio tasks and must never block the bus. A driver that stalls gets
its writes dropped, not the whole simulation.

## Reference

- Engine-side server: `harness/engine_stub.py` (Python reference implementation)
- Sidecar client: `sidecar/factoryforge_sidecar/tagbus.py`
- Tag model: `sidecar/factoryforge_sidecar/tags.py`
