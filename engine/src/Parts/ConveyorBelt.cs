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

    /// <summary>
    /// Rubber belt against cardboard. High enough to accelerate a carton up to
    /// line speed without slip, low enough that a blocked box scuffs along
    /// instead of being welded to the surface. Applied live, so changing it in
    /// the inspector re-grips the belt immediately.
    /// </summary>
    [Export]
    public float SurfaceFriction
    {
        get => _surfaceFriction;
        set
        {
            _surfaceFriction = value;
            if (PhysicsMaterialOverride is { } material) material.Friction = value;
        }
    }

    private float _surfaceFriction = 0.70f;

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
            Friction = _surfaceFriction,
            Bounce = 0.0f,
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
        // ConstantLinearVelocity is a world-space vector, but Direction describes
        // the belt's own travel. Rotating a belt in the editor (R) has to rotate
        // the transport with it, or a turned belt still drives boxes down +X.
        Vector3 worldDir = (GlobalBasis * Direction).Normalized();
        ConstantLinearVelocity = running ? worldDir * Speed : Vector3.Zero;
    }
}
