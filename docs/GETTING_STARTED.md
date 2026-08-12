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
| `Space` | Pause / resume — freeze the line and read every sensor at that instant |
| `Ctrl+R` | Reset the run (machines stay where they are) |
| `C` | Switch between the orbit and fly cameras |
| `F4` / `F5` | I/O wiring panel · driver connection |

The toolbar also carries a **0.25×–4× rate selector**: slow a fast interlock
down to watch it, or fast-forward a long cycle. Your PLC stays connected while
the scene is paused.

If you need repeatable results — grading an exercise, or comparing two runs — add
`-- --deterministic` for the fixed-timestep scene. Both expose the same tags, so
your program does not change.

---

## 🔌 Connecting to Siemens S7-PLCSIM Advanced

FactoryForge supports **two direct connection methods** to Siemens S7-1500 PLCs:

### Method A: Direct PLCSIM Advanced Native API Driver (Recommended — Zero Licence Cost)

1. Open **S7-PLCSIM Advanced Control Panel**.
2. Set Interface to **PLCSIM Virtual Ethernet Adapter**.
3. Start a virtual CPU named `Sorting_PLC` with IP `192.168.1.20`.
4. Download your TIA Portal SCL program (e.g. [`examples/tia/Sorting.scl`](../examples/tia/Sorting.scl)).
5. Launch the FactoryForge sidecar with the native PLCSIM driver:

```bash
python -m factoryforge_sidecar demo --driver plcsim-advanced
```

---

### Method B: OPC UA Server Connection

1. Enable **OPC UA Server** in TIA Portal CPU properties under *Protection & Security -> OPC UA*.
2. Compile and download to PLCSIM Advanced (`192.168.1.20:4840`).
3. Discover NodeIds using the sidecar browse tool:

```bash
python -m factoryforge_sidecar browse opc.tcp://192.168.1.20:4840
```

4. Launch the sidecar with your OPC UA mapping:

```bash
python -m factoryforge_sidecar demo --driver opcua-client -o url opc.tcp://192.168.1.20:4840
```

---

## 🛠️ Using the 3D Scene Editor

* **`Left-Click`**: Select part in 3D or place active palette component on the voxel grid floor.
* **`R`**: Rotate placement preview 90°.
* **`M`**: Move selected component to a new voxel location.
* **`Delete` / `Backspace`**: Delete selected component.
* **`Ctrl+Z` / `Ctrl+Y`**: Undo / Redo placement or deletion.
* **`F4`**: Open the **Visual I/O Driver Wiring Panel** to map PLC addresses (`%I0.0`, `%Q0.0`) directly to component tags.
* **`C`**: Toggle between **Orbit Camera** and **Free-Look Fly Camera** (WASD + Right-Click drag).
