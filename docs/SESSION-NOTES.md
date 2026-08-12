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

---

# 2026-08-12 — the panel you can press

Until this session every input the controller could see was one the simulation
computed for it. Sensors fired because a box passed them; limit switches
followed the pusher. There was no way to *start* anything, no stop, and no way
to inject the fault a program is supposed to survive — you could watch a line
run, but not operate one. The `ButtonPanel` had two lamp tags, a
`ButtonPressed` signal nothing emitted, and buttons that were painted on.

## What landed

**Edit / Run mode (`F1`).** A click cannot both pick a part up and press it, so
the two meanings needed separating before buttons could exist at all. Edit mode
is the old behaviour; Run mode routes a left click to the operator controls and
nothing else. Entering Run drops a placement in progress and clears the
selection, and the palette hides itself. The toolbar and palette both listen to
`ModeChanged` rather than keeping their own copy of the mode.

**Four real controls.** Start, Stop and Reset are momentary; the mushroom is a
maintained E-stop that latches when struck and releases when clicked again. All
four are `TagKind.Input` — the operator drives them, the controller reads them,
exactly like a sensor.

**`panel.estop` is normally closed**, so it reads *true while the circuit is
healthy*. That is the real wiring, and it is worth not hiding: a program that
runs happily with that tag false would also run with the wire to the E-stop cut.

## The two things that were genuinely hard

**Momentary means one scan, not one mouse-down.** A click arrives on the frame
clock; tags are written on the physics clock. The panel queues presses and the
dispatch drains the queue, clearing the previous tick's pulse *before* raising
this tick's. That ordering is the whole guarantee: hold the mouse for a second,
or click three times between two ticks, and a program still sees exactly one
clean rising edge.

**Hit-test the caps, not the part.** Picking already existed, but it tests a
part's bounding box — and the panel's box covers the housing, the pedestal and
both lamps. Reusing it would have made the entire station one large Start
button. Run mode asks each panel directly, and the panel tests each cap's own
sphere in its local space.

## The bug worth remembering

The feature was finished, both halves were correct, and clicking a button did
nothing.

`PressControlAt` read `GetViewport().GetMousePosition()` — where the pointer is
*now* — instead of taking the position off the event. For a real click those
agree, so the code looked fine; for the synthesized click that proves it works,
they do not. The headless self-test passed every assertion it could reach the
entire time, because the failure was in the input handler and the headless test
starts below it.

That is why there are now two self-tests rather than one:

- `--self-test=buttons` — headless, no GPU: pulse width, the maintained latch,
  cap picking (including at a rotation that lines up with nothing, since a hit
  test that quietly assumed world axes passes the as-placed case and misses
  every button once the panel is turned).
- `--self-test=click` — needs a display: synthesizes a real
  `InputEventMouseButton` at a cap's projected screen position and asserts the
  tag moved.

Both were checked by deliberately reintroducing each bug and confirming they
fail. A self-test nobody has watched fail is a guess.

## Smaller things

- Adding a ninth toolbar button overflowed the bar at the default window size
  and clipped "Clear Scene" off the right-hand end. Shortcut hints moved from
  the labels into tooltips.
- The default scene now ships an operator station at the head of the line, on
  the side the camera looks from. The first placement put it behind the belt
  facing away, and the one before that sat under the parts palette — a button
  you cannot see is a button you cannot press.

---

# 2026-08-12 (later) — the seam between the engine and a PLC

Walking the path a new user takes — download it, build a line, connect a PLC —
turned up a set of failures that were invisible from inside the project because
every existing test drove the *demo* scene through a bespoke script.

## The one that mattered most

**Nothing could attach a driver to the running 3D engine.** The sidecar's only
run command, `demo`, starts its *own* Python `EngineStub` on the bus port. With
the engine already running it finds 7411 taken; if it bound first it would drive
the Python scene while the 3D one sat still. The only thing that ever drove the
Godot engine was `tools/drive_engine.py`, which uses the mock driver.

So the S7-1500 and Node-RED results in the README are real, but they were the
*harness* scene. The 3D engine had never been driven by a real protocol driver,
and no documented command could do it.

Fixed with a `connect` subcommand: attach to an engine already listening, run
any driver against it. Same driver stack, same client — the drivers were always
independent of which engine was on the other end, and the CLI simply gave no way
to say so.

## Two UIs that configured nothing

`DriverWiringUI` kept its mappings in a `Dictionary` that nothing outside that
one file ever read. `DriverConnectionUI.ApplyConnectionSettings` assigned four
fields, printed a line, and hid the dialog. Both looked like they worked, and
both were the first thing a person would reach for.

They now write and consume `io_mapping.json`, and Apply & Connect really starts
the sidecar (`OS.CreateProcess`), reporting the pid or saying plainly that
python was not found, with the command on the clipboard either way.

The wiring panel's address column was also a hardcoded list of nine Siemens
addresses describing the sorting demo — so a scene you built yourself had
nothing to wire to. It is generated from the live tag table now.

## The rest

- **Parts could not be named.** The id a PLC program is written against was
  `pushermechanism_2`, and unstable: delete and re-place a part and the number
  moves, silently breaking a mapping that points at the old one.
- **No way to get the I/O list out.** Twenty auto-generated tag ids, read off
  the inspector and retyped into TIA Portal. A typo there produces an input that
  is false forever, which looks exactly like a sensor that never triggers. F4
  now exports `io_mapping.json` and `io_tags.csv`.
- **Editor changes were never announced.** `SendDescribe` existed and bumped the
  epoch; nothing called it. A driver connected while you built kept working from
  the tag list it got at connect time.
- **Save/Load was one hardcoded slot** — one scene, silently overwritten, in a
  directory nobody could find.
- **Every scene reported as `sorting-by-height`**, a `const` in `Main`.
- **Packaging was marked done and was not.** No `export_presets.cfg` existed and
  no binary had ever been built. The presets are written now, but still unbuilt:
  the export templates are not installed here. `docs/PACKAGING.md` records that
  honestly, including the open question of how to ship the Python sidecar.

## Traps this session added to AGENTS.md

1. **A crashing self-test can exit 0.** An exception in `_PhysicsProcess` is
   logged by Godot and execution continues, so the test never reports, the run
   ends via `--duration`, and the exit code says success. Found by mutation
   testing, not by reasoning.
2. **`Quit()` is deferred to end of frame**, so a self-test body runs again next
   tick against state it already mutated, burying the real failure.
3. **A failed `dotnet build` leaves the old binary**, and Godot runs it happily.
   A run that produces *no* output where you expected some is more likely stale
   than wrong.
4. **A guard at a higher layer can mask a mutation.** Removing the collision
   check inside `RenameInstance` changed nothing observable, because the editor
   checks the whole prefix first. The lower guard needed testing directly, or
   its all-or-nothing promise was guarded by nothing.

## Verification note

Two probes were written, used, and deleted: one that drives the F5 dialog to
confirm a `cmd` → `python` process really starts, and one that submits the
inspector's Name field the way a user would. The second reported a stale value
that turned out to be the probe's fault — `QueueFree` is deferred, so a
same-frame search finds the old LineEdit. Worth remembering before believing a
probe over the code.
