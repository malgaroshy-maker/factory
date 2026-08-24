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

    /// <summary>
    /// The drive's own fault contact. A faulted drive does not turn, whatever
    /// the controller commands — which is the entire point of it (FI-01).
    ///
    /// Until this existed, every actuator in the library did exactly what it
    /// was told, so a command and reality could never disagree. That made half
    /// of real PLC work unteachable here: an interlock exists because the plant
    /// does not always obey, and a student who has only ever driven a line that
    /// always obeys has never had to check.
    /// </summary>
    public bool IsFaulted { get; private set; }

    private MeshInstance3D? _faultLamp;
    private StandardMaterial3D? _faultLampMat;

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

        BuildFaultLamp();
    }

    /// <summary>A beacon on the drive end, dark until the drive faults. On a
    /// real line this is the light on the motor starter, and it is there for
    /// the same reason it is here: a stopped belt looks identical to a faulted
    /// one, and the difference is the whole diagnosis.</summary>
    private void BuildFaultLamp()
    {
        _faultLampMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.06f, 0.06f),
            Metallic = 0.10f,
            Roughness = 0.35f,
        };
        // On the side of the head end, clear of the belt surface so a carton
        // never hides it, on a short stalk so it reads as mounted to the
        // frame rather than floating beside it.
        var mount = new Vector3(Size.X / 2 - 0.08f, Size.Y / 2 + 0.02f, Size.Z / 2 + 0.05f);
        const float stalk = 0.07f;

        AddChild(new MeshInstance3D
        {
            Name = "DriveFaultStalk",
            Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.010f, Height = stalk },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.22f, 0.23f, 0.25f),
                Metallic = 0.40f,
                Roughness = 0.50f,
            },
            Position = mount + new Vector3(0, stalk / 2, 0),
        });

        _faultLamp = new MeshInstance3D
        {
            Name = "DriveFaultLamp",
            Mesh = new SphereMesh { Radius = 0.042f, Height = 0.084f },
            MaterialOverride = _faultLampMat,
            Position = mount + new Vector3(0, stalk + 0.028f, 0),
        };
        AddChild(_faultLamp);
    }

    /// <summary>Raise or clear the drive fault. Nothing in the simulation sets
    /// this — it is an Input, driven by whoever is playing maintenance: the
    /// toolbar's fault tool, a forced tag, or a test.</summary>
    public void SetFaulted(bool faulted)
    {
        if (faulted == IsFaulted && _faultLampMat is not null) return;
        IsFaulted = faulted;

        if (_faultLampMat is null) return;
        _faultLampMat.AlbedoColor = faulted ? new Color(1.0f, 0.15f, 0.12f) : new Color(0.30f, 0.06f, 0.06f);
        _faultLampMat.EmissionEnabled = faulted;
        _faultLampMat.Emission = faulted ? new Color(1.0f, 0.15f, 0.12f) : Colors.Black;
        _faultLampMat.EmissionEnergyMultiplier = faulted ? 2.4f : 0.0f;
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
        // The fault wins over the command. Ordered before everything else so
        // there is exactly one place the two can disagree, and it resolves the
        // same way every time.
        if (IsFaulted) running = false;

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
