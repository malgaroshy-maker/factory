using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Conveyor belt implemented using a surface velocity constraint (ConstantLinearVelocity)
/// on a StaticBody3D with animated tread surface texture scrolling.
/// </summary>
public partial class ConveyorBelt : StaticBody3D
{
    [Export] public float Speed { get; set; } = 0.5f;
    [Export] public Vector3 Direction { get; set; } = Vector3.Right;
    [Export] public Vector3 Size { get; set; } = new(3.0f, 0.12f, 0.5f);

    private CollisionShape3D _collisionShape = null!;
    private StandardMaterial3D? _beltMaterial;

    public bool IsRunning { get; private set; }

    public override void _Ready()
    {
        var visual = IndustrialMeshBuilder.BuildDetailedConveyor(Size, out _beltMaterial);
        AddChild(visual);

        _collisionShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = Size }
        };
        AddChild(_collisionShape);

        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = 0.9f,
            Rough = true,
        };
    }

    public override void _Process(double delta)
    {
        if (IsRunning && _beltMaterial is not null)
        {
            float dt = (float)delta;
            Vector3 offset = _beltMaterial.Uv1Offset;
            offset.X += Speed * dt * 0.8f;
            _beltMaterial.Uv1Offset = offset;
        }
    }

    public void SetRunning(bool running)
    {
        IsRunning = running;
        ConstantLinearVelocity = running ? Direction.Normalized() * Speed : Vector3.Zero;
    }
}
