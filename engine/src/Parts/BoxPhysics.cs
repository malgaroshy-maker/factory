using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Physics-enabled rigid body box for conveyor simulation.
/// Prevents jitter, clipping, or unexpected tumbling.
/// </summary>
public partial class BoxPhysics : RigidBody3D
{
    [Export] public bool IsTall { get; set; }
    [Export] public float Length { get; set; } = 0.30f;
    [Export] public float Width { get; set; } = 0.24f;
    public float Height => IsTall ? 0.30f : 0.15f;

    private CollisionShape3D _collisionShape = null!;
    private MeshInstance3D _meshInstance = null!;
    private StandardMaterial3D _material = null!;

    private static readonly Color ShortColour = new(0.30f, 0.55f, 0.85f);
    private static readonly Color TallColour = new(0.95f, 0.55f, 0.15f);

    public override void _Ready()
    {
        Mass = IsTall ? 1.5f : 1.0f;
        ContinuousCd = true;
        LinearDamp = 0.5f;
        AngularDamp = 1.0f;

        // Lock X and Z rotation to prevent box tumbling
        AxisLockAngularX = true;
        AxisLockAngularZ = true;

        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = 0.8f,
            Bounce = 0.0f,
            Rough = true,
        };

        var boxSize = new Vector3(Length, Height, Width);

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
