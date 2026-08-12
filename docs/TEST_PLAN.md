# FactoryForge Test Plan

*Last run: 2026-08-12. Results at the bottom.*

A single command runs all of it:

```bash
python tools/test_plan.py
```

It exits non-zero if anything fails, prints a table, and needs no PLC and no
Siemens software. Add `--gui` to include the checks that need a display.

---

## Why this shape

The project has three moving parts that fail in different ways, and a test that
only covers one of them has repeatedly passed while the product was broken:

| Layer | Fails as | Caught by |
|---|---|---|
| Python sidecar + drivers | wrong values on the wire | pytest |
| C# engine logic | right values, wrong physics or dispatch | engine self-tests |
| The seam between them | both sides correct, nothing connected | end-to-end checks |

The third row is the one that has burned this project twice — Run-mode clicking
was dead while every assertion the headless test could reach passed, and the 3D
engine could not be driven by any real protocol driver at all while the drivers
themselves worked fine. So end-to-end coverage is not optional here.

---

## What is covered

### A. Build and static

| # | Check | Why |
|---|---|---|
| A1 | `dotnet build` succeeds with **zero warnings** | a failed build silently leaves the old binary in place and Godot runs it |
| A2 | Sidecar package imports | catches a syntax error before it wastes a 40-second engine run |
| A3 | No `TODO`/`FIXME`/temp probe left in `engine/src` | temporary verification hooks have escaped into commits before |

### B. Python unit and integration

| # | Check |
|---|---|
| B1 | Full pytest suite (41 tests): tag model, bus protocol, Modbus, OPC UA client/server, Siemens, sorting scene |

### C. Engine self-tests (headless)

| # | Check | Command |
|---|---|---|
| C1 | Panel buttons: one-scan pulse, maintained E-stop, cap picking incl. rotated | `--self-test=buttons` |
| C2 | Rename and I/O export | `--self-test=io` |
| C3 | Scene save/load round-trip, every part type, undo/redo | `--self-test=scene` |

### D. Engine self-tests (need a display)

| # | Check | Command |
|---|---|---|
| D1 | Whole click path from a synthesized mouse event to a tag | `--self-test=click` |

### E. Determinism and the regression contract

| # | Check | Why |
|---|---|---|
| E1 | `drive_engine.py` → `tall=5 short=5` | the contract the whole project is pinned to |
| E2 | Two `--deterministic` runs produce **identical** counts | reproducibility is the point of that mode; a drift here invalidates E1 |
| E3 | `--time-scale=4` reaches a higher tick count than `1.0` in the same wall-clock | the rate control actually drives the accumulator |

### F. Engine ↔ sidecar seam

| # | Check | Why |
|---|---|---|
| F1 | `connect --driver mock` attaches to a running engine and reports the real scene and tag count | the path that did not exist until recently |
| F2 | `connect --driver modbus-tcp` starts and prints an address map | server drivers need no PLC |
| F3 | `connect --driver opcua-server` starts and reports an endpoint | ditto |
| F4 | `connect` against **no** engine fails fast with a useful message, not a hang | the most likely first-run mistake |
| F5 | Physics scene driven over the bus actually sorts (`counter.tall` > 0) | proves parts, sensors, pusher and removers work together under Jolt |

### G. Robustness

| # | Check | Why |
|---|---|---|
| G1 | Corrupt scene JSON is refused without crashing | a hand-edited scene file is expected |
| G2 | Unknown CLI arg does not prevent startup | |
| G3 | Loading a scene saved by a *newer* build (unknown property keys) still opens | forward compatibility is claimed in `SceneData`'s docs |

### H. With a PLC — manual, verified 2026-08-12

Not in the runner: it needs S7-PLCSIM Advanced, which CI does not have. Run by
hand, and **passing**:

```bash
# 1. PLCSIM Advanced: instance started, examples/tia/ downloaded to it
# 2. Terminal 1
godot --path engine/
# 3. Terminal 2
cd sidecar
python -m factoryforge_sidecar connect --driver plcsim-advanced \
    -o instance <your instance name> --mapping ../examples/plcsim_mapping.json
```

**Result:** the rigid-body 3D scene driven by a real S7-1500 CPU. Belt running,
emitter pulsing, sensors reporting back, pusher diverting tall cartons down the
chute, `counter.tall` and `counter.short` both climbing. The first time the 3D
engine has ever been driven by a real protocol driver rather than a script.

The instance name is whatever the PLCSIM control panel shows — not necessarily
the CPU name in TIA. List them with `SimulationRuntimeManager.RegisteredInstanceInfo`.

Still unverified with hardware: `opcua-client` **against the 3D engine**
(previously verified against the Python harness scene), and `s7-snap7`.

### I. Not automated

Honest list of what this plan does **not** prove:

- **OPC UA and snap7 against a PLC.** Only the native PLCSIM Advanced path has
  been run end to end against the 3D engine. Reaching a PLCSIM CPU over OPC UA
  needs the instance on a **PLCSIM Virtual Ethernet Adapter**; in the default
  *Softbus* (local) mode the virtual CPU has no IP at all, so the endpoint
  configured in TIA is not listening no matter what the project says.
- **Packaging.** No binary has ever been exported (`docs/PACKAGING.md`).
- **Visual correctness.** Screenshots are rendered and read by hand; nothing
  compares them automatically.
- **Long-run stability.** Nothing runs for hours.

---

## Results

**2026-08-12 — 20 passed, 0 failed, 227s** (`python tools/test_plan.py --gui`).

Notable: **F5 sorts 5 tall / 5 short on the rigid-body scene**, matching the
deterministic contract. That is not guaranteed and is not asserted — Jolt makes
no reproducibility promise, which is the whole reason `--deterministic` exists —
but it means emitter, sensors, pusher, chute and removers agree with the
scripted model on this layout.

### What building the plan found

Three real defects, none of which any existing test could have caught, because
none of them are in code any existing test executes:

1. **A second engine reported "ready" with no tag bus.** The bind failure was
   pushed as an error and then ignored: the scene rendered, the parts simulated,
   and no driver could ever connect. Anyone hitting this would go and debug
   their PLC. The startup line now says `[NO TAG BUS — port in use]` and the
   error names the likely cause. (**G4**)

2. **The engine could not be restarted promptly.** The socket from the previous
   instance is not always released by the time the new one asks, and the retry
   was a single 500 ms attempt — so closing the app and reopening it produced a
   silently unreachable engine. Now retried over ~3 s.

3. **Godot hangs when its stdout is an undrained pipe**, and it fills that
   buffer *before* binding the bus. Measured: with inherited stdout or a file
   the port opens in 0.25 s; with `subprocess.PIPE` and no reader it never opens
   at all. `subprocess.run` is fine because `communicate()` drains concurrently;
   a bare `Popen` is not. This is a trap for anyone scripting the engine.

And two of my own tests were wrong in ways worth recording, since both would
have shipped as false confidence:

- **E2 passed vacuously.** It compared two *undriven* deterministic runs, which
  both sorted nothing, so it asserted `(0,0) == (0,0)`. A determinism check that
  passes when the simulation does no work is not a check. It now drives both
  runs and requires the result to be non-zero as well as equal.
- **F5 tested the wrong thing.** It drove the physics scene with `--driver mock`
  and expected cartons to sort. The mock driver is a passthrough with no control
  logic, so an engine driven by it correctly does nothing — the failure was in
  my test, not the engine.

Every self-test added here was also checked by deliberately reintroducing the
bug it guards. `--self-test=scene` was confirmed to catch both historical
save/load defects: a sensor losing `visual_only` and a remover losing the tag it
counts into.
