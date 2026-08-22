# FactoryForge — First-Run, Manual Operation & Scene-Exercise Plan

**Status:** in progress. Done: Phase 1 (UX-10…UX-15), Phase 2's UX-16…UX-20
(the C# side; UX-21/22, `tools/try_scene.py`, not started), Phase 3
(UX-23…UX-29), Phase 4's UX-30/UX-32 (UX-31/33 blocked on UX-21), and Phase
5's UX-34/UX-35/UX-36. Everything else is still proposal.
**Written:** 2026-08-22, against `9ac37d2`.
**Work items:** UX-01 … UX-46, indexed in [Appendix A](#appendix-a--work-item-index).

A plan for three things that turn out to be one problem:

1. **Ease of use for someone who just installed this.** Not "is the feature
   there" — it mostly is — but "does the first twenty minutes work without the
   author sitting next to you".
2. **Being able to operate any component by hand** — click a conveyor and turn
   it on, stroke a pusher, open a valve — so you can see what a part does before
   you write a line of PLC code.
3. **A runnable exercise for every shipped scene**, so you can watch each one
   work, and check your own build, before going anywhere near a PLC.

They are one problem because the app's answer to "show me it works" is a single
scene driven by a single button, while the start screen offers five scenes, two
modes and fifteen parts.

Ahead of all of it sits **Phase 0: ship a binary**, so that "installed the
software" stops meaning "installed a game engine and a compiler first".

---

## 0. How this was evaluated

Not by reading the docs and agreeing with them. Every claim below was checked
against a running build:

* Launched all five shipped scenes and read the `describe` message off the tag
  bus directly, to get the true tag id, type and direction for each.
* Ran every scene headless *and* windowed, and compared the two.
* Traced Edit/Run mode, the Tag Inspector and the part property panel through
  the source to establish exactly what a click, a Force and a selection reach.
* Confirmed parts act on forced values (`SceneEditor.cs:1220` onward reads
  `Tags.TryGetVisible`), which is what makes §5 a specification rather than a
  guess.
* **Ran the Phase 1 spike** (below) and reverted it.
* Ran `python tools/test_plan.py --only A,B,C,E` — 14 passed.

### The spike result — Phase 1 is much smaller than it looked

The plan's largest unknown was whether headless `--scene=` loading needs
renderer surgery. It does not.

`BuildHeadlessPhysicsParts` (`Main.cs:382`) **already** constructs a working
`SceneEditor` headless, with no `VoxelGrid`, no inspectors and no cameras. A
six-line change to call `editor.LoadTemplate(_scenePath)` there instead of
`RegisterDefaultSceneParts` was enough. Measured, with the patch applied and
then reverted:

| Template | Headless scene name | Tags | Matches windowed? |
|---|---|---:|---|
| `start_stop_station` | `start-stop-station` | 14 | yes |
| `tank_level_control` | `tank-level-control` | 13 | yes |
| `light_curtain_sorting` | `light-curtain-sorting` | 15 | yes |
| `roller_line_weighing` | `roller-line-weighing` | 13 | yes |

No errors, no exceptions, and every tag set identical to the windowed run.

Then the harder question — does a template actually *simulate* headless? Driving
`start-stop-station` over the bus for 22 seconds (force `belt.rotate`, pulse
`emitter.emit`) gave **`counter.count = 6`**. Boxes emitted, rode the belt,
tripped the photoelectric sensor and were counted, with no renderer present.

**Consequences.** UX-10 drops from "spike of unknown size" to **S**. Phase 2's
exercises can run in Linux CI rather than needing a display. The risk note that
used to sit on Phase 1 is gone, and open decision 1 in §6 is closed.

Three findings below (§2.1, §2.3, §2.7) contradict what the code's own comments
or its UI claim, which is why each carries its evidence.

---

## 1. The five ready-to-use scenes

Verified by connecting to a live engine and reading `describe`. This table does
not exist anywhere in the repo today, which is itself part of the problem.

| Scene | `scene` id | Tags | The I/O that makes it different |
|---|---|---:|---|
| **Sorting by height** (built-in) | `sorting-by-height` | 16 | `conveyor.rotate` · `emitter.emit` · `sensor_low.detect` · `sensor_high.detect` · `pusher.extend`/`.extended`/`.retracted` · `counter.tall`/`.short` · `stack_light.green` |
| **Start / stop station** | `start-stop-station` | 14 | `belt.rotate` · `part_present.detect` · `counter.count` · `produced.value` · `tower.red`/`.yellow`/`.green` |
| **Tank level control** | `tank-level-control` | 13 | `tank.fill` (float out) · `tank.drain` (float out) · `tank.level` (float in) · `level_readout.value` |
| **Light curtain sorting** | `light-curtain-sorting` | 15 | `height_gauge.height` (float in) · `height_gauge.blocked` · `diverter.extend`/`.extended`/`.retracted` · `tall_count.count` · `short_count.count` |
| **Roller line with weighing** | `roller-line-weighing` | 13 | `infeed.rotate` · `scale.rotate` · `scale.weight` (int in) · `metal_check.detect` · `weight_readout.value` · `outfeed.count` |

All five carry the six `panel.*` tags (`start`/`stop`/`reset`/`estop` in,
`green`/`red` out). Note that the four templates use **`belt.rotate`**, not the
built-in scene's `conveyor.rotate` — that one difference is the root of §2.1.

---

## 2. Findings

### 2.1 Four of the five scenes have nothing that can run them — blocker

Every runnable artifact in the repository targets `sorting-by-height` and only
it:

| Artifact | Writes |
|---|---|
| `engine/src/Sim/DemoDriver.cs` — the "Watch it run" button | `conveyor.rotate`, `emitter.emit`, `pusher.extend`, `stack_light.green` |
| `tools/live_driver.py` — what `run_factoryforge.bat` auto-starts | same |
| `tools/drive_engine.py` — the determinism contract | same |
| `examples/fake_plc.py` + `examples/tia/Sorting.scl` | same |
| `examples/opcua_mapping.json`, `plcsim_mapping.json`, `snap7_mapping.json` | same |
| `examples/nodered/factoryforge-flow.json` | same |

None of those tag ids exist in any template. `DemoDriver` writes through
`SetIfPresent` (`DemoDriver.cs:108`), which silently skips a tag that is not in
the table, so on a template **every write is a no-op**.

The failure mode is worse than "nothing happens". `Start()` sets `Active = true`
regardless, and `SceneToolbarUI` renders that state, so the button turns green
and reads **⏹ Demo** while the factory sits perfectly still. It reports running
while doing nothing — the same class of dishonesty FF-06 and FF-23 were about.

The highest-value finding here. The start screen devotes its entire left column
to the four templates and its single loudest button to "Watch it run"; the two
do not work together.

*Fixed by:* UX-16 … UX-20.

### 2.2 `run_factoryforge.bat` fights the app it launches

* **Force-kills whatever holds port 7411** via `Stop-Process -Force`, with no
  prompt and no check that it is even a FactoryForge process.
* Falls back to `D:\Godot_v4.7.1-stable_mono_win64\...` — the author's own
  machine path — when Godot is not on `PATH`.
* Auto-starts `tools/live_driver.py` four seconds after launch. That connects a
  real client, and `DemoDriver` stands down the instant `Bus.HasClient` is true.
  **Launching the documented way disables the "Watch it run" button**, and the
  Python driver started in its place only drives one of the five scenes.

*Fixed by:* UX-23, UX-27, UX-28.

### 2.3 `--scene=` is silently ignored headless

Verified: `godot --headless --path engine -- --scene=res://templates/tank_level_control.json`
reports `scene 'sorting-by-height', 16 tags`. So do all four templates.

Cause: both `--scene=` and `--demo` are handled inside `BuildView`
(`Main.cs:309`), and `Main.cs:105` skips `BuildView` entirely when
`DisplayServer.GetName() == "headless"`. The flags parse fine, nothing consumes
them, and nothing warns.

Two more consequences of the same block:

* `--scene=` and `--demo` are an `if` / `else if`, so `--scene=X --demo` loads
  the template and **silently discards `--demo`**. "Open this template and run
  its demo" is not expressible.
* `--deterministic --scene=X` produces a **hybrid tag table**. Measured on
  `light_curtain_sorting`: 24 tags — the deterministic sorting scene's 10 plus
  the template's 15 — published under scene name `light-curtain-sorting`. A
  driver sees `conveyor.rotate` and `belt.rotate` side by side, one belonging to
  a scene that is not on screen.

The spike in §0 shows the fix is small.

*Fixed by:* UX-10, UX-11, UX-12.

### 2.4 The install path still has the author's machine in it

* `docs/GETTING_STARTED.md:20` — the student guide's **first command** is
  `cd C:/Users/masal/source/factoryforge`.
* `tools/drv_trace.py:5` — `ROOT = Path(r"C:\Users\masal\source\factoryforge")`.
* `run_factoryforge.bat` — the `D:\Godot...` fallback above.

FF-09 removed the developer's PLC IP and instance name from the shipped UI
defaults. That cleanup never reached the docs, the launchers, or `tools/`.

*Fixed by:* UX-24.

### 2.5 Shipped material contradicts the `connect` vs `demo` warning

`GETTING_STARTED.md:111` carries a section headed *"`connect` vs `demo` — read
this first"*, warning that picking wrong "wastes an afternoon". Two shipped
files then pick wrong:

* `run_plcsim_advanced.bat` runs `demo --driver plcsim-advanced -o instance Sorting_PLC`
  — also hardcoding the author's own instance name.
* `examples/fake_plc.py`'s docstring instructs `demo --driver opcua-client`.

Both start a headless Python scene instead of driving the 3D view.

*Fixed by:* UX-25.

### 2.6 `connect --driver mock` is a trap

`mock` is a passive transport with no control logic (`drivers/mock.py` — it
records history and exposes an async API for tests). Run against a live engine
it connects a client, which stands `DemoDriver` down, and then drives nothing.
The most obvious command for "let me just try it" leaves the scene *more* dead
than doing nothing at all.

*Fixed by:* UX-21 (a real `try_scene.py` to point people at instead).

### 2.7 Run mode is the least discoverable thing in the app

Edit and Run are a good idea — `EditorMode.cs` argues it well: in a factory you
click a part to move it and click a button to press it, same mouse, opposite
intent. The implementation is sound; the presentation is not.

* **The toolbar says "Run" twice, adjacent, with the same glyph.** The mode
  button renders `▶ RUN` / `✎ EDIT` (`SceneToolbarUI.cs:62`); the pause button
  beside it renders `▶ Run` / `⏸ Pause` (`SceneToolbarUI.cs:74`). Paused in Run
  mode, the toolbar literally reads `▶ RUN … ▶ Run`, meaning two unrelated
  things.
* **Mode does not start or stop anything.** Physics, the emitter and the demo
  driver all run in Edit mode. Mode only changes what a click means. But it sits
  beside Pause, Reset and the rate selector, which *do* control time.
* **Only a Control Panel is clickable in Run mode.** `PressControlAt`
  (`SceneEditor.cs:515`) ray-tests for `ButtonPanel` and nothing else. Build a
  line without one and Run mode is a mode in which nothing responds.
* **Nothing marks a control as pressable.** No hover highlight, no cursor
  change. You find the caps by guessing.
* **`Ctrl+S` is silently dead in Run mode.** `_UnhandledInput` handles F1 then
  returns early for every other event when `Mode == Run`
  (`SceneEditor.cs:195`), taking Ctrl+S, Ctrl+O, Ctrl+Z/Y, `M`, `R`, `Del` and
  Ctrl+D with it. Blocking the *editing* keys is right; silently swallowing
  **save** is not.

*Fixed by:* UX-37 … UX-40.

### 2.8 The Force button only works on `bit` tags — blocker for manual operation

`TagInspectorUI.ToggleForce` (`TagInspectorUI.cs:138`) reads:

```csharp
if (tag.Type == TagType.Bit) { ... }
```

with no `else`. Press **Force** on an `int` or `float` tag and nothing happens —
no value forced, no message, and the button still reads "Force". There is no
value-entry field anywhere in the inspector; each row is a name button, a value
label, and that one button.

A UI gap, not a protocol one. `TagTable.Force` takes an `object`, the wire
`force` message carries any JSON scalar, and parts read forced values through
`Tags.TryGetVisible` — so forcing a **bit** output genuinely drives the machine,
and forcing a float *would* too. Only the button refuses.

What it costs:

* **The tank scene cannot be made to do anything by hand.** `tank.fill` and
  `tank.drain` are float outputs. No demo profile (§2.1), no clickable control
  (§2.7), no forceable tag. It is completely inert — and it is the template the
  start screen recommends for analog work.
* **No Digital Display can ever show a number by hand.** `*.value` is an `int`
  output on all three scenes that have one.

*Fixed by:* UX-35, UX-36.

### 2.9 You cannot operate a component *from the component*

The property panel is where you meet a part: click it and you get its type, its
**Name** field, and numeric spin boxes for its settings — speed, friction,
stroke, incline (`PartPropertyInspectorUI.cs`, driven by `PartProperties.cs`).

It does not show that part's **I/O at all**, and offers no way to operate it.
So "turn this conveyor on and see what it does" means: know that a conveyor owns
a `.rotate` tag, know the part's name is the tag prefix, find `belt.rotate` in a
separate scrolling list of 13–16 tags on the other side of the screen, and press
Force there. Three indirections between the thing on screen and the switch that
turns it on.

That is the gap §5 specifies closing. The part is right there and already
selected — its I/O belongs on the same panel as its settings.

*Fixed by:* UX-34, UX-37.

### 2.10 The startup line reports the wrong scene name

`Main.cs:120` prints `$"scene '{SceneName}', {tags.Count} tags"` using the
**const** at `Main.cs:29`, which is always `"sorting-by-height"`. The bus itself
is correct — `AdoptSceneName` sets `_bus.SceneName` from the loaded scene — so
the two disagree. Observed during the spike: the log said
`PHYSICS scene 'sorting-by-height', 14 tags` while the bus published
`start-stop-station`.

The tag count is right, which makes it more confusing rather than less: the line
looks plausible and is wrong about the only thing it names.

*Fixed by:* UX-15.

### 2.11 Smaller, cheap to fix

* The README badge and `GETTING_STARTED.md:27` both say **41 tests**; the suite
  is **71**.
* `GETTING_STARTED.md` says Python 3.10+; `sidecar/pyproject.toml` requires
  `>=3.11`.
* README and GETTING_STARTED both tell the user to run `"<GODOT_CONSOLE_EXE>"` —
  a placeholder they must resolve themselves, with no hint of where the mono
  build lives or that the plain non-mono Godot will not work.
* There is no launcher for Linux or macOS, only the `.bat`, even though
  `DriverConnectionUI.SpawnInTerminal` now supports both.
* No binary release — now **Phase 0**, not a footnote.

*Fixed by:* UX-26, UX-29.
---

## 3. Plan

### How to read a work item

```
UX-nn — one-line title
  Files:      what gets touched
  Done when:  the observable state that means it is finished
  Verify:     the command or the click that proves it
  Size:       S | M | L | XL
```

**Sizes.** `S` = under half a day. `M` = one to two days. `L` = three to five
days. `XL` = more than a week, or unknown until a spike lands. Sizes assume
familiarity with this codebase; they are relative, not a schedule.

### Sequencing

Phases are numbered by dependency, not by date. Phase 1 gates Phase 2;
everything else can be reordered. Recommended order of work:

> **Phase 3 → Phase 5 → Phase 1 → Phase 2 → Phase 4 → Phase 6**, with **Phase 0
> running in parallel from the start.**

Phase 3 is nearly free and fixes what a new user hits in the first ten minutes.
Phase 5 is the one that makes the app explorable — it is what turns "five scenes
you can look at" into "fifteen components you can switch on" — and after the
spike it no longer waits on anything. Those two buy the most per hour spent.
Phase 0's engineering is long-lead and independent, so it should start now.

One caveat on Phase 0, stated once: **cut the first release after Phases 3, 5
and 2 land, not before.** A binary built from today's `master` would package a
start screen offering five scenes, four of which cannot be made to move, and a
tank that cannot be operated at all. Building the machinery now is right;
shipping v0.1 out of it before there is something worth downloading is the part
worth waiting on.

**Platform scope:** Windows and Linux. Both export presets already exist and CI
already runs Linux headless, so the second target is close to free. macOS is
deferred (§7).

---

### Phase 0 — Ship a binary

Today, running FactoryForge means installing Godot 4.7.1-mono, the .NET 8 SDK
and Python, then running `dotnet build` — a real barrier for a student who
wanted to learn ladder logic, not to install a game engine and a compiler.

`docs/PACKAGING.md` already lays out the intended recipe honestly, including its
own status: **nothing in it is verified.** Confirmed while writing this — the
Godot export templates are not installed on this machine, so no binary has ever
been produced from `engine/export_presets.cfg`.

**UX-01 — Produce a Windows and a Linux binary at all**
*Files:* none at first; `engine/export_presets.cfg` if the presets need fixing.
*Done when:* `dist/windows/FactoryForge.exe` and `dist/linux/FactoryForge.x86_64`
exist and each launches to the start screen with a template loadable.
*Verify:* run each binary; pick "Sorting by height"; the scene renders.
*Size:* M — mostly the ~1 GB export-template download and first-run fiddling.
*Blocks:* every other item in this phase.

**UX-02 — Correct `PACKAGING.md` with what actually happened**
*Files:* `docs/PACKAGING.md`.
*Done when:* the status note at the top no longer says "not verified", and every
step reflects the real commands.
*Verify:* a second person follows it start to finish.
*Size:* S.

**UX-03 — Decide and implement how Python ships**
*Files:* new `tools/build_release.py`; `sidecar/pyproject.toml`.
*Done when:* a release archive contains a runnable sidecar with no `pip install`
step. `PACKAGING.md` recommends PyInstaller-per-platform; that is the only
option where a person downloads one archive and connects to a PLC without
reading anything.
*Verify:* on a machine with no Python, extract the archive and connect the
sidecar to a running engine.
*Size:* L. *Depends on:* UX-01.

**UX-04 — Make the engine find a bundled sidecar**
*Files:* `engine/src/Editor/DriverConnectionUI.cs`.
*Done when:* F5's *Apply & Connect* starts the sidecar in a packaged build.
Today `ApplyConnectionSettings` (`DriverConnectionUI.cs:391`) derives the
sidecar directory from the **parent** of `ProjectSettings.GlobalizePath("res://")`
— correct in a checkout, where `res://` is `engine/` and `sidecar/` sits beside
it, but in an export `res://` resolves to the executable's own directory, so it
looks for `<parent-of-exe>/sidecar` and misses. The dialog then falls through to
"command copied, run it yourself" as the **normal** path rather than the
exception. Needs a resolution order: bundled sidecar beside the executable → a
`FACTORYFORGE_SIDECAR` override → `PATH` → the existing copied-command fallback.
*Verify:* press *Apply & Connect* in the packaged build; the sidecar console
opens.
*Size:* M. *Depends on:* UX-03.

**UX-05 — Decide "one file" or "one folder"**
*Files:* `engine/export_presets.cfg`, `docs/PACKAGING.md`.
*Done when:* either `binary_format/embed_pck=true` on both presets, or the docs
state the archive layout plainly. Both presets currently set it to `false`, so a
release is an executable *plus* a `.pck` *plus* the .NET assemblies.
*Verify:* the described layout matches what `build_release` produces.
*Size:* S.

**UX-06 — Settle what ships alongside the binary**
*Files:* `tools/build_release.py`, `docs/PACKAGING.md`.
*Done when:* the archive contains the engine, the sidecar, `examples/`, the
docs, and — once Phase 2 lands — the per-scene exercises. `templates/` are
`res://` resources and travel automatically; nothing else does.
*Verify:* extract the archive and run each shipped exercise.
*Size:* S. *Depends on:* UX-03.

**UX-07 — A release CI job**
*Files:* new `.github/workflows/release.yml`.
*Done when:* pushing a tag builds Windows and Linux, runs the headless
self-tests **against the exported binary**, and attaches both archives to the
release.
*Verify:* push a `v0.1.0-rc1` tag; artifacts appear and the self-tests gate them.
*Size:* L. *Depends on:* UX-01.

**UX-08 — Fix the fixture path that breaks self-tests in an export**
*Files:* `engine/src/Sim/TagParitySelfTest.cs`, `.github/workflows/release.yml`.
*Done when:* the exported-binary test run passes. `--self-test=parity` reads
`res://../tests/fixtures/tag_cases.json` (`TagParitySelfTest.cs:35`), a path
that does not exist in an export. Either ship the fixture as a resource or run
that one test only in the source-tree job.
*Verify:* run every self-test against the exported binary; all pass or are
explicitly excluded with a reason.
*Size:* S. *Depends on:* UX-01.

**UX-09 — Decide on code signing, or warn honestly**
*Files:* `docs/PACKAGING.md`, `README.md`.
*Done when:* either releases are signed, or the download page says in plain
words that Windows SmartScreen will warn and why. Signing costs a certificate
and a process; not signing costs every first-time user a scary dialog. Either is
defensible — silently doing neither is not.
*Verify:* download the release on a clean Windows box and follow the docs
through the warning.
*Size:* S as a decision; L if signing is chosen.

---

### Phase 1 — Make scenes scriptable — done

The prerequisite for Phase 2. **De-risked by the spike in §0** — this is now
small, mechanical work.

All five items landed together (`engine/src/Main.cs`, new
`engine/templates/manifest.json`, new `engine/src/Editor/TemplateManifest.cs`).
Verified beyond the self-tests below: forced `tank.fill` to 0.5 over a live
tag-bus connection to the template loaded via `--scene=` headless and watched
`tank.level` climb from 0.0 to 1.08 over 10.5s (real Torricelli physics, no
renderer) — the exact scenario UX-35's verify step names. Also confirmed the
manifest-driven start screen renders identically to the old hardcoded one by
screenshot, and that the widened Tag Inspector shows a real editable value
field next to `counter.tall`/`counter.short` in a live window, not just headless.
New regression coverage: `tools/test_plan.py` C7 (`--scene=` + `--print-tags`
headless, asserts scene name and tag count) and C8 (`--deterministic
--scene=` rejected, non-zero exit). `python -m pytest -q` still 71 passed.

**UX-10 — Load `--scene=` headless — done**
*Files:* `engine/src/Main.cs`.
*Done when:* `godot --headless --path engine -- --scene=res://templates/X.json`
publishes X's scene name and X's tags. Proven by the spike: call
`editor.LoadTemplate(_scenePath)` in `BuildHeadlessPhysicsParts`
(`Main.cs:382`) instead of `RegisterDefaultSceneParts`, and adopt the name.
*Verify:* all four templates report their own name and tag count headless,
matching the windowed run.
*Size:* S.

**UX-11 — Make `--scene` and `--demo` compose — done**
*Files:* `engine/src/Main.cs` (the `else if` at `:315`).
*Done when:* `--scene=X --demo` loads X *and* starts the demo.
*Verify:* run it; the template loads and moves.
*Size:* S.

Verified windowed (this is a `BuildView`-only flag today, matching the plan's
file scope): `tank_level_control.json` loads and reports its own name with
`--demo` set, no crash. `DemoDriver` itself still only has a sorting-line
profile (§2.1) — it composes correctly but has nothing scene-appropriate to do
yet on a template, which is exactly the gap Phase 2 (UX-16…UX-20) closes.

**UX-12 — Reject `--deterministic --scene=` — done**
*Files:* `engine/src/Main.cs`.
*Done when:* the combination exits with a message naming why, instead of
publishing a 24-tag hybrid of two scenes.
*Verify:* run the combination; a clear error, non-zero exit.
*Size:* S.

Found and fixed a second bug while verifying this one: `GetTree().Quit(1)`
schedules the exit for end-of-frame rather than stopping immediately, so
`_Process` still ran once more with `_bus` never constructed and crashed on a
`NullReferenceException` instead of exiting cleanly on the message already
printed. Guarded `_Process` with an early return while `_bus` is null.
Covered by `tools/test_plan.py` C8.

**UX-13 — A scenes manifest — done**
*Files:* new `engine/templates/manifest.json`;
`engine/src/Editor/StartScreenUI.cs` (replacing the hardcoded `Templates` array
at `:38`); `tools/` consumers.
*Done when:* id, title, blurb, path and scene name live in one file that both
the start screen and the Python tooling read, so a sixth template appears
everywhere at once.
*Verify:* add a dummy entry; it shows on the start screen and in
`try_scene.py --list` with no C# change.
*Size:* M.

Implemented as `engine/templates/manifest.json` (five entries: id, title,
blurb, path, scene) read by a new shared `TemplateManifest.Load()`. Two
consumers switched over: `StartScreenUI`'s hardcoded array, and
`TemplateSelfTest`'s separate hardcoded path list — the exact duplication this
item exists to remove. `TemplateSelfTest` keeps a small `MustContain` map
keyed by manifest id (which parts each template must contain), since that is
test fixture data, not something the start screen or Python tooling needs.
Verified by screenshot: the start screen renders identically to the old
hardcoded version. The `try_scene.py --list` half of Verify is not yet
checkable — `try_scene.py` is UX-21, not built yet — but the manifest itself
is plain JSON any Python tool can read with no C# change, which is the part
of "done when" this phase owns.

**UX-14 — `--print-tags` — done**
*Files:* `engine/src/Main.cs`.
*Done when:* the flag dumps the `describe` table to stdout and exits, so no
script needs a WebSocket client just to ask what a scene exposes.
*Verify:* output matches what a bus client sees for the same scene.
*Size:* S.

Prints the exact same `{"t":"describe","scene":...,"epoch":...,"tags":[...]}`
shape `TagBusServer.SendDescribe` sends, built from the same `Tag.ToJson` — not
a hand-rolled copy that could drift from the real wire format. Covered by
`tools/test_plan.py` C7.

**UX-15 — Report the loaded scene's real name at startup — done**
*Files:* `engine/src/Main.cs:120`.
*Done when:* the ready line prints the scene actually loaded, not the
`SceneName` const (§2.10).
*Verify:* load a template; the log and the bus agree.
*Size:* S.

One-line fix: the ready line now interpolates `_bus.SceneName` instead of the
`SceneName` const. Verified across all four templates headless — the ready
line and the `--print-tags` `"scene"` field agree in every case.

---

### Phase 2 — One runnable exercise per scene — UX-16…UX-20 done, UX-21/22 not started

The behaviour spec for all five is §4. **Both homes, C# first** — decided:

| | C# `DemoDriver` profiles | Python `tools/try_scene.py` |
|---|---|---|
| Install cost | none | needs the sidecar |
| Serves | the "Watch it run" button, all 5 scenes | the "test before PLC" script |
| Also proves | the scene | the scene **and** the engine↔sidecar seam |
| Assertions | none — it is a demo | yes: exit code + `RESULT` line |

**Assertions must be band-based, not exact.** Templates always run the Jolt
physics scene (§2.3), so no template can promise exact counts the way
`drive_engine.py` does. Assert "at least N counted", "level held within ±5% for
10s", "tall+short equals emitted" — never "tall == 5".

**UX-16 — A per-scene profile mechanism in `DemoDriver` — done**
*Files:* `engine/src/Sim/DemoDriver.cs`.
*Done when:* `DemoDriver` looks up a profile by the loaded scene name and runs
it, and reports honestly when there is none (see UX-30).
*Verify:* the existing sorting behaviour is unchanged, now expressed as a
profile.
*Size:* M. *Depends on:* UX-13.

Implemented as `IDemoProfile` (`Start`/`Tick`) plus one class per scene under
`engine/src/Sim/DemoProfiles/`, looked up from a `scene id → factory`
dictionary keyed to the manifest's ids. `RefusalReason` is set instead of
silently leaving `Active` false, so a custom scene with no profile refuses
audibly rather than turning "Demo" green over nothing — the same dishonesty
class as FF-06/FF-23, closed before it could happen here too. The original
sorting logic moved into `SortingByHeightProfile` unchanged (same constants,
same code); `--demo --duration=25` on the default scene still gives
`tall=3 short=3` after the refactor, matching pre-refactor behaviour.

**A second, unrelated bug found while verifying this and fixed alongside it:**
`DemoDriver` read tags from `_Process` (the frame clock). A panel button's
press is exactly one `_PhysicsProcess` tick wide (`SceneEditor.StepPanelButtons`
sets it, then clears it at the top of the very next physics tick), and headless
has no vsync holding `_Process` and `_PhysicsProcess` at the same cadence —
uncapped, the engine can run several physics ticks per frame to catch up. A
frame-clock reader can watch a one-tick pulse turn on and off again between two
of its own calls and never see it high at all. This reproduced deterministically
(3/3 runs) right after a template load's own allocation hitch gave the catch-up
loop something to catch up on — exactly the moment a real user clicks Start
after a template just finished loading. Moved `DemoDriver` to `_PhysicsProcess`
(it is already added to the tree after `SceneEditor`, so ordering within a tick
is unchanged); UX-17's self-test went from reproducibly failing to reproducibly
passing (3/3) with no other change. This was not introduced by this session's
refactor — the original `DemoDriver` had the same `_Process` read against the
same kind of physics-tick-driven tag (`sensor_high.detect`) and would have had
the same exposure, just with lower odds of noticing since the sorting profile
has no latched state to make a missed edge visible.

**UX-17 — `start-stop-station` profile — done** — §4.2. *Size:* S. *Depends on:* UX-16.
**UX-18 — `tank-level-control` profile — done** — §4.3; needs a small controller, not
just a timer. *Size:* M. *Depends on:* UX-16, UX-35.
**UX-19 — `light-curtain-sorting` profile — done** — §4.4; handshakes on
`diverter.extended`/`.retracted` rather than a timer. *Size:* M. *Depends on:* UX-16.
**UX-20 — `roller-line-weighing` profile — done** — §4.5. *Size:* S. *Depends on:* UX-16.

*Verify (UX-17 … UX-20):* load each template, press 🎬 Demo, watch the line run
for 30 s with no PLC and no Python.

Verified with four new physics-timed self-tests
(`--self-test=startstop|tank|lightcurtain|roller`, `tools/test_plan.py`
C10–C13) rather than only by eye, since "watch it run" is hard to re-check on
every future change:

* **start-stop-station** — drives the real `ButtonPanel.Press()` API (not a
  direct tag write) through Start → E-stop → Start-while-tripped (refused) →
  release → Reset → Start again, asserting the lamp and belt never disagree.
  Deliberately broke the Start-edge branch and watched it fail (4 failures),
  then restored it.
* **tank-level-control** — runs the controller for ~13 simulated seconds and
  checks `tank.level` settled within ±5% of the 55% setpoint, and
  `level_readout.value` tracks it. Real Torricelli physics, no shortcuts.
* **light-curtain-sorting** — runs ~20 simulated seconds and checks both
  `tall_count.count` and `short_count.count` advanced (got `tall=2 short=3`),
  proving the diverter both fires for tall cartons and stays retracted for
  short ones. Deliberately broke `TallThreshold` (set to an unreachable 5.0m)
  and watched `tall_count.count` stay at 0 while `short_count.count` kept
  climbing — the exact failure mode a wrong threshold produces — then restored it.
* **roller-line-weighing** — runs ~20 simulated seconds and checks
  `outfeed.count` advances and `metal_check.detect` fired at least once
  (`outfeed=4 sawMetal=True`) — the material-aware sensing claim, checked
  rather than only asserted in a README.

All four passed on the first real run once the `_PhysicsProcess` fix landed.
`python -m pytest -q` unaffected (71 passed); full `test_plan.py --only A,C,E`
21 passed in 165s.

**UX-21 — `tools/try_scene.py` — not started**
*Files:* new `tools/try_scene.py`; fold `tools/drive_engine.py` in behind it.
*Done when:* `--scene <id>`, `--duration`, `--verbose`, `--list`; one
`RESULT ...` line; exit 0/1 — the convention `check_protocol.py` and
`check_force_while_paused.py` already set.
*Verify:* `python tools/try_scene.py --scene sorting-by-height` reproduces
`drive_engine.py`'s `tall=5 short=5` exactly.
*Size:* M. *Depends on:* UX-10, UX-13.

**UX-22 — Assertions for all five scenes in `try_scene.py`**
*Files:* `tools/try_scene.py`.
*Done when:* each scene's §4 assertions run and fail loudly on a real
regression.
*Verify:* break one thing deliberately per scene and watch the right assertion
fail.
*Size:* M. *Depends on:* UX-21.

---

### Phase 3 — First-run friction — done

Cheapest phase, highest visibility to a new user, no engine changes.

`run.py` is the new cross-platform launcher (`run_factoryforge.bat` and
`run.sh` are now thin wrappers around it); it covers UX-23, UX-27 (names the
process holding port 7411 and asks, instead of killing it) and UX-28 (no
driver starts unless `--live-driver` is passed) in one file, and UX-29 for
free since it is plain Python. UX-24 (author-machine paths), UX-25
(`demo`→`connect`), and UX-26 (stale facts: 41→71 tests, 3.10→3.11, the
`"<GODOT_CONSOLE_EXE>"` placeholder) are fixed in the files the plan named.
Verified: `python -m pytest -q` → 71 passed; `run.py` launches, builds, binds
the tag bus, and correctly refuses/warns on a held port and a missing Godot.

**UX-23 — One cross-platform launcher**
*Files:* new `run.py` (or `run.sh` + a rewritten `run_factoryforge.bat`).
*Done when:* it finds Godot-mono or says exactly what to download, checks for
the .NET SDK and Python, runs `dotnet build`, and launches — nothing more.
*Verify:* run it on a machine where Godot is not on `PATH`; the message names
the required build and where to put it.
*Size:* M.

**UX-24 — Purge the author's machine from the repo**
*Files:* `docs/GETTING_STARTED.md:20`, `tools/drv_trace.py:5`,
`run_factoryforge.bat`.
*Done when:* no shipped file contains `C:\Users\masal` or `D:\Godot...`.
*Verify:* `grep -ri "users.masal\|D:\\\\Godot" --exclude-dir=.git .` is empty.
*Size:* S.

**UX-25 — Fix `demo` → `connect` in shipped material**
*Files:* `run_plcsim_advanced.bat`, `examples/fake_plc.py` docstring.
*Done when:* both use `connect`, and neither hardcodes `Sorting_PLC`.
*Verify:* follow each file's own instructions against a running engine; the 3D
view moves.
*Size:* S.

**UX-26 — Refresh stale facts**
*Files:* `README.md` (test badge), `docs/GETTING_STARTED.md`.
*Done when:* 41 → 71 tests, Python 3.10 → 3.11, and `"<GODOT_CONSOLE_EXE>"`
replaced with a real sentence naming the required build and where the launcher
looks for it.
*Verify:* `pytest -q` count matches the badge.
*Size:* S.

**UX-27 — The launcher must not kill port 7411 silently**
*Files:* `run_factoryforge.bat` / `run.py`.
*Done when:* it names what holds the port and asks, instead of `Stop-Process
-Force` on an unidentified process.
*Verify:* hold 7411 with an unrelated process and run the launcher.
*Size:* S.

**UX-28 — The launcher must not auto-start a driver**
*Files:* `run_factoryforge.bat` / `run.py`.
*Done when:* launching leaves "Watch it run" working (§2.2). Starting a driver
stays opt-in via a flag.
*Verify:* launch, press "Watch it run", the line moves.
*Size:* S.

**UX-29 — A Linux launcher**
*Files:* new `run.sh`.
*Done when:* the same flow works on Linux, matching UX-23.
*Verify:* run it on the CI image.
*Size:* S. *Depends on:* UX-23.

---

### Phase 4 — Tell the truth in the UI — UX-30/32 done, UX-31/33 blocked on UX-21

**UX-30 — The Demo button refuses honestly — done**
*Files:* `engine/src/Sim/DemoDriver.cs`, `engine/src/Editor/SceneToolbarUI.cs`.
*Done when:* with no profile for the loaded scene, the button says so and does
**not** turn green (§2.1). The FF-06 fix applied to the demo path.
*Verify:* load an empty scene, press Demo, read the refusal.
*Size:* S. *Depends on:* UX-16.

`Active` staying false was already true from UX-16 (Phase 2) — the actual gap
was that the refusal reached only the console. Added a `DemoDriver.Refused`
signal (distinct from `ActiveChanged`, so pressing Demo twice on the same
broken scene says so twice rather than staying silent after the first
`RefusalReason` value is "already known") and wired it into `IdleHintUI`,
which forces itself visible with the specific reason for a few seconds — even
if the ambient idle nag was already dismissed, since this is a direct
response to a click, not an ambient one. Covered by a new self-test
(`--self-test=refusal`, `tools/test_plan.py` C14) that subscribes exactly the
way the UI does and checks the panel and its label, not just the driver's own
state. Deliberately commented out the `EmitSignal` call and watched it fail
(2 failures), then restored it.

**While verifying this, found and fixed a second, older bug it depends on:**
`StartScreenUI.DefaultSceneChosen` and `.EmptySceneChosen` never called
`AdoptSceneName`, unlike every other scene-changing path (`TemplateChosen`,
`OpenRequested`, the toolbar's Load). So the bus kept reporting whatever scene
name was already stale after "Sorting by height" or "Empty scene" — on Empty
specifically, this meant `Bus.SceneName` never became `"untitled"`, so Demo
would try to run `SortingByHeightProfile` against an empty scene instead of
refusing, reproducing the exact silent-no-op bug UX-30 exists to close. Fixed
both call sites in `Main.cs`.

**UX-31 — A "Try this scene" affordance — not started, blocked on UX-21**
*Files:* `engine/src/Editor/SceneToolbarUI.cs`.
*Done when:* it runs the right exercise for whatever is loaded and copies the
command — the same shape as F5's *Apply & Connect*.
*Verify:* press it on each template; the exercise runs.
*Size:* M. *Depends on:* UX-21.

**UX-32 — Keep "what this scene teaches" reachable — done**
*Files:* `engine/src/Editor/StartScreenUI.cs`, a new panel or the property
panel's empty state.
*Done when:* each template's blurb (`StartScreenUI.cs:38`) and its tag list stay
available after the scene loads, instead of vanishing with the start screen.
*Verify:* load a template; find its description without going Home.
*Size:* M. *Depends on:* UX-13.

The tag list half was already true — the Tag Inspector panel (top-right) has
always shown the loaded scene's live tags regardless of the start screen.
What vanished was the blurb, so `PartPropertyInspectorUI`'s empty state
("Click a placed part…") now leads with the loaded template's own title and
blurb, read from the same `TemplateManifest` the start screen uses, matched
against `Editor.SceneName`.

Found and fixed a genuine layout risk while wiring this up, the same class of
bug as the F5 dialog fix earlier in this plan: the empty state's content
container sat directly in a fixed-height `PanelContainer` with nothing
scrollable, and the longest blurb (tank's, four wrapped lines) plus the
standing "click a part" text does not fit the panel's original 220px. Wrapped
it in a bounded `ScrollContainer` (matching `TagInspectorUI`'s own pattern)
before shipping the new content, rather than after finding it broken by eye.

Also found and fixed a real timing bug via a `--scene=` screenshot: the empty
state redraws mid-load, via `SceneEditor.DeselectPart()`'s **direct** call
into `PropertyInspector.InspectNode(null,…)` when the old scene's parts clear
— and that happens *before* `SceneName` is updated to the new scene, so the
panel briefly (and, on the `--scene=` startup path specifically, persistently)
showed the previous scene's blurb. Refreshing only on `TagsChanged` was not
enough — during `--scene=` startup that event fires before anything has
subscribed to it. Fixed by refreshing from `AdoptSceneName` itself (a `Main`
field, `_propertyInspector`, added for exactly this), which every
scene-adoption path already calls after the swap is genuinely complete.
Verified by screenshot: `--scene=tank_level_control.json` now shows "Tank
level control" and its real blurb, scrolling correctly, not "Sorting by
height" left over from the scene that was replaced.

**UX-33 — F5's empty state offers the exercise — not started, blocked on UX-31**
*Files:* `engine/src/Editor/DriverConnectionUI.cs`.
*Done when:* with no driver connected the dialog reads *"No PLC yet? Run the
built-in exercise for this scene first."* with a button.
*Verify:* open F5 on a fresh launch.
*Size:* S. *Depends on:* UX-31.

---

### Phase 5 — Operate any component by hand

**The phase that makes the app explorable.** Specification in §5. Today, turning
a conveyor on means knowing it owns a `.rotate` tag, knowing the part name is
the tag prefix, finding that id in a 16-row list on the far side of the screen,
and pressing Force there — and if the tag is a float, there is no way at all
(§2.8, §2.9).

**UX-34 — Live I/O in the part property panel — done**
*Files:* `engine/src/Editor/PartPropertyInspectorUI.cs`, `PartTagManager.cs`.
*Done when:* selecting a part shows its own tags beneath its settings, each with
a control: a toggle for `bit` outputs, a value field or slider for `int`/`float`
outputs, and a live readout for inputs with an override toggle. **The single
highest-value item in this phase** — it puts the switch on the same panel as the
thing it switches.
*Verify:* click a conveyor, flip its toggle, watch the belt start; click a tank,
drag the fill valve, watch the level rise.
*Size:* L. *Depends on:* UX-35.

Every own-tag row goes through `TagTable.Force` — the same call the Tag
Inspector's own button makes, so "forcing is sticky" (§5.2) holds here too: a
control left touched keeps winning over a driver that connects later, exactly
like it always has. A `bit` output gets a `CheckButton` (a real toggle switch,
not a Force/UNFORCE button); `int` a `SpinBox`; `float` an `HSlider` (0–100,
matching the only float outputs that exist — `tank.fill`/`.drain`) — each
wired to Force on every value change, immediately, not on a separate "apply"
click. One named exception: `.emit` (Box Emitter) is a rising edge, not a
level — `SceneEditor`'s dispatch only spawns a box on the edge, so a plain
toggle left on would look broken. It gets an "Emit one" button instead: force
true, then clear the force ~50ms later.

An `int`/`float` input's own value only shows read-only, with a checkbox
labelled **Override** beside it — checking it forces the tag at its current
value and reveals the same kind of control an output gets; unchecking clears
the force and returns to the live readout. Answers "what does my PLC do if
this sensor is stuck on?" (§5.4) without leaving the panel to ask it.

**Two real bugs found and fixed while building this, both about signals firing
when they should not, or not being caught when they should have been:**

* Godot's `Range` controls (`SpinBox`, `HSlider`) have no `SetValueNoSignal`
  the way `BaseButton` has `SetPressedNoSignal` — setting `.Value` to the tag's
  *own current* value to initialise a fresh control's position fires
  `ValueChanged` anyway, which would have force-pinned every int/float output
  the instant a part was merely selected, before anyone touched anything.
  Guarded with an `_initializing` flag checked inside every `ValueChanged`
  handler.
* The empty state's content used to sit directly in a fixed-height
  `PanelContainer` with nothing scrollable — the same class of bug as the F5
  dialog fix earlier in this plan. Wrapped in a bounded `ScrollContainer`
  (`TagInspectorUI`'s own pattern) before adding any I/O rows, not after
  finding it overflow by eye.

Verified two ways. A new self-test (`--self-test=proppanel`,
`tools/test_plan.py` C15) drives the real controls -- found by the same
tooltip-based row search as the earlier self-tests, not a test-only accessor
-- for one of each combination on the tank scene (`tank.fill` float output,
`level_readout.value` int output, `panel.green` bit output,
`panel.estop` bit-input override) and checks `TagTable` actually changed.
Deliberately disabled the float slider's `ValueChanged` wiring and watched it
fail, then restored it. Also verified by screenshot, reusing the real
on-screen panel (`Editor.PropertyInspector`) rather than a second invisible
one: selecting the tank showed **`FillRate: 18`, `DrainRate: 22`** (its
existing settings) followed by an **I/O** section, and forcing `tank.fill` to
42 through the panel's own slider made `tank.level` climb on screen exactly
the way forcing it from the Tag Inspector did in UX-35 — same mechanism, now
one click away instead of three.

**UX-35 — Force any tag type, not just bits — done**
*Files:* `engine/src/Editor/TagInspectorUI.cs`.
*Done when:* `int` and `float` tags can be forced to a typed value from the
inspector. The plumbing exists — `TagTable.Force` takes an `object` and parts
read through `TryGetVisible` — so this is UI only (§2.8). It is what makes the
tank scene operable at all.
*Verify:* force `tank.fill` to 0.5; the level rises. Force `display.value` to
42; the display reads 42.
*Size:* M.

Implemented as a `LineEdit` beside the Force button for any non-bit tag,
pre-filled with the live value and left alone while it has focus or while the
tag is forced. Guarded by a new headless self-test (`--self-test=force`,
`tools/test_plan.py` C6) that drives the inspector's real controls — the same
LineEdit and Button a click would use, found by the row's own tooltip rather
than a test-only accessor — for a bit, an int and a float tag, and confirms
`TagTable` actually changed. Verified the guard is real by reverting the fix
and watching C6 fail (`Force did not force`), then restoring it.

The `tank.fill` / `display.value` verify steps above need a template loaded
headless or windowed by hand — Phase 1 (UX-10) isn't done yet, so this was
verified with a synthetic tag table instead (one bit, one int, one float tag),
which exercises the same code path.

**UX-36 — Until UX-35 lands, refuse audibly — done, folded into UX-35**
*Files:* `engine/src/Editor/TagInspectorUI.cs`.
*Done when:* pressing Force on a non-bit tag says why instead of doing nothing.
*Verify:* press it on `counter.tall`; a message appears.
*Size:* S.

Landed alongside UX-35 rather than as a stopgap ahead of it: an unparseable
value flashes the input field red for a second instead of silently doing
nothing. Covered by the same C6 self-test (`CheckInvalid`).

**UX-37 — Click a component in Run mode to operate it**
*Files:* `engine/src/Editor/SceneEditor.cs` (`PressControlAt`, `:515`), the part
classes.
*Done when:* Run mode is not `ButtonPanel`-only (§2.7): clicking a conveyor
toggles `.rotate`, a pusher strokes, a stack light stage toggles, a tank opens
its valve. Each part declares what a click on it means.
*Verify:* in Run mode, click each part type in turn and watch its tag move.
*Size:* L.

**UX-38 — End the "Run" collision in the toolbar**
*Files:* `engine/src/Editor/SceneToolbarUI.cs:62`.
*Done when:* the mode toggle no longer shares a word and a glyph with the pause
button — `✎ Build` / `👆 Operate` reads unambiguously beside `⏸ Pause` / `▶ Run`.
Drop the ▶ from the mode button whatever the wording.
*Verify:* pause while in Run mode; the toolbar reads unambiguously.
*Size:* S.

**UX-39 — Run mode explains itself**
*Files:* `engine/src/Editor/SceneEditor.cs`,
`engine/src/Editor/IdleHintUI.cs`.
*Done when:* entering Run mode says what is clickable; a scene with nothing
operable says *that* rather than presenting a dead mode; operable parts
highlight on hover.
*Verify:* enter Run mode on an empty scene, then on a full one.
*Size:* M. *Depends on:* UX-37.

**UX-40 — `Ctrl+S` and `Ctrl+O` survive Run mode**
*Files:* `engine/src/Editor/SceneEditor.cs:195`.
*Done when:* save and open work in both modes, or refuse out loud. Blocking the
editing keys in Run mode is correct; silently swallowing save is not (§2.7).
*Verify:* press Ctrl+S in Run mode; the save dialog opens.
*Size:* S.

**UX-41 — Show what is being held by hand, and release it in one click**
*Files:* `engine/src/Editor/TagInspectorUI.cs`,
`engine/src/Editor/SceneToolbarUI.cs`.
*Done when:* a count of forced tags is visible outside the inspector, with a
"release all" control. A tag left forced silently wins over a PLC that connects
later, and that is a genuinely confusing afternoon.
*Verify:* force three tags, connect a driver, see the warning and clear it.
*Size:* M. *Depends on:* UX-35.

---

### Phase 6 — Hold the line

**UX-42 — `--self-test=scenes`**
*Files:* new `engine/src/Sim/SceneTagSetSelfTest.cs`, `engine/src/Main.cs`, a
checked-in expectation file.
*Done when:* every manifest entry loads and its tag set (ids, types, directions)
matches the expectation — catching a template edit that renames a tag out from
under a mapping file.
*Verify:* rename a tag in a template; the test fails naming it.
*Size:* M. *Depends on:* UX-10, UX-13.

**UX-43 — Wire the exercises into `tools/test_plan.py`**
*Files:* `tools/test_plan.py`, `.github/workflows/test-plan.yml`.
*Done when:* all five scenes run headless in the same job that already covers
the sorting line. The spike proved templates simulate headless, so this needs no
display.
*Verify:* `python tools/test_plan.py --only H` passes locally and in CI.
*Size:* M. *Depends on:* UX-22.

**UX-44 — `--self-test=modes`**
*Files:* new `engine/src/Sim/ModeSelfTest.cs`, `engine/src/Main.cs`.
*Done when:* the Edit/Run contract is asserted **as a pair**: a click selects in
Edit and does not in Run, a control operates in Run and does not in Edit, and
entering Run clears the preview and the selection. `=click` and `=buttons` each
cover one half; nothing covers the switch.
*Verify:* run it; break `SetMode` and watch it fail.
*Size:* M. *Depends on:* UX-37.

**UX-45 — Cover non-bit forcing**
*Files:* new `tools/check_force_types.py`, `tools/test_plan.py`.
*Done when:* forcing an `int` and a `float` is asserted to reach the bus, the
way `check_force_while_paused.py` does for bits.
*Verify:* run it against an engine; then break `Force` and watch it fail.
*Size:* S. *Depends on:* UX-35.

**UX-46 — Document all of it**
*Files:* `docs/TEST_PLAN.md`, `README.md`, `docs/GETTING_STARTED.md`.
*Done when:* the new sections sit beside the existing `A`–`G` ones, and §5 of
this document has been folded into the user-facing docs rather than left in a
planning file.
*Verify:* a new user follows the docs and operates a component within five
minutes of first launch.
*Size:* S.
---

## 4. Per-scene exercise specification

The behaviour spec the C# profiles (UX-17…UX-20) and `try_scene.py` (UX-22)
share. Each is a small, honest control program — the same program a student
would be asked to write, which is the point: read it, then delete it and write
your own.

### 4.1 `sorting-by-height` — the reference line

**Already exists** as `tools/drive_engine.py`; fold it in behind the runner
rather than rewriting it.

*Drives:* `conveyor.rotate` on; `emitter.emit` pulsed at 1.5 s;
`pusher.extend` 0.9 s after `sensor_high.detect` rises, held 0.5 s.
*Asserts:* deterministic run gives exactly `tall=5 short=5` — the existing
E1/E2 contract, unchanged.

### 4.2 `start-stop-station` — momentary buttons and a latching E-stop

*Drives:* `panel.start` → `belt.rotate` + `tower.green`; `emitter.emit` pulsed;
`part_present.detect` rising edges counted into `produced.value`; `panel.stop`
→ belt off + `tower.yellow`; drop `panel.estop` → belt off + `tower.red`, and
refuse to restart until `panel.reset`.
*Asserts:* `counter.count` advances; the belt stops within 200 ms of the E-stop;
`panel.start` after an E-stop does **not** restart the belt; lamp state and belt
state never disagree.
*Teaches:* why `panel.estop` reads **true when healthy** — it is wired normally
closed, and a program that assumes "true means stopped" fails here loudly.

### 4.3 `tank-level-control` — the analog one

*Drives:* read `tank.level`, modulate `tank.fill` and `tank.drain` to hold a
setpoint, mirror the level into `level_readout.value`.
*Asserts:* level enters a ±5 % band around setpoint and stays for 10 s; then
**re-run the same controller at a low setpoint and show it behaves differently**
— outflow follows Torricelli, so process gain varies with level and a controller
tuned at 80 % overshoots at 20 %. That contrast *is* the lesson, so print both
settling times side by side.
*Teaches:* float tags end to end, and why one PID tuning is not enough.

### 4.4 `light-curtain-sorting` — sorting on a measurement

*Drives:* `belt.rotate` on, `emitter.emit` pulsed; read `height_gauge.height`
while `height_gauge.blocked`; compare against a threshold **declared at the top
as a named constant**; fire `diverter.extend` for tall boxes, handshaking on
`diverter.extended`/`.retracted` rather than on a timer.
*Asserts:* `tall_count.count + short_count.count` equals the number emitted —
nothing lost, nothing double-counted; no box is diverted whose measured height
was below threshold.
*Teaches:* the difference between two bits and one measurement — move a single
constant and the line re-sorts, with no rewiring.

### 4.5 `roller-line-weighing` — checkweighing and material

*Drives:* `infeed.rotate` and `scale.rotate` on; `emitter.emit` pulsed with metal
enabled; read `scale.weight` once a box settles; publish it to
`weight_readout.value`; read `metal_check.detect`.
*Asserts:* `scale.weight` is non-zero and stable (±1) while a box is on the
scale and returns to zero between boxes; `metal_check.detect` asserts for metal
cartons and **not** for cardboard — the material-aware sensing claim, checked
rather than asserted in a README; `outfeed.count` advances.
*Teaches:* an inductive sensor is not a second presence sensor.

---

## 5. Operating components by hand

### 5.1 What this should feel like

Click a conveyor. Turn it on. Watch it move.

That is the whole requirement, and it should need no PLC, no Python, no script,
and no knowledge of tag ids. It is how you find out what a part *is* — what a
light curtain reports, how a pusher's feedback bits sequence, how a tank's gain
changes with level — before you write a line of control code against it.

This is the specification for Phase 5. Everything in it is a statement about
what should be true, with the current state marked.

### 5.2 What you can do today, and why it is awkward

Three routes exist, and each stops short:

| Route | Reaches | Stops at |
|---|---|---|
| **Click it** — `F1` → Run mode, click the part | Control Panel caps only | `PressControlAt` ray-tests for `ButtonPanel` and nothing else *(§2.7 → UX-37)* |
| **Force it** — Tag Inspector → **Force** | any `bit` tag, input or output | silently does nothing on `int`/`float` *(§2.8 → UX-35)* |
| **Run the demo** — 🎬 Demo | `sorting-by-height` only | silent no-op on all four templates *(§2.1 → UX-16…UX-20)* |

So turning a conveyor on today means: know a conveyor owns a `.rotate` tag; know
the part's **Name** is the tag prefix; find `belt.rotate` in a 13–16 row list on
the other side of the screen; press Force there. **Three indirections between
the thing on screen and the switch that turns it on** — and the part's own
property panel, already open and already showing its settings, says nothing
about its I/O at all (§2.9).

Two things that are worth knowing and will stay true:

* **Forcing is sticky.** The button reads **UNFORCE** while a tag is held, and a
  tag left forced will beat a driver you connect later. UX-41 makes that
  visible.
* **Forcing works while paused.** Press `Space`, force a sensor, and the value
  still reaches a connected driver — that was FF-14, and
  `tools/check_force_while_paused.py` keeps it fixed.

Also worth knowing: forcing a **bit output** genuinely drives the machine. Parts
read forced values via `Tags.TryGetVisible`, so this is not a display trick —
the belt really turns.

### 5.3 Edit mode and Run mode

Mode changes **what a click means**, and nothing else. It does not start or stop
the simulation: physics, the emitter and the demo all run in Edit mode. Time is
controlled separately by `Space`, `Ctrl+R` and the 0.25×–4× rate selector.

| | Edit mode | Run mode |
|---|---|---|
| Left click | select a part, or place the palette part | operate the part *(today: Control Panel only)* |
| `M` / `R` / `Del` / `Ctrl+D` | move / rotate / delete / duplicate | — |
| `Ctrl+Z` / `Ctrl+Y` | undo / redo | — |
| `Ctrl+S` / `Ctrl+O` | save / open | **— silently, §2.7 → UX-40** |
| `F1` | → Run | → Edit |
| `Space`, `Ctrl+R`, rate selector | work | work |
| `C`, WASD, mouse orbit | work | work |
| Parts palette | shown | hidden |
| Physics, sensors, counters | running | running |

Entering Run mode cancels a placement preview and drops the selection; a move in
progress is cancelled the way `Escape` cancels it, so the part stays where it
started rather than being lost.

### 5.4 Component by component

What operating each part should do. **Today** is what works right now: ✓ works,
◐ works but only through the Tag Inspector by tag id, ✗ impossible.

Place the part, name it in the properties panel — the name is the tag prefix, so
a pusher named `reject` gives `reject.extend` — then:

#### Transport

| Part | Its I/O | Operating it should mean | Today |
|---|---|---|---|
| **Conveyor Belt** | `.rotate` bit out | A run/stop toggle on the part. Belt surface moves, boxes ride. Raise `speed` and boxes outrun a diverter | ◐ |
| **Roller Conveyor** | `.rotate` bit out | Same toggle; rollers spin at true surface speed, a box tracks without slipping | ◐ |
| **Weight Conveyor** | `.rotate` bit out · `.weight` int in | Run toggle, plus a live weight readout on the part — non-zero while a box sits on it, zero between | ◐ / readout ✗ |

#### Sensors

Sensor tags are **inputs the simulation owns** — you exercise them by making
something pass. An override is still worth having: it is how you ask "what does
my PLC do if this sensor is stuck on?"

| Part | Its I/O | Operating it should mean | Today |
|---|---|---|---|
| **Photoelectric** | `.detect` bit in | Live indicator on the part, plus a force-on/force-off override | ◐ |
| **Retroreflective** | `.detect` bit in | Same. Run it beside a photoelectric to see it catch matt boxes the diffuse one misses | ◐ |
| **Inductive** | `.detect` bit in | Same. Set the Emitter's `metal_every` to 1 then 0 and watch it react to metal only | ◐ |
| **Light Array** | `.height` float in · `.blocked` bit in | A live height readout on the part — the number changes per box, which is the whole point of the part | readout ✗ |

#### Actuators

| Part | Its I/O | Operating it should mean | Today |
|---|---|---|---|
| **Pneumatic Pusher** | `.extend` bit out · `.extended`, `.retracted` bit in | An extend/retract toggle, with both feedback bits shown. They flip in sequence and are never both true. Hold it out and the line backs up behind it | ◐ |
| **Ramp / Chute** | none — physical | Nothing to operate. Push a box onto it and lower `incline`; `incline` and `friction` are a matched pair, so boxes stop sliding | ✓ |

#### Process

| Part | Its I/O | Operating it should mean | Today |
|---|---|---|---|
| **Box Emitter** | `.emit` bit out | A "emit one box" button — one box per **rising** edge, so holding it true does not stream boxes. That behaviour is worth making obvious rather than discovering | ◐ |
| **Box Remover** | `.<count_tag>` int in | A live count on the part, and a reset | ◐ readout only |
| **Level Tank** | `.fill`, `.drain` float out · `.level` float in | **Two valve sliders and a level bar.** This is the part that most needs it and least has it | ✗ — both commands are floats *(§2.8)* |

#### Operator

| Part | Its I/O | Operating it should mean | Today |
|---|---|---|---|
| **Control Panel** | `.start`, `.stop`, `.reset`, `.estop` bit in · `.green`, `.red` bit out | `F1` → Run, click the caps. Start/Stop/Reset are **momentary** — one clean scan per click, however long you hold. The mushroom is **maintained**, wired normally closed, so `.estop` is **true while healthy** | ✓ |
| **Stack Light** | `.red`, `.yellow`, `.green` bit out | Click a stage to toggle it | ◐ |
| **Digital Display** | `.value` int out | Type a number and see it on the display | ✗ — `int` output *(§2.8)* |

The pattern is plain: **the one part built for direct operation works, and the
other fourteen are reachable only by tag id, or not at all.** UX-34 and UX-37
close that; UX-35 unblocks the two ✗ rows.

### 5.5 Two minutes with each scene

What you should be able to do on first launch. Steps marked **blocked** need
Phase 5.

**Sorting by height** — press **Watch it run**. Belt starts, boxes emit, tall
ones divert down the chute, `counter.tall` and `counter.short` climb. The one
path that works end to end today.

**Start / stop station** — Force `belt.rotate` and `emitter.emit`; watch
`part_present.detect` pulse and `counter.count` climb. `F1` → Run, press the
E-stop, and note `panel.estop` goes **false** because it is normally closed.
`produced.value` stays 0 *(blocked — int output)*.

**Tank level control** — **blocked entirely.** Nothing in this scene can be
driven by hand *(§2.8)*. After UX-35 it becomes the best scene in the set for
learning analog behaviour: open the fill valve halfway, watch the level rise and
the rate fall off as it climbs.

**Light curtain sorting** — Force `belt.rotate` and `emitter.emit`; watch
`height_gauge.height` change per box; Force `diverter.extend` by hand as a tall
one arrives and watch `tall_count.count` rise. The best scene *today* for
learning what forcing does, because you stand in for the PLC one box at a time.

**Roller line with weighing** — Force `infeed.rotate`, `scale.rotate` and
`emitter.emit`; watch `scale.weight` settle while a box sits on the scale and
return to zero after. Set `metal_every` to 1 and confirm `metal_check.detect`
follows metal only. `weight_readout.value` stays 0 *(blocked)*.

### 5.6 Checking the machinery, without the 3D app

How to check a build before blaming your PLC. All of this ships today:

| Command | Checks |
|---|---|
| `python tools/test_plan.py` | Everything below plus determinism; `--gui` adds the display-dependent click path |
| `python -m pytest -q` | The 71-test Python suite: tag model, protocol, Modbus, OPC UA, Siemens |
| `godot --headless --path engine -- --self-test=buttons` | Panel momentary and latching behaviour, from the tag side |
| `… --self-test=io` | Rename and I/O export |
| `… --self-test=scene` | Scene save/load round-trip, every part type |
| `… --self-test=templates` | Every shipped template loads and registers its I/O |
| `… --self-test=parity` | The C# and Python tag models agree |
| `… --self-test=layout` | The F5 modal still fits on screen |
| `godot --path engine -- --self-test=click` | A synthesized mouse click reaching a tag (needs a display) |
| `python tools/check_protocol.py` | `hello`/`describe`/`update` carry exactly the documented fields |
| `python tools/check_force_while_paused.py` | A forced input still reaches a driver while paused |
| `python examples/fake_plc.py` | A fake S7-1500 running the real SCL logic over OPC UA — sorting scene only |

Gaps this plan fills: nothing tests the two modes as a pair (UX-44), nothing
tests non-bit forcing (UX-45), and nothing covers four of the five scenes
(UX-43).

---

## 6. Decisions

### Settled

* **A packaged binary is in scope** — Phase 0, **Windows and Linux**. Both
  presets exist and CI already runs Linux headless, so the second target is
  close to free. macOS deferred (§7).
* **Per-scene exercises live in both C# and Python, C# first** — profiles repair
  the "Watch it run" button with zero install; `try_scene.py` adds the
  assertions and proves the engine↔sidecar seam. One shared spec (§4).
* **The four templates get no mapping files or PLC programs yet.** Exercises
  first; revisit when people are building against them (§7).
* **Order of work:** Phase 3 → Phase 5 → Phase 1 → Phase 2 → Phase 4 → Phase 6,
  with Phase 0 in parallel throughout.
* **Headless scene loading is viable** — settled by the spike in §0, not by
  argument. UX-10 is **S**, and Phase 2's exercises can run in Linux CI.
* **`--deterministic --scene=` — rejected outright (UX-12), not made
  compatible.** Making templates deterministic too would be a large piece of
  work buying exact-count assertions §4's band-based ones don't need.
  Revisit only if the band-based assertions prove flaky in practice.
* **UX-34 vs UX-37 — the panel leads.** UX-34 landed; UX-37 (click the part
  itself) is still open and remains complementary, not superseded.

### Still open

1. **UX-03 — how Python ships.** `PACKAGING.md` recommends
   PyInstaller-per-platform, but nothing has been tried. Best decided *after*
   UX-01 proves a binary can be produced at all.
2. **UX-09 — code signing.** Costs a certificate and a process; not signing
   costs every first-time user a SmartScreen warning. Needs deciding before the
   first public release, not before the first build.

---

## 7. Explicitly not in scope

* **macOS packaging** — no preset exists, and it cannot be tested from here.
  Deferred, not rejected; `SpawnInTerminal` already handles macOS terminals when
  someone picks it up.
* **Mapping files and PLC programs for the four templates** — decided against
  for now (§6). The sorting line keeps its `Sorting.scl`, `fake_plc.py` and three
  mappings; the templates get exercises (§4) and nothing PLC-side yet.
* **Rewriting the templates themselves** — their part layouts are fine; only the
  things that *drive* them are missing.
* **New parts, new drivers, new protocols.**
* **Any change to the tag bus wire protocol.**
* **Visual and 3D polish**, which is finished (FF-24…FF-26, FF-31).

---

## Appendix A — Work item index

`S` under half a day · `M` one to two days · `L` three to five days.

| # | Item | Phase | Size | Depends on | Status |
|---|---|---|---|---|---|
| UX-01 | Produce a Windows and a Linux binary at all | 0 | M | — |  |
| UX-02 | Correct `PACKAGING.md` with what happened | 0 | S | UX-01 |  |
| UX-03 | Decide and implement how Python ships | 0 | L | UX-01 |  |
| UX-04 | Make the engine find a bundled sidecar | 0 | M | UX-03 |  |
| UX-05 | Decide "one file" or "one folder" | 0 | S | — |  |
| UX-06 | Settle what ships alongside the binary | 0 | S | UX-03 |  |
| UX-07 | A release CI job | 0 | L | UX-01 |  |
| UX-08 | Fix the fixture path that breaks exported self-tests | 0 | S | UX-01 |  |
| UX-09 | Decide on code signing, or warn honestly | 0 | S / L | — |  |
| UX-10 | Load `--scene=` headless | 1 | S | — | done |
| UX-11 | Make `--scene` and `--demo` compose | 1 | S | — | done |
| UX-12 | Reject `--deterministic --scene=` | 1 | S | — | done |
| UX-13 | A scenes manifest | 1 | M | — | done |
| UX-14 | `--print-tags` | 1 | S | — | done |
| UX-15 | Report the loaded scene's real name at startup | 1 | S | — | done |
| UX-16 | A per-scene profile mechanism in `DemoDriver` | 2 | M | UX-13 | done |
| UX-17 | `start-stop-station` profile | 2 | S | UX-16 | done |
| UX-18 | `tank-level-control` profile | 2 | M | UX-16, UX-35 | done |
| UX-19 | `light-curtain-sorting` profile | 2 | M | UX-16 | done |
| UX-20 | `roller-line-weighing` profile | 2 | S | UX-16 | done |
| UX-21 | `tools/try_scene.py` | 2 | M | UX-10, UX-13 |  |
| UX-22 | Assertions for all five scenes | 2 | M | UX-21 |  |
| UX-23 | One cross-platform launcher | 3 | M | — | done |
| UX-24 | Purge the author's machine from the repo | 3 | S | — | done |
| UX-25 | Fix `demo` → `connect` in shipped material | 3 | S | — | done |
| UX-26 | Refresh stale facts | 3 | S | — | done |
| UX-27 | Launcher must not kill port 7411 silently | 3 | S | — | done |
| UX-28 | Launcher must not auto-start a driver | 3 | S | — | done |
| UX-29 | A Linux launcher | 3 | S | UX-23 | done |
| UX-30 | The Demo button refuses honestly | 4 | S | UX-16 | done |
| UX-31 | A "Try this scene" affordance | 4 | M | UX-21 |  |
| UX-32 | Keep "what this scene teaches" reachable | 4 | M | UX-13 | done |
| UX-33 | F5's empty state offers the exercise | 4 | S | UX-31 |  |
| UX-34 | Live I/O in the part property panel | 5 | L | UX-35 | done |
| UX-35 | Force any tag type, not just bits | 5 | M | — | done |
| UX-36 | Until UX-35 lands, refuse audibly | 5 | S | — | done |
| UX-37 | Click a component in Run mode to operate it | 5 | L | — |  |
| UX-38 | End the "Run" collision in the toolbar | 5 | S | — |  |
| UX-39 | Run mode explains itself | 5 | M | UX-37 |  |
| UX-40 | `Ctrl+S` and `Ctrl+O` survive Run mode | 5 | S | — |  |
| UX-41 | Show what is held by hand; release in one click | 5 | M | UX-35 |  |
| UX-42 | `--self-test=scenes` | 6 | M | UX-10, UX-13 |  |
| UX-43 | Wire the exercises into `tools/test_plan.py` | 6 | M | UX-22 |  |
| UX-44 | `--self-test=modes` | 6 | M | UX-37 |  |
| UX-45 | Cover non-bit forcing | 6 | S | UX-35 |  |
| UX-46 | Document all of it | 6 | S | — |  |

**Totals:** 46 items — 24 S, 17 M, 4 L, 1 S-or-L (UX-09). By phase: 0→9, 1→6, 2→7, 3→7, 4→4, 5→8, 6→5.
**Critical path to a first release:** UX-24 → UX-26 → UX-35 → UX-34 → UX-13 →
UX-10 → UX-16 → UX-17…UX-20, with UX-01 → UX-03 → UX-04 → UX-07 running beside
it.
