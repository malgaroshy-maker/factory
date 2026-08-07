using System.Collections.Generic;
using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Conveyor belt with an integrated load cell scale that measures the total weight of boxes on the belt.
/// </summary>
public partial class WeighingConveyor : ConveyorBelt
{
    private Area3D _scaleArea = null!;
    private readonly HashSet<BoxPhysics> _boxesOnScale = new();

    public float MeasuredWeight { get; private set; }

    public override void _Ready()
    {
        base._Ready();

        // 3D scale frame indicator (yellow industrial weigh frame)
        var scaleMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.7f, 0.1f),
            Metallic = 0.5f,
        };

        var scaleFrame = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Size.X * 0.8f, 0.04f, Size.Z * 1.05f) },
            Position = new Vector3(0, -0.04f, 0),
            MaterialOverride = scaleMat,
        };
        AddChild(scaleFrame);

        // Weighing detection Area3D
        _scaleArea = new Area3D { Name = "ScaleArea" };
        var col = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(Size.X * 0.8f, 0.30f, Size.Z) },
            Position = new Vector3(0, 0.15f, 0),
        };
        _scaleArea.AddChild(col);
        AddChild(_scaleArea);

        _scaleArea.BodyEntered += OnBodyEntered;
        _scaleArea.BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is BoxPhysics box)
        {
            _boxesOnScale.Add(box);
            RecalculateWeight();
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (body is BoxPhysics box)
        {
            _boxesOnScale.Remove(box);
            RecalculateWeight();
        }
    }

    private void RecalculateWeight()
    {
        float total = 0f;
        foreach (var box in _boxesOnScale)
        {
            if (IsInstanceValid(box))
            {
                total += box.Mass * 10f; // Scale factor 10kg per mass unit
            }
        }
        MeasuredWeight = total;
    }
}
