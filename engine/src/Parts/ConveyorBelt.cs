using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Conveyor belt implemented using a surface velocity constraint (ConstantLinearVelocity)
/// on a StaticBody3D, rather than simple friction.
/// </summary>
public partial class ConveyorBelt : StaticBody3D
{
    [Export] public float Speed { get; set; } = 0.5f;
    [Export] public Vector3 Direction { get; set; } = Vector3.Right;
    [Export] public Vector3 Size { get; set; } = new(3.0f, 0.12f, 0.5f);

    private CollisionShape3D _collisionShape = null!;
    private MeshInstance3D _meshInstance = null!;
    private StandardMaterial3D _material = null!;

    public bool IsRunning { get; private set; }

    public override void _Ready()
    {
        var visual = IndustrialMeshBuilder.BuildDetailedConveyor(Size);
        AddChild(visual);

        _collisionShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = Size }
        };
        AddChild(_collisionShape);

        // Physics material with high friction for surface-velocity transfer
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = 0.9f,
            Rough = true,
        };
    }

    public void SetRunning(bool running)
    {
        IsRunning = running;
        ConstantLinearVelocity = running ? Direction.Normalized() * Speed : Vector3.Zero;
    }
}
