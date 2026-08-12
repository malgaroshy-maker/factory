using System.Collections.Generic;
using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Driven roller conveyor. Transport works exactly as the belt does — a surface
/// velocity constraint — but the deck is a row of turning rollers instead of a
/// continuous band, which is what you actually use for pallets and totes that
/// would scuff a belt.
///
/// Inherits <see cref="ConveyorBelt"/> so it shares the speed, friction and
/// rotation handling, and so the editor drives it through the same tag.
/// </summary>
public partial class RollerConveyor : ConveyorBelt
{
    [Export] public float RollerSpacing { get; set; } = 0.12f;

    private const float RollerRadius = 0.035f;

    private readonly List<MeshInstance3D> _rollers = new();
    private float _spin;

    public override void _Ready()
    {
        base._Ready();

        // The inherited visual is a continuous band; hide it and lay rollers.
        var visual = GetNodeOrNull<Node3D>("ConveyorVisual");
        var band = visual?.GetNodeOrNull<MeshInstance3D>("BeltSurfaceMesh");
        if (band is not null) band.Visible = false;

        var rollerMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.68f, 0.70f, 0.74f),
            Metallic = 0.65f,
            Roughness = 0.30f,
        };

        int count = Mathf.Max((int)(Size.X / RollerSpacing), 2);
        float span = Size.Z - 0.08f;

        for (int i = 0; i < count; i++)
        {
            float x = -Size.X / 2 + RollerSpacing * (i + 0.5f);
            if (x > Size.X / 2) break;

            var roller = new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = RollerRadius,
                    BottomRadius = RollerRadius,
                    Height = span,
                },
                MaterialOverride = rollerMat,
                Position = new Vector3(x, Size.Y / 2 - 0.015f, 0),
            };
            // Cylinder axis across the lane, so it rolls the right way.
            roller.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
            AddChild(roller);
            _rollers.Add(roller);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsRunning) return;

        // Spin at the rate the surface actually moves, so the rollers and the
        // load agree rather than the rollers being decorative.
        _spin += Speed / RollerRadius * (float)delta;
        foreach (var roller in _rollers)
        {
            roller.Rotation = new Vector3(Mathf.Pi / 2, 0, -_spin);
        }
    }
}
