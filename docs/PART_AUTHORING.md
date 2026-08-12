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
clicking does nothing.
