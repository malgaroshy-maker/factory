# AGENTS.md — handoff for the next agent

Read this first, then [`docs/PLAN.md`](docs/PLAN.md) and
[`docs/ROADMAP.md`](docs/ROADMAP.md).

**FactoryForge** is a free, open 3D factory simulator for learning PLC
programming — a replacement for Factory I/O, which is stagnant, closed to custom
parts, and €278/year. See [`docs/PRD.md`](docs/PRD.md) for the full rationale.

---

## Absolute paths on this machine

| What | Path |
|---|---|
| **Repo** | `C:\Users\masal\source\factoryforge` |
| **Godot 4.7.1 mono** (console build — use this, it prints to stdout) | `D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe` |
| Python 3.12 | `D:\Python312` (on PATH as `python`) |
| .NET SDK | `C:\Program Files\dotnet` (v10; the project targets net8.0) |
| Node-RED user dir | `C:\Users\masal\.node-red` — **contains the user's own flows, never overwrite** |
| Factory I/O install (reference only) | `F:\Program Files (x86)\Real Games\Factory IO` |

## Live hardware

| What | Detail |
|---|---|
| **S7-PLCSIM Advanced** | `192.168.1.20`, OPC UA at `opc.tcp://192.168.1.20:4840` |
| CPU | 1511-1 PN, FW V2.9, TIA Portal V19 |
| PLC program | Global DB `FF_IO` + FB `Sorting` (instance `Sorting_DB`), currently **SCL v0.3** |
| Node ids | `ns=3;s="FF_IO"."ConveyorRotate"` etc. — quotes are part of the identifier |

The user has **no physical PLC**. PLCSIM Advanced is the reference target and
simulates only the S7-1500 family. Plain bundled PLCSIM has no external network
interface and cannot be used.

---

## Architecture

```
┌────────────────────────────┐          ┌──────────────────────────┐
│  SIM ENGINE                │          │  DRIVER SIDECAR (Python) │
│  Godot 4.7 / C#  (engine/) │  tag bus │  asyncua      (OPC UA)   │
│  or Python stub (harness/) │ ◄──────► │  built-in     (Modbus)   │
│                            │  WS/JSON │  mock         (tests)    │
└────────────────────────────┘          └──────────────────────────┘
```

The **tag bus** ([`docs/tag-bus.md`](docs/tag-bus.md)) is the seam and the most
important contract in the project. Two engine implementations already speak it
(`harness/engine_stub.py` and `engine/src/TagBus/TagBusServer.cs`) and the
sidecar cannot tell them apart. Keep it that way.

`kind` is always from the **controller's** point of view: `output` = PLC writes
it, `input` = simulator writes it. This trips everyone up; it is enforced.

### Layout

```
engine/          Godot 4.7 C# project
  src/TagBus/    Tag, TagTable, TagBusServer  (C# port, must match Python)
  src/Scenes/    SortingScene.cs              (port of harness/scene.py)
  src/View/      SceneView.cs, OrbitCamera.cs (reads state only, never writes)
harness/         Python engine reference + headless scenes (CI scene runner)
sidecar/         Python package: bus client, drivers, minimal Modbus server
examples/tia/    Sorting.scl, FF_IO DB spec, setup walkthrough
examples/nodered/ flow that replaces the PLC entirely
tools/           drive_engine.py (parity check), drv_trace.py (driver tracing)
tests/           41 tests, no Siemens software or GPU required
```

---

## Commands

```bash
cd C:/Users/masal/source/factoryforge

# Tests (38, ~30s)
python -m pytest -q

# Engine build
cd engine && dotnet build

# Engine headless, deterministic — the CI / regression path (no GPU needed)
"<GODOT>" --headless --path engine/ -- --deterministic --duration=20

# Engine with 3D + screenshot (renders a frame you can then look at)
"<GODOT>" --path engine/ --resolution 1600x900 -- \
    --duration=26 --screenshot=C:/tmp/shot.png --screenshot-at=18

# Default launch is the rigid-body scene; nothing to pass
"<GODOT>" --path engine/

# Fast-forward or slow down a headless run (Space / the toolbar do it live)
"<GODOT>" --headless --path engine/ -- --deterministic --time-scale=4 --duration=30

# Parity check: unchanged Python sidecar drives the C# engine.
# The engine MUST be started with --deterministic or the counts are not
# reproducible and the assertion is meaningless.
python tools/drive_engine.py

# Drive the Python stub from the real PLC
cd sidecar && python -m factoryforge_sidecar demo --driver opcua-client \
    --mapping ../examples/opcua_mapping.json \
    -o url opc.tcp://192.168.1.20:4840 --duration 60

# Expose the scene as an OPC UA server (for Node-RED / SCADA / Ignition)
cd sidecar && python -m factoryforge_sidecar demo --driver opcua-server

# Discover NodeIds on any OPC UA server
cd sidecar && python -m factoryforge_sidecar browse opc.tcp://192.168.1.20:4840
```

---

## Two conventions the parts depend on

**Every part's origin sits on the work plane** (`PartLayout.WorkPlaneY`, y = 0.5),
and each part offsets its own geometry from there — sensor posts and the pusher
pedestal reach down to the floor, the chute's deck hangs below and forward of its
anchor. That is why the scene editor can snap X/Z to the 0.5 m grid, pin Y, and
have any part land correctly. Never bake a mounting height into a scene position.

**A part's instance id is a tag *prefix*, never a whole tag name.** The dispatch
in `SceneEditor._PhysicsProcess` appends the suffix, so registering a part as
`"conveyor.rotate"` looks up `conveyor.rotate.rotate` and silently disables it.

**A part's settings must be in `PartProperties`, or they are lost.** Scene files
store a properties map next to the transform. Anything a part reads in `_Ready`
and is not captured there silently reverts on load — which once cost the
removers their count tags and the sensors their `VisualOnly` flag.

**Simulation controls are engine-global.** Run/pause and time scale go through
`Engine.TimeScale`, so one switch covers the fixed-timestep accumulator, Jolt,
and every part animation. The tag bus is deliberately exempt — it polls from
`_Process`, which Godot still calls at time scale 0, so a paused scene keeps its
PLC session instead of dropping it. `--duration` and `--screenshot-at` run on
wall-clock for the same reason: a paused run whose clock also stopped would
never terminate.

**Two scenes, one tag interface.** The engine launches the *rigid-body* scene:
real colliders, real gravity, a held-out pusher genuinely blocks the line.
`--deterministic` swaps in the fixed-timestep `SortingScene` instead — that one
advances by exactly `TickMs`, mirrors `harness/scene.py`, and is the regression
contract (`tools/drive_engine.py` → `tall=5 short=5`). Jolt cannot promise
reproducible counts, so **anything asserting exact numbers must pass
`--deterministic`.**

Both declare the same ten tags via `SortingTags.Declare`, and both report the
same scene name on the bus, so the same `Sorting.scl` or Node-RED flow drives
either without noticing. Keep it that way: if you add a tag to one, add it to
`SortingTags`.

## Hard-won gotchas — these cost hours, do not rediscover them

1. **The S7-1500 forces a 1000 ms subscription publishing interval**, silently
   revising whatever you request. Any signal that rises and falls inside one
   second can vanish. This is why `opcua_client.py` **polls by default**.
   Raising `queuesize` / lowering `sampling_interval` makes it strictly *worse*
   — the S7 rejects them and then reports nothing at all.

2. **On Windows, `asyncio.sleep()` under ~15.6 ms returns immediately.** The
   clock resolution is 15.6 ms and asyncio treats anything inside that window as
   already expired. A 500-iteration poll loop finished in 10 ms of real time.
   Never count iterations to measure time; measure real elapsed time. Tests must
   wait on events, not poll with short sleeps.

3. **Fixed timestep needs a wall-clock accumulator.** Stepping once per
   `sleep(tick_ms)` runs the sim slow (first version ran at ~60% of real time).
   Both engines accumulate real elapsed time and run whole fixed steps. Never
   switch to variable dt — it destroys reproducibility.

4. **`fake_plc.py` is a logic check only, not an authority.** It passed two SCL
   versions that failed on the real CPU. A Python scan loop reproduces IEC timer
   semantics but not a real CPU's scan behaviour. **Trust the PLC over the
   model.** (I got this wrong and told the user their download hadn't landed
   when their code was in fact running.)

5. **Never write `#tEmit(IN := NOT #tEmit.Q, ...)` in SCL.** It makes `Q` high
   for one scan; on a real S7-1500 the follow-on statement did not execute. Hold
   `IN := TRUE` so `Q` latches, then reset explicitly. There is a warning
   comment in `Sorting.scl`.

6. **Never kill test processes with `os._exit()`.** Each leaks an OPC UA session;
   the S7 allows only a few and then refuses connections for ~35 s. Use
   `demo --duration N`, which shuts down cleanly.

7. **One OPC UA session at a time against the S7.** Running the driver plus a
   separate monitoring client destabilised the server. Instrument the driver
   instead — `tools/drv_trace.py` prints every write and receive.

8. **asyncua's default 4 s connect timeout is too short for a real S7.** Use
   `timeout=10`.

9. **Never run Node-RED against `C:\Users\masal\.node-red`** — it holds the
   user's own flows. Use a temp userDir with a **junction** to their
   `node_modules`, and remove it with `cmd //c rmdir` (not `rm -rf`, which
   follows the junction and would delete their packages).

---

## Current state

**Working end to end**, verified against real hardware:

- Tag bus, drivers (OPC UA client + server, Modbus, mock)
- OPC UA client → real S7-1500 → boxes sort by height (**100.0% perfect split: 99 tall / 99 short** with SCL v0.4)
- Node-RED replacing the PLC entirely — **9 tall / 9 short, perfect split**
- Godot C# engine with 3D geometry driven by tags, screenshot in README
- The unchanged Python sidecar drives the C# engine (`tools/drive_engine.py`)
- 41 tests passing; CI needs no Siemens software and no GPU

**Resolved imperfection:** In SCL v0.3, ~12% of tall boxes slipped past the pusher due to timing margin (0.6s catch window vs ~100ms OPC UA round-trip jitter). Fixed in SCL v0.4 by setting `PUSH_HOLD` `T#500MS` → `T#1S500MS`, verified live on real S7-1500 (99 tall / 99 short).

---

## Next steps, in order

1. **`PUSH_HOLD` → `T#1S500MS`** (SCL v0.4) — **Verified on hardware (99 tall / 99 short, 100% split)**.
2. **Finish M2**: integer voxel grid, free-look camera, C# protocol tests in CI — **Done**.
3. **M3 — physics**: surface-velocity belt constraint, non-jittering rigid boxes, raycast sensors, pusher mechanism, emitter/remover, control panel — **Done**.
4. **M4 — Scene Editor**: part palette, place/rotate/delete with grid snapping, save/load JSON format, top toolbar, live tag forcing UI, property inspector — **Done**.
5. **M5 — Siemens Breadth**: PLCSIM Advanced Simulation Runtime API driver, Snap7 S7-protocol driver — **Done**.
6. **M6 — v1 Release**: Windows/Linux packaging, student getting-started guide, part & driver authoring guides — **Done**.

Deliberately skipped at the user's request: OpenPLC/Modbus cross-check.

## Working style the user expects

- Verify against real hardware, not models. Render a frame and look at it.
- State findings with the evidence that produced them.
- Flag your own mistakes plainly and correct them.
- Ask before writing to their PLC or touching their Node-RED install.
