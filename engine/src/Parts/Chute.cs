using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Inclined physics ramp for gravity sliding of sorted boxes down to floor remover zones.
/// </summary>
public partial class Chute : StaticBody3D
{
    [Export] public Vector3 Size { get; set; } = new(0.5f, 0.04f, 0.6f);
    [Export] public float InclineAngleDegrees { get; set; } = 12.0f;

    public override void _Ready()
    {
        RotateX(Mathf.DegToRad(InclineAngleDegrees));

        var chuteMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.48f, 0.45f, 0.42f),
            Roughness = 0.85f,
            Metallic = 0.3f,
        };

        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Size },
            MaterialOverride = chuteMat,
        });

        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = Size }
        });
    }
}
