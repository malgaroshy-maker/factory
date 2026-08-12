using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Physics-enabled rigid body box for conveyor simulation.
/// Prevents jitter, clipping, or unexpected tumbling.
/// </summary>
public partial class BoxPhysics : RigidBody3D
{
    [Export] public bool IsTall { get; set; }

    // Dimensions match the headless SortingScene constants, so the physics scene
    // and the deterministic scene sort the same cartons.
    [Export] public float Length { get; set; } = 0.20f;
    [Export] public float Width { get; set; } = 0.24f;
    public float Height => IsTall ? 0.30f : 0.10f;

    /// <summary>Packed-carton density, kg/m³. A shipping carton of mixed goods
    /// runs 120–200; 150 puts the tall box at ~2.2 kg and the short at ~0.7 kg,
    /// which is what a belt of this size would actually carry.</summary>
    private const float CartonDensity = 150.0f;

    private CollisionShape3D _collisionShape = null!;
    private MeshInstance3D _meshInstance = null!;
    private StandardMaterial3D _material = null!;

    private static readonly Color ShortColour = new(0.30f, 0.55f, 0.85f);
    private static readonly Color TallColour = new(0.95f, 0.55f, 0.15f);

    public override void _Ready()
    {
        var boxSize = new Vector3(Length, Height, Width);

        Mass = boxSize.X * boxSize.Y * boxSize.Z * CartonDensity;
        ContinuousCd = true;

        // Damping is air drag, not a stability crutch. The old 0.5 linear damp
        // fought the belt for grip and made boxes glide to a halt on the chute
        // as if through treacle; friction is what should stop a carton.
        LinearDamp = 0.05f;
        AngularDamp = 0.20f;

        // Rotation is left free: a tall carton shoved by the pusher is *supposed*
        // to be able to tip. Locking X/Z hid tumbling rather than fixing it, and
        // it also stopped boxes from rotating to face down the chute.
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = 0.55f,   // corrugated cardboard on rubber
            Bounce = 0.0f,      // cartons do not bounce
            Rough = true,
        };

        _material = new StandardMaterial3D
        {
            AlbedoColor = IsTall ? TallColour : ShortColour,
            Roughness = 0.55f,
        };

        _meshInstance = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = boxSize },
            MaterialOverride = _material,
        };
        AddChild(_meshInstance);

        _collisionShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = boxSize }
        };
        AddChild(_collisionShape);
    }
}
