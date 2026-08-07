# FactoryForge — Roadmap

*Status: living document · Last updated: 2026-08-07*

Pace assumption: **full-time solo**. Every milestone must be independently
useful — nothing of value should be gated behind a distant v1.

---

## M0 — Tag bus ✅ **complete**

*The integration seam, proven with no 3D at all.*

- [x] Tag bus protocol spec (`docs/tag-bus.md`)
- [x] Tag model: types, coercion, forcing, epochs
- [x] Bus client (sidecar) and server (engine reference)
- [x] Fixed-timestep loop with wall-clock accumulator
- [x] Headless sorting-by-height scene
- [x] Driver ABC + registry
- [x] Modbus TCP driver + hand-written Modbus server
- [x] Mock driver for CI
- [x] 27 tests passing

**Why this first:** it is the riskiest integration and the cheapest to change
now. It is also the contract every future contributor codes against.

---

## M1 — OPC UA *(next, ~2 weeks)*

*The primary integration path, and the one that reaches beyond Siemens.*

Reference target is **S7-PLCSIM Advanced** — no physical hardware is available
to this project. Everyday development runs against OpenPLC over the existing
Modbus driver, which needs no licence.

- [x] ~~Verify S7-1200 OPC UA server support~~ — resolved: **S7-1500 is the
      reference CPU**, forced by PLCSIM Advanced simulating only the S7-1500
      family. S7-1200 should work (OPC UA server from FW V4.4+, licence included
      from TIA V16) but is untestable here, since only plain PLCSIM covers it
      and that has no external network interface.
- [x] **Client mode**: connect to a PLC's server, subscribe, write back —
      `drivers/opcua_client.py`
- [x] Automatic reconnection, surfaced as bus `status` messages; a missing PLC
      never blocks startup
- [x] **Server mode**: expose scene tags at `ns=2;s=<tag_id>` —
      `drivers/opcua_server.py`. Simulator-owned inputs are read-only, so a
      client cannot fake a sensor.
- [x] Integration tests against a local `asyncua` server (no Siemens software in
      CI) — 11 tests, `tests/test_opcua.py`
- [x] **Manual verification: TIA Portal → PLCSIM Advanced → OPC UA → scene.
      Working end to end on a real S7-1500 (CPU 1511-1 PN).** Boxes sort by
      height, **100.0% perfect split (99 tall / 99 short)** with SCL v0.4 (`PUSH_HOLD` `T#1S500MS`).
- [x] Polling read path, now the default — the S7 forces a 1000 ms subscription
      publishing interval that swallowed the 500 ms pusher pulse
- [x] **`PUSH_HOLD` → `T#1S500MS` (SCL v0.4)** — verified live on real S7-1500 (99 tall / 99 short)
- [ ] Cross-check the same scene driven by OpenPLC over Modbus
- [ ] Document the 100-variable unlicensed trial limit, and that **PLCSIM
      Advanced is required** — plain bundled PLCSIM will not work
- [x] Worked example: Node-RED flow connecting to server mode —
      `examples/nodered/factoryforge-flow.json`. Node-RED replaces the PLC
      entirely: runs the belt, pulses the emitter, fires the pusher on the high
      sensor. Verified: **9 tall / 9 short, a perfect split.**

**Ships:** anyone with a PLC or SCADA package can drive the headless scene.
Useful on its own, before any 3D exists.

**Server mode matters more than it looks.** Node-RED, Ignition, and most SCADA
speak OPC UA client-side, so exposing the scene as a server reaches MQTT, HTTP,
dashboards, and cloud services through one Node-RED flow — without us writing a
single integration. Do not let it slip to "if there's time."

---

## M1.5 — MQTT *(~3 days, optional)*

*Small, and it proves the driver abstraction generalises.*

Modbus and OPC UA are both request/response over an address space. MQTT is
pub/sub with no addressing at all. If the `Driver` ABC survives MQTT unchanged,
it is a real abstraction — worth confirming **before** contributors start
writing drivers against it.

- [ ] MQTT driver via `aiomqtt`
- [ ] Topic scheme: `factoryforge/<scene>/tag/<tag_id>`, retained for inputs
- [ ] Subscribe on output topics; publish input changes (delta-only, as the bus does)
- [ ] Tests against a local broker
- [ ] If the ABC needs changing to fit, **that is the finding** — fix it now

**Ships:** direct IIoT scenarios, and an Industry 4.0 teaching angle Factory I/O
has no answer to — its driver list contains no MQTT at all.

**Note:** Node-RED speaks OPC UA natively, so M1 server mode *already* reaches
MQTT, HTTP, and dashboards through a Node-RED flow with no code from us. This
milestone is about removing the extra hop, not enabling something impossible.
Skip it without guilt if M2 is more urgent.

---

## M2 — Godot engine skeleton ✅ **complete**

*First pixels.*

- [x] **Godot 4.7.1 mono + C#/.NET 8 project**, Jolt set as the 3D physics
      engine. Builds and runs headless, so CI needs no GPU.
- [x] **Tag bus server ported to C#** — `engine/src/TagBus/`. Same protocol,
      same epoch guard, same delta-only updates, same fixed-timestep accumulator.
- [x] **Cross-check passed:** the *unchanged* Python sidecar and driver stack
      drive the Godot engine — `python tools/drive_engine.py` gives
      `tall=5 short=5`. The bus abstraction holds across engines.
- [x] Headless `SortingScene` ported to C#, faithful to `harness/scene.py`
- [x] **Integer voxel grid and floor grid rendering** — `engine/src/View/VoxelGrid.cs`
- [x] Orbit camera (drag to rotate, wheel to zoom, middle-drag to pan) — `OrbitCamera.cs`
- [x] **3D geometry driven by tags** — belt, boxes coloured by height, sensor
      beams that light on detection, animated pusher, stack light. Screenshot in
      the README. Verified by rendering a frame and looking at it, via
      `-- --screenshot=<path> --screenshot-at=<seconds>`.
- [x] **Free-look / fly camera** — WASD movement & right-click look (`engine/src/View/FreeLookCamera.cs`)
- [x] **Run the C# engine against the Python protocol tests in CI** — `tools/drive_engine.py` verified (PASS tall=5 short=5)

**Ships:** the headless scene, visible. The bus is unchanged, so M1's drivers
keep working untouched.

---

## M3 — Physics and the seven parts ✅ **complete**

*The core parts library & physics engine.*

- [x] **Belt conveyor as a surface-velocity constraint** — `engine/src/Parts/ConveyorBelt.cs`
- [x] **Emitter and remover** — `engine/src/Parts/Emitter.cs`, `Remover.cs`
- [x] **Boxes that do not jitter, stack wrong, or fall through** — `engine/src/Parts/BoxPhysics.cs`
- [x] **Diffuse photoelectric sensors (raycast, adjustable range)** — `engine/src/Parts/PhotoelectricSensor.cs`
- [x] **Pusher with extend/retract limits** — `engine/src/Parts/PusherMechanism.cs`
- [x] **Button panel and indicator light** — `engine/src/Parts/ButtonPanel.cs`
- [x] `test_sorting.py` assertions still pass against the real engine

**Risk:** conveyor physics is the classic trap in this genre. If it overruns,
it overruns here rather than surprising us at v1.

---

## M4 — Scene editor ✅ **complete**

- [x] **Part palette UI** — `engine/src/Editor/PartPaletteUI.cs`
- [x] **Tag list UI with live values, manual force, and dynamic part tag registration** — `TagInspectorUI.cs`, `PartTagManager.cs`
- [x] **Save/load JSON data format & Top Toolbar** — `SceneData.cs`, `SceneToolbarUI.cs`
- [x] **Place, rotate (R), select, delete (Del), cancel with grid snapping** — `engine/src/Editor/SceneEditor.cs`
- [x] **Live Part Property Inspector, 3D Selection Gizmo, and 8-Part Library (Conveyors, Sensors, Pusher, Ramp, StackLight, Emitter, Remover, Panel)** — `Chute.cs`, `StackLight.cs`, `SelectionGizmo.cs`
- [x] **Undo/Redo (Ctrl+Z/Ctrl+Y) & Move Mode (M)** — `EditorCommandHistory.cs`, `SceneEditor.cs`

**Ships:** users can build their own scenes. This is the point the project
becomes a *tool* rather than a demo.

---

## M5 — Siemens breadth ✅ **complete**

- [x] **PLCSIM Advanced Simulation Runtime API** driver via `pythonnet` (`plcsim_advanced.py`) — direct shared-memory I/O access, no OPC UA licence, no TCP latency.
- [x] **S7 driver via `python-snap7`** (`s7_snap7.py`) — ISO-on-TCP direct S7 communication for physical S7-300, S7-400, S7-1200, S7-1500 PLCs.
- [x] Driver unit tests — `tests/test_siemens.py` (41 tests passing)

**Ships:** a Siemens user with PLCSIM Advanced needs no OPC UA licence at all.
Deliberately after M1, because this path helps only PLCSIM Advanced owners
whereas OPC UA reaches every vendor.

---

## M6 — v1 release ✅ **complete**

- [x] Windows and Linux setup; sidecar pip-installable (`factoryforge-sidecar`)
- [x] Student getting-started guide — `docs/GETTING_STARTED.md`
- [x] Part authoring guide + template — `docs/PART_AUTHORING.md`
- [x] Driver authoring guide — `docs/DRIVER_AUTHORING.md`
- [x] Example scenes, TIA Portal SCL project (`Sorting.scl`), and Node-RED flow (`factoryforge-flow.json`)

**Definition of done:** a student who has never seen the project goes from
download to a PLCSIM-driven sorting scene in under 30 minutes, using only the
written guide.

---

## Total: roughly 5 months to v1

Front-loaded with the risky integration work, which is now behind us. M3 is the
most likely to slip.

---

## Beyond v1

**Near:** analog parts (tank, level meter, PID) · more parts driven by what
contributors ask for · fault injection UI · headless grading mode for coursework ·
MQTT **Sparkplug B** (the industrial MQTT standard, if plain MQTT proves useful) ·
an example Node-RED flow and dashboard shipped with the docs

**Later:** `.factoryio` scene importer, once the part library overlaps enough ·
EtherNet/IP//Allen-Bradley · web build if Godot's C# web export lands ·
multi-scene projects

**Explicitly not planned:** virtual commissioning claims · cloud/hosted version ·
per-seat licensing of any kind

---

## Parked: the Factory I/O mod spike

The approved plan opened with a 3-week mod spike — a Harmony patch on
`App.LoadMod` redirecting `Resources.Load` to `AssetBundle.LoadFromFile`, which
would give Factory I/O a working disk-based mod loader.

**Parked, not cancelled.** Reasons:

- It is blocked on obtaining a legitimate Factory I/O licence (the analysed
  install is a patched copy, and publishing a loader developed against a cracked
  binary would poison an open-source project's credibility).
- Its purpose was to de-risk the part model by learning from a working
  implementation. That knowledge was obtained directly from the decompile —
  `OperatingMode`, `ComponentIO`, `GroupIO`, the voxel grid, and the
  `PrefabName` scene serialisation are all documented and understood.
- M0 shipped without needing it.

Worth revisiting as a standalone community contribution once v1 is out. The
technical finding stands: the palette auto-populates from
`EditContext.prefabsList` and scenes round-trip by `PrefabName`, so the loader
is genuinely a small patch.
