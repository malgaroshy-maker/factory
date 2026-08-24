# FactoryForge 🏭

[![Godot 4.7](https://img.shields.io/badge/Godot-v4.7.2--mono-blue?logo=godotengine)](https://godotengine.org/)
[![Python 3.12](https://img.shields.io/badge/Python-3.12+-green?logo=python)](https://www.python.org/)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
[![Tests](https://img.shields.io/badge/Tests-73%20Passed-brightgreen)](tests/)
[![Siemens S7-1500](https://img.shields.io/badge/Siemens-S7--1500%20Verified-009999?logo=siemens)](examples/tia/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

*A free, open 3D factory simulator for learning PLC programming — a modern, customizable open replacement for Factory I/O.*

**Author & Creator:** Mahamed Algaroshy (محمد الجروشي)  
**Repository:** [github.com/malgaroshy-maker/factory](https://github.com/malgaroshy-maker/factory)

---

## 🌟 Overview

**FactoryForge** allows students, automation engineers, and software developers to write PLC logic (Ladder Diagram, SCL, Function Block Diagram) in TIA Portal, OpenPLC, Node-RED, or Ignition SCADA, and watch it drive a real-time 3D physics-based factory in Godot 4.7.

No accounts, no per-seat subscription fees, and 100% open for custom part & driver creation.

![FactoryForge 3D Engine & Scene Editor Demo Video](docs/images/demo_video.gif)

```
┌────────────────────────────┐          ┌──────────────────────────┐
│  SIM ENGINE (Godot 4.7/C#) │          │  DRIVER SIDECAR (Python) │
│                            │          │                          │
│  3D render + Jolt physics  │  tag bus │  asyncua      (OPC UA)   │
│  scene editor / voxel grid │ ◄──────► │  pythonnet    (PLCSIM)   │
│  15-part library           │    WS    │  python-snap7 (S7)       │
│  tag registry (authority)  │   JSON   │  built-in     (Modbus)   │
└────────────────────────────┘          └──────────────────────────┘
```

---

## ✨ Key Features

* 🧲 **Material-aware sensing**: items carry a material, so an inductive sensor sorts metal from cardboard instead of being a second presence sensor.
* ⚠️ **Break the machine on purpose (`⚠ Fault`)**: arm the fault tool and click a conveyor or a pusher. The drive stops **while its command is still on** — the belt disobeys, its fault beacon lights, and `<part>.fault` goes true for the PLC to read. A jammed cylinder freezes mid-stroke rather than returning home, so the limit switches are the only honest thing to read. Until this existed every actuator did exactly what it was told, which made half of real PLC work unteachable: an interlock exists precisely because the plant does not always obey.
* 🎚️ **Analog I/O**: Float tags end to end — a modulating valve and a level transmitter, so you can write a real PID against a nonlinear process rather than only on/off logic.
* ⏯️ **Run / Pause / Reset & time scale (0.25×–4×)**: freeze the line mid-cycle to read every sensor and actuator at that instant, or slow a fast sequence down to watch an interlock. The PLC stays connected while paused.
* 🎮 **Godot 4.7 C# 3D Engine & Jolt Physics**: 60 FPS 3D rendering with soft shadows, SSAO, metallic shaders, and continuous collision detection.
* 📦 **Real rigid-body cartons**: mass from carton density, friction tuned per material pair (rubber belt, cardboard, steel chute), boxes that accumulate behind a blocked diverter instead of passing through it.
* 🏠 **Start screen with five templates**: open on a chooser rather than cold into one demo. Each template teaches a different thing — momentary buttons and a latching E-stop, analog level control with a nonlinear process, sorting on a measurement instead of two bits, a checkweigher with metal detection — plus recent scenes and the full key list.
* 🕹️ **Operate any component by hand (`F1`)**: switch the toolbar from **`✎ Build`** to **`👆 Operate`** and click a conveyor, a pusher, a stack light lamp or a tank valve directly — not just the operator panel's Start/Stop/Reset/E-stop. **Every shipped scene answers to its panel**: Start runs the line, Stop stops it, the mushroom latches a trip that only Reset clears, and the panel's setpoint pot is the one number that scene is about — the level to hold, the height that counts as tall, the weight that counts as a reject, the batch to make. Drag the knob mid-run and the line changes what it does, with no code edited. A banner names what's clickable, hovering outlines it, and every part's own property panel carries a live toggle or slider for its I/O too, so you can see what a part does before writing a line of PLC code against it.
* 🛠️ **3D Scene Editor Suite**: click a placed part and **drag it** to a new cell — one gesture, one **`Ctrl+Z`** — with grid snapping, rotation (**`R`**), duplicate (**`Ctrl+D`**), a selection wireframe gizmo, and undo/redo throughout. The property panel names what the selected part responds to, so none of it has to be guessed.
* 🔌 **Visual I/O Driver Wiring Panel (`F4`)**: Centered split-screen modal — click a PLC address (`%I0.0`, `%Q0.0`), then click the component tag to map it to. **Auto-map** suggests an address for every tag in the loaded scene, and **Export** writes `io_mapping.json` and `io_tags.csv` for the sidecar and for whoever is building the PLC side.
* 🏷️ **Live tag inspection and forcing**: the Tag Inspector lists every tag the loaded scene owns with its live value, and forces any of them — bit, int and float alike — with a typed value. A `🔓 N forced` chip in the toolbar shows what is being held by hand and releases it all in one click. The parts that measure something — the light curtain, the level tank, the digital display — also read out in 3D on the part itself.
* 🧪 **A built-in exercise per scene**: `python tools/try_scene.py --scene <id>` (or the toolbar's **🧪 Try** button) spawns or attaches to the engine and drives the scene the way a PLC would — pressing the panel's own buttons, timing the E-stop against a 200 ms limit, turning the setpoint pot mid-run to prove the line follows it — then reports pass/fail. The thing to run before writing a real program against it.
* 🏭 **Native Siemens Integration**: **all three Siemens paths verified driving the 3D scene from a virtual S7-1500** — PLCSIM Advanced Simulation Runtime API (shared memory, no network, no OPC UA licence), OPC UA client, and Snap7 ISO-on-TCP. Belt, emitter, sensors, diverter and counters all run off the CPU's own program.
* 📊 **Multi-Protocol SCADA Support**: Built-in OPC UA client/server, Modbus TCP server, and Node-RED integration.

---

## 📦 15-Part Industrial Component Library

The tag ids below are the built-in scene's names. **A part's Name is its tag
prefix** — rename a pusher to `reject` in the property panel and its tags become
`reject.extend`, `reject.extended`, `reject.retracted`. That is the whole naming
rule, and it is what makes a scene you build addressable from a PLC.

| Component | Description | Tag Bus Interface |
|---|---|---|
| **Conveyor Belt** | Surface-velocity belt with side rails and legs, plus a drive-fault beacon | `conveyor.rotate` (Bit, Output) · `conveyor.fault` (Bit, Input) |
| **Photoelectric Sensor** | Diffuse beam sensor, reflects off the item itself | `sensor.detect` (Bit, Input) |
| **Retroreflective Sensor** | Beams to a reflector post across the lane; sees matt and dark items a diffuse sensor misses | `sensor.detect` (Bit, Input) |
| **Inductive Sensor** | Responds to metal only — cardboard passes it as if the lane were empty | `sensor.detect` (Bit, Input) |
| **Light Array** | Light curtain of 12 beams; reports the height of the tallest blocked beam, so one part replaces a low/high sensor pair | `lightarray.height` (Float, Input), `.blocked` (Bit, Input) |
| **Pneumatic Pusher** | Cylinder housing, chrome shaft & orange face plate; a jam freezes it mid-stroke | `pusher.extend`, `pusher.extended`, `pusher.retracted`, `pusher.fault` |
| **Inclined Ramp (Chute)** | 30° gravity chute with guide rails; incline and friction are a matched pair so cartons actually slide | Physical static body |
| **Stack Light** | 3-stage industrial tower light (Green, Yellow, Red) | `stacklight.green`, `yellow`, `red` |
| **Digital Display** | 3D 7-segment LED panel displaying live integer counts | `display.value` (Int, Output) |
| **Roller Conveyor** | Driven roller deck for pallets and totes that would scuff a belt; rollers spin at the true surface speed | `rollerconveyor.rotate` (Bit, Output) · `.fault` (Bit, Input) |
| **Weight Scale Conveyor**| Integrated load cell scale reading the carton's mass **in grams** — 720 g for a short carton, 2160 g for a tall one, 12960 g for a metal one — and showing it on the scale | `weighconveyor.weight` (Int, Input) |
| **Box Emitter** | Spawner emitting tall & short rigid cartons, optionally every Nth in metal | `emitter.emit` (Bit, Output) |
| **Box Remover** | Area3D zone despawning items & incrementing a counter; the counted tag is pickable, so two removers can feed one total | `remover.count` (Int, Input) |
| **Control Panel** | Operator station you can actually press. Start/Stop/Reset are momentary — one clean scan per click, however long you hold the mouse — and the mushroom is a maintained E-stop wired **normally closed**, so its tag is true while the circuit is healthy. The setpoint pot is **dragged**, reads out in the scene's own units on its scale plate, and turns itself to match a tag driven from a PLC | `panel.start`, `.stop`, `.reset`, `.estop` (Bit, Input) · `panel.setpoint` (Float, Input) · `panel.green`, `.red` (Bit, Output) |
| **Level Tank** | Analog process tank; outflow follows Torricelli, so process gain varies with level and a PID tuned full overshoots when empty | `tank.fill`, `tank.drain` (Float, Output), `tank.level` (Float, Input) |

---

## ⚡ Quick Start

### Just want to run it? Download a release

Grab the archive for your platform from
[Releases](https://github.com/malgaroshy-maker/factory/releases), extract it,
and run `FactoryForge`. **No Godot, no .NET SDK and no Python needed** — the
sidecar that speaks every PLC protocol ships frozen alongside the engine, and
F5's *Apply & Connect* finds it automatically.

> **Windows will warn you on first run.** These builds are not code-signed, so
> SmartScreen shows "Windows protected your PC" — click *More info → Run
> anyway*. That warning means the binary has no purchased certificate attached,
> not that anything is wrong with it. See
> [PACKAGING.md](docs/PACKAGING.md#code-signing--not-signed-and-the-download-page-says-so)
> for why this project does not buy one.

Everything below is for building from source instead.

### 1. Installation

```bash
git clone https://github.com/malgaroshy-maker/factory.git
cd factory
pip install -e "sidecar[dev,opcua]"
```

### 2. Run Test Suite (73 Tests)

```bash
python -m pytest -q
```

Or the full plan — build, the Python suite, the engine's own self-tests,
determinism, the engine↔sidecar seam and robustness. No PLC needed:

```bash
python tools/test_plan.py          # --gui adds the display-dependent check
```

### 3. Launch 3D Simulation Engine

```bash
python run.py
```

`run.py` finds Godot (or tells you exactly what to install and where), builds
the C# engine, and launches it — no separate `dotnet build` step, and it works
the same on Windows and Linux. (Windows users can also double-click
`run_factoryforge.bat`, which just calls `run.py`.)

This runs the **physics scene**: Jolt rigid-body cartons, real collisions, and
components whose properties genuinely change how the line behaves — speed up the
belt and boxes outrun the diverter; hold the pusher out and the line backs up
behind it.

```bash
# Fixed-timestep scene instead: reproducible, and the regression contract.
python run.py -- --deterministic
```

Both scenes expose the **same 16 tags** and report the same scene name, so a PLC
program, Node-RED flow or SCADA client drives either one unchanged. Use
`--deterministic` whenever you need repeatable counts — CI and
`tools/drive_engine.py` rely on it.

---

## 🔌 Driver Execution Commands

The engine speaks only its own tag bus; every PLC protocol lives in the Python
sidecar. **`connect` attaches to a running engine — that is the one to use with
the 3D view.** (`demo` starts its own headless Python scene instead, which is
for checking a driver with no Godot in the picture.) The **F5 Driver dialog**
runs these for you and copies the command.

```bash
cd sidecar

# Siemens PLCSIM Advanced (Shared Memory API — Zero Licence Cost)
python -m factoryforge_sidecar connect --driver plcsim-advanced -o instance Sorting_PLC

# Siemens S7 ISO-on-TCP (Snap7)
python -m factoryforge_sidecar connect --driver s7-snap7 -o host 192.168.1.20 -o db 1

# OPC UA Client (connecting to an S7-1500 @ 192.168.1.20)
python -m factoryforge_sidecar connect --driver opcua-client \
    -o url opc.tcp://192.168.1.20:4840 --mapping io_mapping.json

# OPC UA Server (exposing the scene to Node-RED / SCADA)
python -m factoryforge_sidecar connect --driver opcua-server
```

Building your own scene? Name your parts in the inspector, then **F4 → Export**
writes `io_mapping.json` and `io_tags.csv` for the tags that scene actually has.
See [Getting Started](docs/GETTING_STARTED.md#-connecting-a-scene-you-built-yourself).

---

## 📚 Documentation Sitemap

| Document | Description |
|---|---|
| 🚀 **[GETTING_STARTED.md](docs/GETTING_STARTED.md)** | Step-by-step setup for PLCSIM Advanced, TIA Portal & Node-RED |
| 🛠️ **[PART_AUTHORING.md](docs/PART_AUTHORING.md)** | Guide & template for building custom 3D factory components |
| 🔌 **[DRIVER_AUTHORING.md](docs/DRIVER_AUTHORING.md)** | Guide for adding custom Python protocol drivers |
| ✅ **[TEST_PLAN.md](docs/TEST_PLAN.md)** | What is tested, what is not, and the last run's results |
| 📦 **[PACKAGING.md](docs/PACKAGING.md)** | Building a distributable release — verified end to end |
| 📋 **[PLAN.md](docs/PLAN.md)** | Architectural specifications, design choices, and status |
| 🧹 **[LOOSE_ENDS_PLAN.md](docs/LOOSE_ENDS_PLAN.md)** | Claims without code, controls without effect — what a full sweep of the app found, and the plan to close it |
| 🗺️ **[ROADMAP.md](docs/ROADMAP.md)** | Milestone completion tracking |
| 📑 **[PRD.md](docs/PRD.md)** | Problem statement, target audience, and success criteria |
| ⚡ **[tag-bus.md](docs/tag-bus.md)** | WebSocket tag bus protocol specification |
| 🤖 **[AGENTS.md](AGENTS.md)** | Developer cheat sheet, paths, and hardware gotchas |

---

## ⚖️ License

Distributed under the **MIT License**. See `LICENSE` for more information.
