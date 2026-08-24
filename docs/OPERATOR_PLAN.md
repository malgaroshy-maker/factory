# The operator plan — driving a scene by hand

*Status: **done** — all ten items. Written and finished 2026-08-24, after a
session testing the five shipped scenes from the running app.*

Three things came out of driving the shipped scenes the way a user actually
does — open one, press F1, and click things:

1. **Four of the five scenes ignore the control panel completely.** Every
   template places a `ButtonPanel`, every panel registers `start`, `stop`,
   `reset` and `estop` tags, and every one of those tags is read by exactly one
   scene. Press Start on the roller line and nothing happens; strike the E-stop
   on the tank and it keeps filling. The buttons are wired to the tag bus and
   the tag bus is wired to nobody.

2. **Nothing has a setpoint you can turn.** The one number each scene is really
   about — the level to hold, the height that counts as tall, the weight that
   counts as a reject — is a constant inside a Python file. You cannot change
   what the line does without editing code, which is the opposite of what an
   operator station is for.

3. **A part you have placed cannot obviously be moved.** `M` does it. Nothing
   on screen says so, no part responds to being dragged, and the first thing
   everybody tries is dragging.

These are one problem in three places: the scenes look interactive and are not.

---

## 0. Spikes — the parts most likely not to work

Done before anything was designed around them.

### 0.1 Can a drag reach the editor in Run mode?

`_UnhandledInput` already receives `InputEventMouseMotion` in Run mode (it
drives the hover highlight, UX-39). A press-move-release therefore arrives as
three events with nothing in between swallowing them. **A dial can be dragged.**

### 0.2 Does forcing a setpoint tag fight the panel writing it?

`estop` already has this exact shape: the panel writes it every tick with
`TrySet`, and `try_scene.py` overrides it with `force`. `TrySet` yields to a
forced tag, so the force wins and the panel's own write is a no-op until the
force clears. A setpoint can be dialled by hand *or* driven from a test with no
special case — and `IsForced` lets the knob turn itself to match a forced
value, so a headless test moves the same dial a human would.

### 0.3 Does drag-to-move collide with click-to-select?

Only if a drag is treated as a click. Threshold it: a press that never moves
more than a few pixels is a click, anything past that is a move. This is what
every editor does and it needs no modifier key.

---

## 1. What landed

| | | |
|---|---|---|
| **OP-01** | A setpoint pot on every control panel — a knob, a pointer, end stops and a scale plate reading in the scene's own units | done |
| **OP-02** | The pot is dragged in Run mode, publishes itself every scan, and turns to match a forced tag | done |
| **OP-03** | `sorting-by-height` — operator-driven, pot = diverter timing | done |
| **OP-04** | `start-stop-station` — pot = batch size; the line stops itself at target | done |
| **OP-05** | `tank-level-control` — pot = the level to hold; Stop shuts both valves | done |
| **OP-06** | `light-curtain-sorting` — pot = the tall/short threshold, read per carton | done |
| **OP-07** | `roller-line-weighing` — pot = the reject limit in grams | done |
| **OP-08** | Drag a placed part to move it, as one undoable step | done |
| **OP-09** | Selecting a part says what you can do with it | done |
| **OP-10** | Self-tests, fixtures and docs follow the new tags | done |

## 2. Decisions worth writing down

**Every panel gets a dial, not just the scenes that need one.** A tag set that
varies with a part's *properties* would have to be rebuilt whenever a property
changed, and both the Python mirror and `scene_tag_sets.json` assume a part
type has one tag set. A real operator station usually does carry a setpoint
pot; a scene that ignores `panel.setpoint` costs one unread Input tag.

**The dial is a percent, and each scene maps it to its own units.** 0–100 with
the engineering range chosen scene-side is how a real 4–20 mA setpoint pot
works, and it keeps one tag type across five very different meanings. Each
scene prints its mapping when it starts, so the number on the knob is never a
mystery.

**Every scene starts stopped.** This is the change most likely to read as a
regression — press Try and nothing moves — so every driver presses its own
Start immediately and says so. The point is that Stop and the E-stop now
*work*, not that the user has to press Start to see anything.

---

## 3. What each scene actually does now

Every one of these runs from its own panel. `python tools/try_scene.py --scene
<id>` presses the buttons itself, so the same sequence a person can perform by
hand is also the regression test — and both go through the tag bus, the seam a
real PLC uses.

**All five share one operator contract**, checked identically
(`exercise_interlocks` in `try_scene.py`, `OperatorStation` in the engine):
the line starts stopped, Start runs it, the mushroom stops it **within 200 ms**
(measured, not asserted loosely), Start while tripped does nothing, releasing
the mushroom alone does nothing, Reset clears the fault without restarting, and
Stop leaves the counters where they were. Five scenes agreeing on that is worth
more than five dialects of it, and it is why the shared helper exists.

| Scene | The knob | What the run proves about it |
|---|---|---|
| **sorting-by-height** | diverter delay, 0.30–1.80 s | measured 0.91 s at a 0.90 s setting, 1.80 s at 1.80 — the timing *is* the knob |
| **start-stop-station** | batch size, 0–50 pcs | set to 4, the line made exactly 4 and stopped itself; the tower went yellow, not green |
| **tank-level-control** | level, 0–100 % | 9.1 s to reach 70 %, 20.1 s to reach 20 % with the same gain — Torricelli, on one knob |
| **light-curtain-sorting** | tall threshold, 0–0.50 m | tall=4 short=5 at 0.15 m; at 0.50 m the curtain still measured and diverted nothing |
| **roller-line-weighing** | reject limit, 0–6000 g | at 3000 g, 3 of 9 rejected — **the same 3 the inductive sensor flagged**; at 100 g, all of them |

That last row is the one worth pausing on. Mass and material are independent
measurements of the same cartons, so making the run assert that the
checkweigher and the inductive sensor **agree** catches a wiring or threshold
mistake in either instrument. "Metal was seen at least once" passes for a
sensor wired to fire on everything, which is the exact confusion that scene
exists to clear up.

### The one thing that had to give

`try_scene.py` no longer runs `sorting-by-height` under `--deterministic`. It
could not: that flag selects a fixed 10-tag hybrid scene which registers no
editor parts at all and therefore **has no control panel** — none of
`panel.start`, `panel.stop` or `panel.estop` exists on it, so there is nothing
to drive. Its exact `tall=5 short=5` contract still lives in
`tools/drive_engine.py`, which is the tool written for it. What `try_scene.py`
runs instead is the line as a user actually opens it, panel included, with
band-based assertions and conservation — which is the check that catches a
diverter dropping cartons anyway.

## 4. Moving a part

`M` moved a selected part and nothing on screen said so, which in practice
meant a part placed in the wrong cell got deleted and placed again. Now:

* **Press, drag, release.** Past a 6-pixel threshold the part follows the
  cursor; under it, the press is still a plain click that only selects. The
  drop goes through the same grid snap and build-volume clamp placement uses —
  a part that snapped differently depending on how it got somewhere would have
  a saved position that depended on how you moved it, so both paths now call
  one `WorkPlanePoint`.
* **One drag, one `Ctrl+Z`** — not one per motion event. A drag that moved
  nothing pushes nothing, so `Ctrl+Z` after a click still undoes whatever came
  before the click.
* **The panel says so.** Selecting a part shows
  `Drag to move · R rotate · Ctrl+D duplicate · Del delete` in its property
  panel, and announces the same thing once in the hint bar. Every one of those
  worked before this; none was written down anywhere in the app.

`M` still works. It was never the problem — being the *only* way was.

## 5. Verification

* `tools/test_plan.py --only A,B,C` — 31 passed, 0 failed (24 headless engine
  self-tests, 73 pytest cases).
* Three new self-tests: `--self-test=setpoint` (**C23**) and
  `--self-test=drag` (**C24**) headless, both wired into `check_release.py` so
  a packaged binary is gated on them too — 23 self-tests against a release now,
  up from 21 — plus `--self-test=dragpath` (**D2**), which needs a display.

  D2 exists because C24 enters at the ray seam and so skips the two things that
  can kill dragging outright while every headless assertion still passes: the
  pixel threshold, and *which* part a press selects. The second turned out to
  be a real defect. Edit mode's click asked the viewport where the cursor was
  instead of asking the event where the click happened — the same bug the Run
  mode dispatch had already fixed once, still sitting in the Edit path. It is
  invisible to a person, because for a real click the two agree; it is fatal to
  a drag, because a drag has to grab the part under the press. Breaking the fix
  again made D2 fail four assertions, starting with `the press selected 'panel'
  (got '')`.
* `try_scene.py` run against all five scenes; every one PASS.
* **Deliberate break, restored**: removing the drag's `MoveCommand` push made
  C24 fail on exactly the two assertions that describe it
  (`one Ctrl+Z puts the part back`, and the no-op-drag check) and pass again on
  restore. Earlier, omitting `setpoint` from the tag-id cache made C23 fail six
  assertions covering both directions of the tag.
* Two screenshots of the panel. The first was wrong and worth recording: the
  scale plate was sized for the panel's full width and landed across the
  mushroom, and the pot's end stops were positioned at `centre.Z - 0.004`,
  which is *inside* the 0.12 m-deep housing box — they never rendered at all.
  Both fixed and re-shot.
