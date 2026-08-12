# 🚀 FactoryForge Student Getting Started Guide

Welcome to **FactoryForge**, a free, open 3D factory simulator for learning PLC programming.

---

## 📋 System Requirements

* **Operating System:** Windows 10/11 or Linux (x86_64)
* **Python:** Python 3.10+ (Python 3.12 recommended)
* **Godot:** Godot 4.7.1 Mono / C#
* **PLC Target (Optional):** Siemens S7-PLCSIM Advanced v3.0+, TIA Portal V14-V19, or Node-RED

---

## ⚡ Quick Start (5 Minutes)

### 1. Clone & Install Sidecar

```bash
cd C:/Users/masal/source/factoryforge
pip install -e "sidecar[dev,opcua]"
```

### 2. Run the Test Suite

Verify that all 41 unit & protocol drivers tests pass:

```bash
python -m pytest -q
```

### 3. Launch the 3D Engine

Launch the Godot 4.7 C# factory simulation engine:

```bash
cd engine
dotnet build
"<GODOT_CONSOLE_EXE>" --path .
```

You get the **physics scene**: rigid-body cartons on a real conveyor. Useful
keys straight away:

| Key | Action |
|---|---|
| `F1` | Switch between **Edit** and **Run** mode |
| `Space` | Pause / resume — freeze the line and read every sensor at that instant |
| `Ctrl+R` | Reset the run (machines stay where they are) |
| `C` | Switch between the orbit and fly cameras |
| `F4` / `F5` | I/O wiring panel · driver connection |

The toolbar also carries a **0.25×–4× rate selector**: slow a fast interlock
down to watch it, or fast-forward a long cycle. Your PLC stays connected while
the scene is paused.

### Edit mode and Run mode

A click has to mean one thing at a time. In **Edit** mode it selects a part so
you can move (`M`) or delete it; in **Run** mode the parts are furniture and the
only things that answer a click are the controls an operator could reach. The
toolbar shows which mode you are in, and the parts palette hides itself while
the line is running.

Press `F1`, then click the buttons on the **control panel** beside the belt:

| Control | Tag | Behaviour |
|---|---|---|
| Start (green) | `panel.start` | Momentary — high for exactly one scan per click |
| Stop (black) | `panel.stop` | Momentary |
| Reset (blue) | `panel.reset` | Momentary |
| E-Stop (red mushroom) | `panel.estop` | Maintained — click to strike, click again to release |

Momentary means what it does on a real panel: one click is one clean rising
edge, however long you hold the mouse down. Write your logic against the edge,
not the level.

The **E-Stop is wired normally closed**, like the real thing: `panel.estop` is
**true while the circuit is healthy** and goes false when the mushroom is
struck. If your program runs happily with that tag false, it would also run with
the wire to the E-stop cut — which is the exact failure NC wiring exists to
catch. This is the cheapest place to learn that.

If you need repeatable results — grading an exercise, or comparing two runs — add
`-- --deterministic` for the fixed-timestep scene. Both expose the same tags, so
your program does not change.

---

## 🔌 `connect` vs `demo` — read this first

The engine speaks one protocol: its own tag bus. **Every PLC protocol lives in
the Python sidecar**, so connecting a PLC always means starting the sidecar
alongside the running engine.

There are two subcommands and picking the wrong one wastes an afternoon:

| | |
|---|---|
| **`connect`** | Attaches to an engine that is **already running**. This is the one you want with the 3D engine. |
| **`demo`** | Starts its **own** headless Python scene on the bus port. For a quick driver check with no Godot involved. |

Run `demo` while the 3D engine is up and it finds port 7411 taken — or worse,
binds first and drives a scene you cannot see while the 3D one sits still.

```bash
# Terminal 1
godot --path engine/

# Terminal 2
cd sidecar
python -m factoryforge_sidecar connect --driver plcsim-advanced -o instance Sorting_PLC
```

The **F5 Driver dialog** does exactly this for you: pick a driver, fill in the
address, and *Apply & Connect* starts the sidecar and copies the command to your
clipboard in case you would rather run it yourself.

---

## 🔌 Connecting to Siemens S7-PLCSIM Advanced

FactoryForge supports **two direct connection methods** to Siemens S7-1500 PLCs:

### Method A: Direct PLCSIM Advanced Native API Driver (Recommended — Zero Licence Cost)

1. Open **S7-PLCSIM Advanced Control Panel**.
2. Set Interface to **PLCSIM Virtual Ethernet Adapter**.
3. Start a virtual CPU named `Sorting_PLC` with IP `192.168.1.20`.
4. Download your TIA Portal SCL program (e.g. [`examples/tia/Sorting.scl`](../examples/tia/Sorting.scl)).
5. With the engine running, attach the native PLCSIM driver to it:

```bash
cd sidecar
python -m factoryforge_sidecar connect --driver plcsim-advanced -o instance Sorting_PLC
```

---

### Method B: OPC UA Server Connection

1. Enable **OPC UA Server** in TIA Portal CPU properties under *Protection & Security -> OPC UA*.
2. Compile and download to PLCSIM Advanced (`192.168.1.20:4840`).
3. Discover NodeIds using the sidecar browse tool:

```bash
python -m factoryforge_sidecar browse opc.tcp://192.168.1.20:4840
```

4. Attach the sidecar to the running engine with your OPC UA mapping:

```bash
cd sidecar
python -m factoryforge_sidecar connect --driver opcua-client \
    -o url opc.tcp://192.168.1.20:4840 --mapping <your io_mapping.json>
```

---

## 🏭 Connecting a scene you built yourself

The sorting demo ships with a mapping file. A line you build in the editor does
not — it has its own parts and its own tag ids, and nothing on the PLC side
knows them yet. The path is:

**1. Name your parts.** Click a part and edit **Name** in the properties panel.
The name is the tag prefix, so a pusher called `reject_pusher` gives you
`reject_pusher.extend`. Do this before writing any PLC code: the default ids
(`pushermechanism_2`) are unreadable, and worse, unstable — delete a part and
re-place it and the number moves, silently breaking a mapping that points at the
old one.

Renaming is refused, with a reason, for parts that only mirror tags the
simulation owns — the sorting demo's belt and sensors.

**2. Export the I/O.** Open **F4 Wiring** and press **Export & Copy Command**.
You get two files, and the absolute path to both:

* `io_mapping.json` — every tag id with a blank address, ready to fill in.
  Re-exporting after adding a part keeps the addresses you already typed and
  only adds new blanks.
* `io_tags.csv` — the same list with descriptions, direction, and a suggested
  IEC address. Open it in a spreadsheet and build your PLC symbol table from it.

The direction column is worth reading carefully. It is written from the
**controller's** point of view: an *Input* is something the PLC reads (a sensor,
a button) and the simulation writes.

**3. Find your NodeIds** (OPC UA only) and paste them into `io_mapping.json`:

```bash
cd sidecar
python -m factoryforge_sidecar browse opc.tcp://192.168.1.20:4840
```

**4. Connect**, via the F5 dialog or the command it copies.

Add or delete a part while a driver is connected and the engine republishes the
I/O list automatically, so the driver sees the new tags without a reconnect.

---

## 🛠️ Using the 3D Scene Editor

* **`Left-Click`**: Select part in 3D or place active palette component on the voxel grid floor.
* **`R`**: Rotate placement preview 90°.
* **`M`**: Move selected component to a new voxel location.
* **`Delete` / `Backspace`**: Delete selected component.
* **`Ctrl+Z` / `Ctrl+Y`**: Undo / Redo placement or deletion.
* **`F1`**: Switch between **Edit** and **Run** mode.
* **`F4`**: **I/O Wiring** — map PLC addresses to tags, and export `io_mapping.json` / `io_tags.csv`.
* **`F5`**: **Driver** — pick a protocol and start the sidecar against this engine.
* **Save / Load**: choose a file, so you can keep more than one line and share it. The filename becomes the scene name reported on the tag bus.
* **`C`**: Toggle between **Orbit Camera** and **Free-Look Fly Camera** (WASD + Right-Click drag).
