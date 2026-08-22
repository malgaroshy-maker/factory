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

    private bool _hasAppliedVelocity;
    private bool _lastRunning;
    private Basis _lastBasis;
    private float _lastSpeed;

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

    /// <summary>
    /// Called every physics tick regardless of whether anything changed, so
    /// this used to redo a matrix multiply and a square root on every belt on
    /// every tick even when the belt was sitting there doing nothing (FF-16).
    /// Skipping when (running, orientation, speed) all match the last applied
    /// values makes the steady-state case free while still catching the case
    /// that made this call unconditional in the first place: rotating a
    /// running belt has to rotate its transport direction with it, and a live
    /// speed-slider edit has to take effect without a rotate to trigger it.
    /// </summary>
    public void SetRunning(bool running)
    {
        IsRunning = running;
        Basis basis = GlobalBasis;

        if (_hasAppliedVelocity && running == _lastRunning && basis == _lastBasis
            && Mathf.IsEqualApprox(Speed, _lastSpeed))
        {
            return;
        }

        // ConstantLinearVelocity is a world-space vector, but Direction describes
        // the belt's own travel. Rotating a belt in the editor (R) has to rotate
        // the transport with it, or a turned belt still drives boxes down +X.
        Vector3 worldDir = (basis * Direction).Normalized();
        ConstantLinearVelocity = running ? worldDir * Speed : Vector3.Zero;

        _lastRunning = running;
        _lastBasis = basis;
        _lastSpeed = Speed;
        _hasAppliedVelocity = true;
    }
}
