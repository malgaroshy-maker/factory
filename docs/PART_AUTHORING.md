# 🛠️ FactoryForge Part Authoring Guide

This guide explains how to author custom 3D factory components for **FactoryForge**.

---

## 🏗️ Part Architecture Overview

Every factory component in FactoryForge is a Godot C# node located in `engine/src/Parts/`. Components read output tags written by the PLC (motors, solenoids, lamps) and update input tags read by the PLC (optical sensors, limit switches, encoders).

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

Register creation and tick update logic in `engine/src/Editor/SceneEditor.cs`:

```csharp
// In CreatePartNode:
"CustomPart" => new CustomPart(),

// In _Process:
case "CustomPart":
    if (node is CustomPart custom && Tags.Contains($"{instanceId}.run"))
    {
        custom.SetActive((bool)Tags.Visible($"{instanceId}.run"));
        Tags.Set($"{instanceId}.active", custom.IsActive);
    }
    break;
```
