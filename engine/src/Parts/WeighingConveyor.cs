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

    /// <summary>The reading, on the scale (LE-08). Every other part that
    /// measures something shows it on itself — the light curtain's height, the
    /// tank's level, the display's number — and this one computed a weight and
    /// displayed it nowhere, so watching a carton land on the scale told you
    /// nothing without hunting its tag down in the inspector.</summary>
    private Label3D _readout = null!;

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

        // Same treatment as LevelTank's and LightArray's readouts: a large font
        // scaled right down, so it reads at working distance without becoming a
        // billboard across the scene.
        _readout = new Label3D
        {
            Text = "0 g",
            Position = new Vector3(0, Size.Y + 0.34f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 84,
            PixelSize = 0.0015f,
            Modulate = new Color(1.0f, 0.85f, 0.35f),
        };
        AddChild(_readout);

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
        // Only here, not every frame: the weight changes exactly when a carton
        // enters or leaves, and setting Label3D.Text rebuilds its glyph mesh.
        if (_readout is not null) _readout.Text = $"{MeasuredWeight:0} g";
    }
}
