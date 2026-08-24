# 🛠️ FactoryForge Part Authoring Guide

This guide explains how to author custom 3D factory components for **FactoryForge**.

---

## 🏗️ Part Architecture Overview

Every factory component in FactoryForge is a Godot C# node located in `engine/src/Parts/`. Components read output tags written by the PLC (motors, solenoids, lamps) and update input tags read by the PLC (optical sensors, limit switches, encoders).

### Three rules that are not obvious

**1. A part's origin sits on the work plane.** `PartLayout.WorkPlaneY` (y = 0.5)
is the conveyor height, and it is where the scene editor drops every part. Your
part offsets its own geometry from there — legs reach *down* by
`PartLayout.FloorDrop`, anything that sits on a belt clears
`PartLayout.BeltSurface`. Never bake a mounting height into a scene position, or
the part will hover when someone places it from the palette.

**2. The instance id is a tag *prefix*, never a whole tag name.** The dispatch
appends the suffix, so a part registered as `"conveyor"` resolves
`conveyor.rotate`. Registering it as `"conveyor.rotate"` makes the lookup ask for
`conveyor.rotate.rotate`, which matches nothing — the part is placed, draws
correctly, and silently does nothing.

**3. Anything read in `_Ready` must also be saved.** Parts build their geometry
from their exported values, so a setting that is not in `PartProperties` is lost
the moment the scene is saved and reloaded. See Step 5.

---

## 📝 Step-by-Step Part Creation

### Step 1: Create the C# Component Class

Create a new C# file under `engine/src/Parts/CustomPart.cs`:

```csharp
using Godot;

namespace FactoryForge.Parts;

public partial class CustomPart : Node3D
{
    [Export] public float Speed { get; set; } = 1.0f;
    public bool IsActive { get; private set; }

    public override void _Ready()
    {
        // Add 3D visual meshes and collision shapes
        var meshInstance = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.4f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.6f, 0.9f) },
        };
        AddChild(meshInstance);
    }

    public void SetActive(bool active)
    {
        IsActive = active;
    }
}
```

---

### Step 2: Register Tags in `PartTagManager.cs`

Add tag definitions for your component in `engine/src/Editor/PartTagManager.cs`:

```csharp
case "CustomPart":
    tags.Add(new Tag($"{instanceId}.run", $"CustomPart {index} Run", TagType.Bit, TagKind.Output));
    tags.Add(new Tag($"{instanceId}.active", $"CustomPart {index} Active", TagType.Bit, TagKind.Input));
    break;
```

---

### Step 3: Add Palette Button in `PartPaletteUI.cs`

Add a button for your new part in `engine/src/Editor/PartPaletteUI.cs`:

```csharp
AddPaletteButton(mainBox, "Custom Part", "CustomPart");
```

---

### Step 4: Add Factory Case in `SceneEditor.cs`

Register creation and per-tick logic in `engine/src/Editor/SceneEditor.cs`:

```csharp
// In CreatePartNode:
"CustomPart" => new CustomPart(),

// In _PhysicsProcess — the physics clock, not the render frame. Parts that own
// a physics body must move on it, and `dt` there is scaled simulation time, so
// your part obeys pause and the time-scale control for free.
case "CustomPart":
    if (node is CustomPart custom && Tags.Contains($"{instanceId}.run"))
    {
        custom.SetActive((bool)Tags.Visible($"{instanceId}.run"));
        Tags.Set($"{instanceId}.active", custom.IsActive);
    }
    break;
```

---

### Step 5: Make Settings Survive Save/Load

A scene file stores position, rotation and a **properties map**. Add your
part's exported settings to both halves of `engine/src/Editor/PartProperties.cs`:

```csharp
// In Capture:
case CustomPart custom:
    p["speed"] = N(custom.Speed);
    break;

// In Apply:
case CustomPart custom:
    if (Num(props, "speed") is { } speed) custom.Speed = speed;
    break;
```

`Apply` runs *before* the node enters the tree, precisely because `_Ready` builds
geometry from these values. Skip this step and your part reloads at its defaults.

---

### Step 6: Expose Properties in the Inspector (optional)

Add sliders in `engine/src/Editor/PartPropertyInspectorUI.cs`. Every property
there must actually reach the simulation — if the value is only read when the
part is built, give your part a `Rebuild()` and call it from the setter, or the
slider will move and nothing will happen:

```csharp
else if (node is CustomPart custom)
{
    AddSliderProperty("Speed (m/s)", custom.Speed, 0.1f, 2.0f, 0.1f,
                      val => custom.Speed = val);
}
```

That rule is enforced, not just written down. **`--self-test=partsettings`
(C21) fails on a settings row it does not know how to drive**, so adding the
slider above without adding your part to `PartSettingsSelfTest` gives you:

```
FAIL  CustomPart: settings row 'Speed (m/s)' has no check in PartSettingsSelfTest
```

Add the row's label to that test's `Covered` map and a check that asserts a
**named observable** — what a person would see change — rather than that the
property was assigned, which is true by construction and proves nothing. The
check exists because `LightArray`'s "Curtain Height" slider shipped moving a
value the part only ever read while building itself: the slider moved and the
curtain did not.

C21 also fails a row wider than the panel's 260px content width. The panel is
fixed-width with horizontal scrolling off, so an over-wide row does not scroll —
it pushes the whole panel off the right of the screen. Keep labels short, and
watch controls that size themselves to their content: an `OptionButton` takes
the width of its longest *menu item* unless you set
`FitToLongestItem = false`.

---

## Step 7: Parts the operator can touch (optional)

Most parts only ever read tags. If yours has controls a human should be able to
click — buttons, selector switches, a hand valve — three extra things apply.
`engine/src/Parts/ButtonPanel.cs` is the worked example.

**Register the controls as `TagKind.Input`.** The kind is from the *controller's*
point of view: the operator drives the button, the controller reads it, so it is
an input exactly like a sensor.

**Hit-test your own geometry, not your bounding box.** Run mode calls into your
part rather than picking it first, because a part's box also covers its housing
and its pedestal — hit-testing that would make the whole station one big Start
button. Expose something like:

```csharp
public PanelButton? HitTest(Vector3 worldOrigin, Vector3 worldDirection)
{
    var toLocal = GlobalTransform.AffineInverse();
    Vector3 origin = toLocal * worldOrigin;
    Vector3 dir = (toLocal.Basis * worldDirection).Normalized();
    // ...test each control in local space
}
```

Take the ray into local space. A test written against world axes passes for an
unrotated part and misses every control once someone turns it.

**Decide momentary or maintained, and mean it.** A momentary contact is high for
*one scan*, not for as long as the mouse is down — clicks arrive on the frame
clock and tags are written on the physics clock, so queue presses in the part
and let the dispatch drain the queue, clearing the previous tick's pulse before
raising this tick's. A maintained control latches until it is clicked again.
Getting this wrong produces a button that looks fine and gives a PLC program a
rising edge of unpredictable width.

Finally: **the input path needs its own test.** Take the click position from the
`InputEventMouseButton`, never from `GetViewport().GetMousePosition()`, and
verify with `--self-test=click`. The logic can be entirely correct while
clicking does nothing. That is not hypothetical twice over: Run mode's dispatch
had exactly this bug, and so did Edit mode's selection, found only when a drag
needed to grab the part under the *press* rather than under wherever the cursor
had wandered.

---

## Step 8: Controls that are dragged, not clicked (optional)

The setpoint pot on `ButtonPanel` is the worked example, and it needed three
things a button does not.

**Its own hit test, separate from the buttons'.** A cap is pressed and a pot is
turned; a click that begins a drag must not also fire a button. Give the drag
target its own `HitTestDial`-style method and return early for it in the click
dispatch, or the enum's default value gets pressed every time somebody grabs the
knob.

**Join the shared hit test, not a private one.** `SceneEditor.FindOperableTarget`
is what both the click dispatch and the hover highlight ask, precisely so the two
can never disagree about what the cursor is over. A control reachable only
through its own second ray cast is a control the hover cannot know about — and
one nobody will find, because nothing on screen reacts as the mouse passes over
it. The cursor shape is the affordance for a drag; the outline is the affordance
for a click.

**Say so when the mode is entered.** Run mode's banner reads *"N parts respond to
a click"*, which is exactly the sentence that leaves a drag-only control
undiscovered. If your part has one, the banner has to mention it.

---

## Step 9: Parts that can fail (optional)

Anything with a drive should be able to break. Until every actuator could, a
command and reality could never disagree in this library — and an interlock
exists precisely because the plant does not always obey.

**Register a `.fault` tag as `TagKind.Input`.** Nothing in the simulation
computes it; it is raised by the toolbar's fault tool, a forced tag or a test,
and read by the controller exactly like a sensor. `SceneEditor.CanFault` derives
"can this break?" from the tag set rather than a second list, so registering the
tag is the whole opt-in.

**The fault must win over the command, in one place.** Resolve it at the top of
whatever applies the command:

```csharp
public void SetRunning(bool running)
{
    if (IsFaulted) running = false;   // one place the two can disagree
    // ...
}
```

**Fail where it stands, not where it is safe.** A jammed cylinder stops
mid-stroke and stays there; a seized valve holds its opening. "Returns home" and
"fails closed" are *safe* failures, and safe failures teach nothing — the whole
point is that the limit switches, or the process variable, become the only
honest thing to read.

**Show it.** A stopped machine and a faulted machine look identical, and the
difference is the whole diagnosis. Every faultable part carries a beacon on a
short stalk, dark until it lights.

**Watch what you name.** `--self-test=roller` took *"the first child whose mesh
is a `CylinderMesh`"* to mean "a roller", so a fault beacon mounted on a
cylindrical stalk became the first cylinder and the test started measuring a lamp
post for rotation. Name the meshes that matter, and look them up by name.
