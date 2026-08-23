# FactoryForge — Loose Ends: Claims Without Code, Controls Without Effect

**Status:** proposed. Nothing in this plan has been implemented.
**Written:** 2026-08-23, against `7dbec65`, on Godot 4.7.2-mono.
**Work items:** LE-01 … LE-12, indexed in [Appendix A](#appendix-a--work-item-index).

This plan came out of reviewing `docs/UX_PLAN.md` against the running app. That
review found one claim in `README.md` with no code behind it, which raised the
obvious question: **how many more are there?** So the whole surface got swept —
every type in the engine, every documented key, every documented CLI flag, every
property slider, every button — and the answer is: **not many, but the ones
there are, are of a kind.**

Every finding below is a case of the app or its docs saying something is there
when nothing is. That is the same failure FF-06, FF-23 and UX-30 were about, and
the same one the top of `UX_PLAN.md` §2.1 opens with. The gap is small enough to
close in a few days, and worth closing precisely because the rest of the
codebase is honest — a single dead slider is more expensive in a project whose
selling point is that its parts really do what they say.

Two of the items are not claims at all but the two remaining part-level gaps the
UX plan left explicitly open (the Weight Conveyor's readout, the emitter's metal
setting). They belong here because they are the same size and touch the same
files.

---

## 0. How this was evaluated

Not by reading. Three spikes were built, run and reverted, and the rest was
mechanically swept.

### Spike 1 — can `LightArray` rebuild at runtime? **Yes, and it proves the bug.**

A temporary `LightArray.Rebuild()` (the same shape `PhotoelectricSensor.Rebuild`
already has) plus a throwaway `--self-test=spikelightarray` against
`light_curtain_sorting.json`:

| Step | `topBeamY` | Beams |
|---|---:|---:|
| As built (`CurtainHeight = 0.48`) | 0.54 | 12 |
| After `CurtainHeight = 0.90`, **field only** — what the slider does today | **0.54** | 12 |
| After `Rebuild()` | **0.96** | 12 |
| 28 frames later, `_Process` reading the rebuilt lists | alive, no freed-node error | 12 |

So the "Curtain Height (m)" slider genuinely moves nothing, and a rebuild fixes
it for the cost of PhotoelectricSensor's own fifteen lines. The one real risk —
`_Process` walking `_beams`/`_beamMeshes` while the children are being freed —
did not materialise once the two lists are cleared inside `Rebuild()`.
**LE-01 is S, not a spike of unknown size.**

### Spike 2 — wire up `FloatingTagBadge3D` and look at it. **Unusable as written.**

A temporary `SceneEditor.SpikeAddBadges()` hung one badge per owned tag over
every placed part, then screenshotted a real scene:

* Default scene: **14 badges over 10 parts.** `light_curtain_sorting`: **15
  badges over 8 parts.**
* At its declared size the text spans the viewport. Every other in-world readout
  in the codebase sets `PixelSize` explicitly — `LevelTank` 0.0016, `LightArray`
  0.0015 — and the badge sets none, taking Godot's default. Badges overlapped
  each other, the machine and the toolbar; the line underneath was unreadable.
* Colour coding works (green for a true bit, amber for forced), billboarding
  works.
* **The docstring's "allows direct in-scene click forcing" does not exist.** The
  class has no `_Input`, no `Area3D` and no collider. It has never had one.

So this item is not "construct the thing that already exists". Either it becomes
a scoped, resized, toggleable feature (**M**), or it goes (**S**). That is a
decision, not an implementation — see LE-06.

### Spike 3 — give the Weight Conveyor the readout it lacks. **Twelve lines.**

A temporary `Label3D` on `WeighingConveyor`, copied from `LevelTank`'s
(`FontSize = 84`, `PixelSize = 0.0015`), updated inside the existing
`RecalculateWeight()`. Screenshotted against `roller_line_weighing.json` with
the demo running: **`22 g` sits above the carton on the scale**, at the same
visual weight as the light curtain's `0 mm` and the tank's `%` — legible, not
intrusive, no layout work needed. **LE-08 is S.**

### The sweep — and what it did *not* find

Worth stating plainly, because it bounds this plan:

| Swept | Method | Result |
|---|---|---|
| Every type in `engine/src` | 90 declarations, each grepped for a reference outside its own file | **1 dead**: `FloatingTagBadge3D`. (Two others flagged are nested records used in their own file.) |
| Every key documented in any doc | `Key.*` handled in `engine/src` | **All present** — F1, F4, F5, Space, Ctrl+R/S/O/Z/Y/D, M, R, Del, Esc, C, WASDQE |
| Every `--flag` mentioned in any doc | against `Main.cs` and each tool's `argparse` | **All real**; every non-engine flag belongs to a Python tool or to Godot itself |
| Every button handler in `engine/src/Editor` | 36 `Pressed +=` wirings | **None empty**, none a stub |
| Every property slider in the inspector | traced each setter to a runtime read or a `Rebuild()` | **One dead**: LightArray's. `ConveyorBelt.SurfaceFriction` even re-applies live to the physics material on set |
| Every part's tag suffixes | `PartTagManager` against README's table | **All match** |

That is a healthy result. The findings below are a short list, not a symptom.

---

## 1. Findings

### 1.1 The property panel ships a slider that does nothing — the app's own rule, broken

`PartPropertyInspectorUI.cs:135`:

```csharp
AddSliderProperty("Curtain Height (m)", curtain.CurtainHeight, 0.1f, 1.0f, 0.02f,
                  val => curtain.CurtainHeight = val);
```

Thirty lines above it, `:104`:

> Every property here must actually reach the simulation. Anything whose value
> is only read when the part is built needs a `Rebuild()` alongside it, or the
> slider moves and nothing happens.

`LightArray` reads `CurtainHeight` at `:70`, `:75`, `:89` and `:116` — post
sizes, beam heights, readout position — **all inside `_Ready`**, and nowhere
else. `_Process` measures from `_beams[i].Position.Y`, which was fixed at build.
There is no `Rebuild()` on the class to call. Measured in Spike 1: dragging the
slider from 0.48 to 0.90 leaves the top beam at 0.54.

`docs/PART_AUTHORING.md:137` states the same rule for anyone adding a part. The
rule is written down twice and broken once, in the shipped library.

This is the worst of the findings because of *where* it is: the property panel
is the thing UX-34 built as the honest, one-click way to operate a part. One
inert control in it undermines the rest.

*Fixed by:* LE-01.

### 1.2 A README feature, a class in the tree, and no path between them

`engine/src/Editor/FloatingTagBadge3D.cs` is a complete, working billboard label
that reads a tag every frame, formats bit/int/float, and tints itself amber when
forced. **Nothing in the repository ever constructs one** — no `new
FloatingTagBadge3D()`, no `.tscn` node, nothing. It is 58 lines of dead code.

Until this review, `README.md` advertised it:

> 🏷️ Live In-Scene Tag Inspection & Floating 3D Badges: Floating 3D billboard
> labels above components with interactive live forcing buttons

Two claims, both untrue: no badge appears in any scene, and even the dead class
has no click handling to force anything with. The bullet has since been
rewritten to describe the Tag Inspector, the property panel and the parts that
really do read out in 3D. The class is still there, and still dead.

Spike 2 measured what wiring it up would actually give: 14–15 badges per scene,
each several times the world size of every other in-world readout, overlapping
each other and the machine. So "just wire it up" is not a fix.

*Fixed by:* LE-06, then LE-07.

### 1.3 Three parts keep their most interesting setting out of reach

The inspector's settings section handles `WeighingConveyor`, `ConveyorBelt`,
`PhotoelectricSensor`, `PusherMechanism`, `LightArray`, `LevelTank` and `Chute`.
It has no branch for `Emitter`, `Remover`, `DigitalDisplay`, `StackLight` or
`ButtonPanel`. For three of those that is right — a stack light has nothing to
tune. For two it is a real gap:

* **`Emitter.MetalEvery`** — every Nth carton is spawned in metal. This is what
  makes the inductive sensor a different part from a photoelectric one, which is
  a headline README feature and the thing `roller_line_weighing.json` is built
  to teach. The template sets `"metal_every": "3"` **in JSON**;
  `PartProperties` saves and loads it; the tag bus never sees it; and there is
  **no way to change it from the UI at all**. `UX_PLAN.md` §5.4 and §5.5 both
  instruct the reader to "set `metal_every` to 1" — an instruction that cannot
  be followed without a text editor.
* **`Remover.CountTag`** — which tag a remover counts into. Saved and loaded as
  `count_tag`, settable only by editing a scene file. Less pressing (a fresh
  remover counts into its own `.count`), but the same shape.

*Fixed by:* LE-03, LE-04.

### 1.4 One part measures something and shows nothing

Three parts draw their measurement on themselves in 3D: `LightArray` (`N mm`),
`LevelTank` (`N.N %` and a liquid column), `DigitalDisplay` (its 7-segment
panel). `WeighingConveyor` computes `MeasuredWeight` every time a carton enters
or leaves the scale — and displays it nowhere. On the roller line you watch a
box sit on a scale that never says what it read; the number exists only in the
Tag Inspector on the far side of the screen, which is the exact indirection
UX-34 and UX-37 spent a phase removing.

This is the last `readout ✗` in `UX_PLAN.md` §5.4 still standing. Spike 3 shows
it costs twelve lines.

*Fixed by:* LE-08.

### 1.5 The F4 panel says drag; there is no drag

`README.md`: *"Visual I/O Driver Wiring Panel (F4): … allowing users to
drag/click PLC addresses (`%I0.0`, `%Q0.0`) directly to factory component
tags."* The panel works by **click to select an address, click to map it to a
tag** (`DriverWiringUI.SelectPlcAddress` → `MapSelectedToTag`). There is no
`_GetDragData`, `_CanDropData` or `_DropData` anywhere in the codebase.

The panel is not broken — the click path works, exports a real `io_mapping.json`
and survives a restart. Only the word "drag" is unearned, and a user who tries
it first will conclude the panel is broken when it is not.

*Fixed by:* LE-09.

### 1.6 Smaller

* **README's part table gives the Box Remover `counter.tall`, `counter.short`.**
  A placed remover registers `{id}.count`, one tag. The row is describing the
  built-in sorting scene, where the remover is pointed at two scene-owned
  counters — true of that scene, misleading as a description of the part.
* **`WeighingConveyor` gets one slider where its base class gets two.** The
  inspector's `if (node is WeighingConveyor)` branch comes before the
  `ConveyorBelt` one and offers Belt Speed only, so a weighing deck silently
  loses the Surface Friction control every other belt has. Nothing is broken;
  it is an ordering accident, not a decision.
* **The tag ids in README's table are the built-in scene's names**
  (`weighconveyor.weight`), not what a placed part gets
  (`weighingconveyor_1.weight`). Fine as illustration, worth one sentence saying
  so, since the whole point of the naming rule is that the part's **Name** is
  the prefix.

*Fixed by:* LE-10.

---

## 2. Plan

### How to read a work item

```
LE-nn — one-line title
  Files:      what gets touched
  Done when:  the observable state that means it is finished
  Verify:     the command or the click that proves it
  Size:       S | M | L
```

**Sizes.** `S` = under half a day. `M` = one to two days. Same scale as
`UX_PLAN.md`; these are relative, not a schedule.

### Sequencing

> **Phase 1 → Phase 4 → Phase 3 → Phase 2.**

Phase 1 is the real bug and its siblings. Phase 4 comes second on purpose: its
self-test is the net under Phase 1, and writing it immediately after means the
fix is proven rather than assumed. Phase 3 is cheap and visible. Phase 2 is last
because it is a decision, not a task, and nothing else waits on it.

Total is a few days. Nothing here blocks `UX_PLAN.md`'s Phase 0 (ship a binary),
and nothing here needs to land before a first release — though LE-01 and LE-08
are the two a new user is most likely to notice.

---

### Phase 1 — Controls that do what they say

**LE-01 — `LightArray.Rebuild()`, and a slider wired to it**
*Files:* `engine/src/Parts/LightArray.cs`,
`engine/src/Editor/PartPropertyInspectorUI.cs:135`.
*Done when:* dragging **Curtain Height** rebuilds the curtain — posts, beams and
readout all move — and the measurement changes with it. Proven by Spike 1: split
`_Ready` into `BuildGeometry()`, add a `Rebuild()` that frees the children,
clears `_beams` and `_beamMeshes`, and rebuilds; call it from the slider the way
`PhotoelectricSensor` and `Chute` already do.
*Verify:* place a Light Array, drag the slider, watch the curtain grow; then the
LE-11 self-test.
*Size:* S.

**LE-02 — Beam count on the panel, or a stated reason not to**
*Files:* `engine/src/Editor/PartPropertyInspectorUI.cs`.
*Done when:* `BeamCount` — the curtain's resolution, and the one setting that
changes what the part can measure — is either a spin box beside Curtain Height,
or explicitly left out with a comment saying why. It is already captured by
`PartProperties` and already rebuild-safe once LE-01 lands.
*Verify:* set beams to 4, watch the curtain coarsen and `.height` quantise.
*Size:* S. *Depends on:* LE-01.

**LE-03 — The Emitter's `metal_every` gets a control**
*Files:* `engine/src/Editor/PartPropertyInspectorUI.cs`.
*Done when:* a selected Box Emitter shows a **Metal every Nth carton** spin box
(0 = never), so the inductive sensor can be demonstrated without editing JSON —
and `UX_PLAN.md` §5.4's "set `metal_every` to 1 then 0 and watch it react to
metal only" becomes an instruction a reader can follow.
*Verify:* place an emitter and an inductive sensor, set 1, watch `.detect`
follow every carton; set 0, watch it go quiet.
*Size:* S.

**LE-04 — The Box Remover's `count_tag` gets a control, or an honest note**
*Files:* `engine/src/Editor/PartPropertyInspectorUI.cs`.
*Done when:* either the remover's counted tag is settable from the panel (a
dropdown of the scene's `int` input tags is the honest shape — a free-text field
that accepts a nonexistent tag id would be a new silent no-op), or the panel
says plainly that it counts into its own `.count` and a different target needs a
scene file.
*Verify:* place two removers, point one at the other's counter, watch both
climb.
*Size:* S.

**LE-05 — Give the weighing deck the friction slider every other belt has**
*Files:* `engine/src/Editor/PartPropertyInspectorUI.cs:107`.
*Done when:* `WeighingConveyor`'s branch offers Surface Friction as well as Belt
Speed, or falls through to the `ConveyorBelt` branch for both. The property is
already live-applied on set (`ConveyorBelt.SurfaceFriction`), so this is the
missing row and nothing else.
*Verify:* select a weighing conveyor; both sliders are there and both bite.
*Size:* S.

---

### Phase 2 — Decide the badge

**LE-06 — Decide: scope and resize it, or delete it**
*Files:* `docs/LOOSE_ENDS_PLAN.md` (this section), then whichever branch wins.
*Done when:* the decision is recorded with its reason. The two branches, both
measured in Spike 2:

|  | Keep it | Delete it |
|---|---|---|
| Work | **M** — scope to the selected part, set `PixelSize` like every other readout, add a toggle, and either implement the click-to-force the docstring promises or drop that sentence | **S** — remove one file, confirm no doc mentions it |
| Argument for | A value floating over the machine is the one thing neither the Tag Inspector nor the property panel gives you: you can watch the line and the numbers at once | UX-34, UX-35 and UX-37 already put every tag one click from the part. A fourth route is a fourth thing to keep honest |
| Argument against | 15 tags per scene is a lot of floating text even scoped and resized; it needs a real design pass, not a wiring pass | Deleting removes the only in-world view of a tag that has no 3D readout of its own |

*Verify:* the decision is in §3 with a reason a stranger can weigh.
*Size:* S — it is a decision.

**LE-07 — Implement the chosen branch**
*Files:* `engine/src/Editor/FloatingTagBadge3D.cs`,
`engine/src/Editor/SceneEditor.cs`, `engine/src/Editor/SceneToolbarUI.cs`
(if kept); just the first (if deleted).
*Done when:* if kept — badges appear only for the selected part, sized like the
tank's and curtain's readouts, behind a toolbar toggle that is off by default,
and the docstring matches what the class does. If deleted — the file is gone,
`--self-test=…` and the docs are unaffected, and LE-12's check would have caught
it.
*Verify:* if kept, a screenshot of a full scene with the toggle on and off. If
deleted, a clean build and `test_plan.py --only A,C`.
*Size:* M if kept, S if deleted. *Depends on:* LE-06.

---

### Phase 3 — Readouts and small truths

**LE-08 — The Weight Conveyor reads out its own weight**
*Files:* `engine/src/Parts/WeighingConveyor.cs`.
*Done when:* the scale shows its live weight above the deck, matching
`LevelTank`'s and `LightArray`'s treatment (`FontSize = 84`,
`PixelSize = 0.0015`, billboarded, updated where `MeasuredWeight` is already
recalculated). Spike 3 built and screenshotted exactly this.
*Verify:* run `roller_line_weighing.json` with the demo; the number above the
scale rises as a carton lands and returns to zero after it leaves.
*Size:* S.

**LE-09 — F4: drop the word "drag", or implement it**
*Files:* `README.md`; `engine/src/Editor/DriverWiringUI.cs` if drag is chosen.
*Done when:* the README describes the click-select-then-click-map flow the panel
actually has — or Godot's `_GetDragData`/`_CanDropData`/`_DropData` are
implemented on the address and tag lists and the word is earned.
*Verify:* follow the README sentence literally against the running panel.
*Size:* S to reword, M to implement.

**LE-10 — Correct the part table's tag column**
*Files:* `README.md`.
*Done when:* the Box Remover row shows `{name}.count`, not the sorting scene's
two counters; and one sentence above the table says the ids shown are the
built-in scene's names, since a part you place is prefixed by the **Name** you
give it.
*Verify:* place one of each part and compare the Tag Inspector against the
table.
*Size:* S.

---

### Phase 4 — Keep it from coming back

**LE-11 — `--self-test=partsettings`: every slider reaches the simulation**
*Files:* new `engine/src/Sim/PartSettingsSelfTest.cs`, `engine/src/Main.cs`,
`tools/test_plan.py`, `docs/TEST_PLAN.md`.
*Done when:* for each part type the inspector offers settings for, the test
drives the real control (found by its label, the way `--self-test=proppanel`
already finds its rows) and asserts a **named observable** changed — belt:
`ConstantLinearVelocity`; sensor: raycast reach; curtain: top beam Y; tank:
`Level` rate; chute: incline. A per-part expectation table, in the same spirit
as `TemplateSelfTest`'s `MustContain` map, because there is no generic way to
ask "did this setting matter".
*Verify:* revert LE-01 and watch the curtain row fail; restore it and watch it
pass. This is the check that would have caught §1.1 the day it shipped.
*Size:* M. *Depends on:* LE-01.

**LE-12 — A dead-type check beside A3**
*Files:* `tools/test_plan.py`, `docs/TEST_PLAN.md`.
*Done when:* Section A gains a check that no type declared in `engine/src` is
referenced nowhere outside its own file — the sweep that found
`FloatingTagBadge3D` in §0, run every time instead of once. Nested helper types
used only within their declaring file are the expected false positives and need
an explicit allowance.
*Verify:* add a throwaway unreferenced class; the check fails naming it; remove
it; it passes. Today the sweep reports exactly one hit, so the check can go in
green the moment LE-07 resolves it.
*Size:* S.

---

## 3. Decisions

### Settled

* **The sweep bounds this plan.** 90 types, every documented key, every
  documented flag, all 36 button handlers and every property slider were
  checked. What is written above is the whole of what was found — this is not a
  first instalment.
* **`LightArray` rebuilds rather than reading its settings live.** The
  alternative (make `_Process` derive beam positions from `CurtainHeight` each
  frame) would work for the beams and not for the posts or the readout, and
  would leave this part different from every other. Rebuild is what
  `PhotoelectricSensor` and `Chute` already do.
* **A `count_tag` control must not be free text** (LE-04). A field that accepts
  a tag id nothing owns would be a new silent no-op, in a plan about removing
  them.

### Still open

1. **LE-06 — the badge.** Keep it scoped and resized (M), or delete it (S).
   Spike 2 measured the cost of keeping; nobody has argued the value. Decide
   before LE-07, not before anything else.
2. **LE-09 — drag in F4.** Wording is S and honest; real drag is M and better.
   Worth deciding only once someone has watched a person use the panel.

---

## 4. Explicitly not in scope

* **New parts, new drivers, new protocols.** Same boundary as `UX_PLAN.md` §7.
* **Redesigning the property panel.** LE-02 … LE-05 add rows to the structure
  UX-34 built; they do not rethink it.
* **Anything in `UX_PLAN.md` Phase 0** (ship a binary). Independent, and still
  the more valuable work.
* **The `.godot/` build artifacts and `scratch/`** that carry the author's own
  paths. Both are git-ignored; UX-24's scope was shipped files.

---

## Appendix A — Work item index

`S` under half a day · `M` one to two days.

| # | Item | Phase | Size | Depends on | Status |
|---|---|---|---|---|---|
| LE-01 | `LightArray.Rebuild()`, and a slider wired to it | 1 | S | — | open |
| LE-02 | Beam count on the panel, or a stated reason not to | 1 | S | LE-01 | open |
| LE-03 | The Emitter's `metal_every` gets a control | 1 | S | — | open |
| LE-04 | The Box Remover's `count_tag` gets a control, or a note | 1 | S | — | open |
| LE-05 | Friction slider for the weighing deck | 1 | S | — | open |
| LE-06 | Decide the badge: scope and resize, or delete | 2 | S | — | open |
| LE-07 | Implement the chosen branch | 2 | M / S | LE-06 | open |
| LE-08 | The Weight Conveyor reads out its own weight | 3 | S | — | open |
| LE-09 | F4: drop "drag", or implement it | 3 | S / M | — | open |
| LE-10 | Correct the part table's tag column | 3 | S | — | open |
| LE-11 | `--self-test=partsettings` | 4 | M | LE-01 | open |
| LE-12 | A dead-type check beside A3 | 4 | S | — | open |

**Totals:** 12 items — 9 S, 1 M, 2 either-or. By phase: 1→5, 2→2, 3→3, 4→2.
**The two a user notices first:** LE-01 (a slider that does nothing) and LE-08
(a scale that shows nothing).
