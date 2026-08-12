# Session notes

New agents should read [`../AGENTS.md`](../AGENTS.md) first — it has the paths,
commands, and the gotcha list. This file is the narrative of what happened and
why, kept because the *reasoning* behind several decisions is not obvious from
the code.

## Status

All milestones M0–M6 complete; v1 shipped. **41 tests passing**, engine builds
clean, verified end to end against a real S7-1500.

| Milestone | State |
|---|---|
| M0 — tag bus | ✅ complete |
| M1 — OPC UA | ✅ complete |
| M1.5 — MQTT | optional, not started |
| M2 — Godot engine | ✅ complete |
| M3 — physics | ✅ complete |
| M4 — scene editor | ✅ complete |
| M5 — Siemens breadth | ✅ complete |
| M6 — v1 release | ✅ complete |

Post-v1 work is tracked in [`ROADMAP.md`](ROADMAP.md); the narrative below is
kept per session because the *reasoning* is not obvious from the code.

---

# 2026-08-07 — first sessions (M0–M6)

## What was proven

1. **A TIA Portal SCL program drives the simulation over OPC UA.**
   Real S7-1500 (CPU 1511-1 PN) via PLCSIM Advanced at `192.168.1.20`.
2. **Node-RED can replace the PLC entirely** — `examples/nodered/`. It runs the
   belt, generates the emitter square wave, and fires the pusher on the high
   sensor. **9 tall / 9 short, a perfect split** — better than the S7's 87%,
   because our asyncua server honours a 50 ms subscription while the S7 forces
   1000 ms.
3. **The tag bus abstraction holds across engines.** The *unchanged* Python
   sidecar and driver stack drive the Godot C# engine:
   `python tools/drive_engine.py` → `tall=5 short=5`. This was the whole
   architectural bet and it paid off.
4. **The scene renders.** 3D geometry driven purely by tag values — see the
   screenshot in the README.

## The two hard bugs, and how they were found

### The S7 forces a 1000 ms publishing interval

**Symptom:** boxes emitted and transported, but the pusher never fired —
`tall=0` after 90 s.

**Found by:** instrumenting the driver (`tools/drv_trace.py`) rather than
opening a second OPC UA session, which destabilised the server. The trace showed
writes into the PLC completing in 1 ms and the return path producing nothing.
A subscription test then surfaced the actual cause in a log line:

```
CreateSubscriptionResult(..., RevisedPublishingInterval=1000.0, ...)
```

**Fixed by:** making `opcua_client.py` poll instead of subscribe. One batched
`read_values` call per interval covers every tag, sustaining ~1100 reads/s — 20x
better than the subscription the server actually grants. **No PLC change was
needed.**

### One-scan SCL pulses do not survive

`Sorting.scl` went through three versions, and **both earlier ones passed
against `examples/fake_plc.py` while failing on the real CPU**:

- **v0.1** widened a one-scan pulse to 200 ms with a `TP` timer — far below the
  1000 ms publishing interval, so it could never have worked.
- **v0.2** used `#tEmit(IN := NOT #tEmit.Q, ...)`, making `Q` high for a single
  scan. Diagnosis by reading the instance DB over OPC UA: `tEmit.ET` cycling
  correctly, the assignment line demonstrably running, but `EmitFlag` never
  toggling — so the statement between them never executed.
- **v0.3** holds `IN := TRUE` so `Q` latches until explicitly reset. Works.

**Lesson recorded in AGENTS.md:** the Python model is a logic check, not an
authority. I trusted it over the user's PLC and wrongly told them their download
had not landed. The PLC is the authority.

## Known imperfection

~12% of tall boxes slip past the pusher on the real S7 (`tall=14` vs
`short=18`; `short` counts everything reaching the remover, including
undiverted tall boxes). Timing margin, not a logic error: the catch window is
0.6 s wide and round-trip jitter is ~100 ms.

Fixes, cheapest first:

1. `PUSH_HOLD` `T#500MS` → `T#1S500MS` in `Sorting.scl`. One download.
2. `poll_interval` → 0.02. Removes ~60 ms.
3. Halve `BELT_SPEED` in `harness/scene.py` **and** `SortingScene.cs`, then
   recompute `PUSH_DELAY`. Makes every timing tolerant, and is probably the
   better teaching scenario — sub-second precision is a poor fit for industrial
   transport.

Recommended: (1) now, (3) when M3 revisits the geometry anyway.

## Design decisions worth not re-litigating

- **Polling beats subscribing on Siemens hardware.** See above. `mode="subscribe"`
  remains available for servers that honour a fast interval.
- **Hand-written Modbus server.** pymodbus 3.14 deprecated its datastore
  (removal in v4) and the replacement packs coils into 16-bit registers. Eight
  function codes was less code than the adapter, with no moving dependency.
  pymodbus stays as the *test master*, which is its stable half.
- **`SceneView` never writes tags.** It is strictly a reader, which is what lets
  `Main` skip building it entirely under `--headless` so CI needs no GPU.
- **Screenshot flag.** `-- --screenshot=<path> --screenshot-at=<s>` renders a
  frame to PNG. Added so the visual result could be *looked at* rather than
  assumed — it immediately caught bad framing and black-void shadows. Worth
  keeping as the basis for visual regression checks.
- **The C# and Python tag models must stay in step.** `engine/src/TagBus/Tag.cs`
  mirrors `sidecar/factoryforge_sidecar/tags.py`, including the deliberate
  refusal to accept `bool` as an `int`.

## Next

See "Next steps" in [`../AGENTS.md`](../AGENTS.md).


---

# 2026-08-12 — parts overhaul, physics by default, analog I/O

## What was wrong, and how it was found

**Every default part was inert.** They were registered with an instance id equal
to a whole tag name (`"pusher.extend"`), but the dispatch appends the suffix
itself, so it looked up `pusher.extend.extend` and matched nothing. The pusher
never animated, the belt never scrolled, the sensor beams never lit. Only the
stack light moved, through a hardcoded fallback that masked the bug for everyone
else. Found by rendering a burst of frames across a full extend/retract cycle
and seeing the plate in an identical position in both.

**Lesson:** a part that draws correctly tells you nothing about whether it is
wired. Render motion, not stills.

**The chute could never work.** It had no physics material, so it inherited
Godot's default µ = 1.0 on a 12° ramp; a box needs µ < tan(12°) = 0.21 to slide.
Once that was fixed the *next* bug became visible: the diverter plate was
shorter than the carton, so it pushed below the centre of mass and toppled every
box onto the ramp instead of sliding it. Sizing the plate to the tallest carton
and aligning it with the centre of mass took diverted-and-counted from 1 to 5.

**Two authors on one value.** Making the parts live exposed the fact that a
raycast sensor and the logical scene both wanted to write `sensor_low.detect` —
and the raycast would have won with a permanent `false`, since the deterministic
scene's boxes have no colliders. Hence `VisualOnly`: a part that mirrors a
simulated machine animates from the tag and never writes it back.

## Decisions worth not re-litigating

- **The rigid-body scene is the default; `--deterministic` is the flag.** The
  parts were always real colliders, so the two scenes were never separate
  implementations — `PhysicsScene` was a duplicate and is gone. Reproducibility
  is not lost, only made explicit: `drive_engine.py` and CI pass the flag and
  still get `tall=5 short=5`, and `harness/scene.py` keeps its C# counterpart.
- **Simulation controls go through `Engine.TimeScale`**, so one switch covers
  the accumulator, Jolt and every animation. The tag bus is deliberately exempt:
  it polls from `_Process`, which Godot still calls at time scale 0, so a paused
  scene keeps its PLC session. `--duration` moved to wall-clock for the same
  reason — a paused run whose clock stopped would never terminate.
- **Every part's origin sits on the work plane** and offsets its own geometry.
  That is what lets the editor snap X/Z to the grid, pin Y, and have any part
  land correctly.

## Things that bit, and would bite again

1. **A test harness that outlives the engine reports stale values.** The tank's
   PI loop "stalled" and then "passed" on successive runs; both verdicts were
   the probe reading its last cached value after the engine had quit. Always
   give the engine a longer duration than the probe, and corroborate against the
   engine's own trace.
2. **Clamping to a boundary then testing `< boundary` lets everything through.**
   A box held at the pusher's face failed `position < face` on the very next
   tick and slipped past. Compare against the obstacle's centre, not its face.
3. **`new Gradient()` ships with default black→white points**, and `AddPoint`
   keeps them. That turned a "subtle tread line" into broad white ramps sweeping
   down the belt.
4. **Anything a part reads in `_Ready` must be in `PartProperties`**, or saving
   and reloading resets it. This cost the removers their count tags and the
   sensors their `VisualOnly` flag before it was caught by a round-trip test.
