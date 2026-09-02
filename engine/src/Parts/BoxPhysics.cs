using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Physics-enabled rigid body box for conveyor simulation.
/// Prevents jitter, clipping, or unexpected tumbling.
/// </summary>
public partial class BoxPhysics : RigidBody3D
{
    [Export] public bool IsTall { get; set; }

    /// <summary>
    /// Metal items read on an inductive sensor; cardboard does not. Giving items
    /// a material is what makes material-discriminating sensors mean anything —
    /// without it an inductive sensor is just a second diffuse sensor.
    /// </summary>
    [Export] public bool IsMetal { get; set; }

    // Dimensions match the headless SortingScene constants, so the physics scene
    // and the deterministic scene sort the same cartons.
    [Export] public float Length { get; set; } = 0.20f;
    [Export] public float Width { get; set; } = 0.24f;
    public float Height => IsTall ? 0.30f : 0.10f;

    /// <summary>Packed-carton density, kg/m³. A shipping carton of mixed goods
    /// runs 120–200; 150 puts the tall box at ~2.2 kg and the short at ~0.7 kg,
    /// which is what a belt of this size would actually carry.</summary>
    private const float CartonDensity = 150.0f;

    /// <summary>Hollow steel item, not solid billet — a solid one this size
    /// would weigh 100 kg and behave nothing like a handled part.</summary>
    private const float MetalDensity = 900.0f;

    private CollisionShape3D _collisionShape = null!;
    private MeshInstance3D _meshInstance = null!;
    private StandardMaterial3D _material = null!;

    private static readonly Color ShortColour = new(0.30f, 0.55f, 0.85f);
    private static readonly Color TallColour = new(0.95f, 0.55f, 0.15f);
    private static readonly Color MetalColour = new(0.72f, 0.74f, 0.78f);

    public override void _Ready()
    {
        var boxSize = new Vector3(Length, Height, Width);

        Mass = boxSize.X * boxSize.Y * boxSize.Z * (IsMetal ? MetalDensity : CartonDensity);
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
            // Friction is a property of the *pair* of surfaces, and the item's
            // material is already the thing this project models: an inductive
            // sensor exists here precisely because a steel item is not a
            // cardboard one. Giving both the same 0.55 made that distinction
            // stop at the sensor. Steel on a rubber belt slips noticeably more
            // than corrugated board does, which is why a metal item on a
            // sloped chute runs away and a carton walks down it.
            Friction = IsMetal ? 0.38f : 0.55f,
            Bounce = 0.0f,      // neither cartons nor handled steel bounce
            Rough = true,
        };

        _material = new StandardMaterial3D
        {
            AlbedoColor = IsMetal ? MetalColour : (IsTall ? TallColour : ShortColour),
            // Brushed steel, not chrome. At 0.75/0.30 under a 90%-sky ambient
            // the metal item reflected the sky hard enough to clip white and
            // then bloom, so it rendered as a glowing block rather than as a
            // steel one -- the exact failure IndustrialMeshBuilder's own
            // comments record for the belt rails and sensor posts, repeated
            // here because this file picked its numbers separately.
            Roughness = IsMetal ? 0.45f : 0.55f,
            Metallic = IsMetal ? 0.45f : 0.0f,
        };
        // A cardboard carton is the one object the eye follows across the
        // whole line, and a flat AlbedoColor reads as painted plastic. A
        // subtle multiplicative speckle (never darker than ~78% or brighter
        // than 100% of the base colour) breaks that up without touching
        // Metallic/Roughness — the steel items already had their own
        // reflectivity tuned carefully once (see IndustrialMeshBuilder) and
        // this deliberately leaves them alone. See FF-26.
        if (!IsMetal)
        {
            _material.AlbedoTexture = new NoiseTexture2D
            {
                Width = 64,
                Height = 64,
                Seamless = true,
                ColorRamp = new Gradient
                {
                    Offsets = new[] { 0.0f, 1.0f },
                    Colors = new[] { new Color(0.78f, 0.78f, 0.78f), new Color(1.0f, 1.0f, 1.0f) },
                },
                Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.35f },
            };
            _material.Uv1Scale = new Vector3(6.0f, 6.0f, 1.0f);
        }

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

        AddSurfaceDetail(boxSize);
    }

    /// <summary>
    /// A strip of packing tape down the top seam, or a pair of ribs on a steel
    /// item.
    ///
    /// The carton is the one object the eye follows the whole length of the
    /// line, and until now it was a flat-shaded rectangle: nothing on it said
    /// which way was up, so a box that tipped on the chute or was turned by a
    /// diverter looked exactly the same afterwards. One thin mesh fixes both —
    /// the box reads as a package, and its orientation is legible.
    /// </summary>
    private void AddSurfaceDetail(Vector3 boxSize)
    {
        if (IsMetal)
        {
            var ribMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.52f, 0.54f, 0.58f),
                Metallic = 0.50f,
                Roughness = 0.45f,
            };
            foreach (float z in new[] { -boxSize.Z * 0.28f, boxSize.Z * 0.28f })
            {
                AddChild(new MeshInstance3D
                {
                    Name = $"Rib{(z < 0 ? "N" : "F")}",
                    Mesh = new BoxMesh
                    {
                        Size = new Vector3(boxSize.X * 1.02f, boxSize.Y * 0.16f, 0.012f),
                    },
                    MaterialOverride = ribMat,
                    Position = new Vector3(0, 0, z),
                });
            }
            return;
        }

        var tapeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.86f, 0.82f, 0.70f),
            Roughness = 0.35f,
        };
        AddChild(new MeshInstance3D
        {
            Name = "TopTape",
            // Slightly proud of the lid so it never z-fights with it.
            Mesh = new BoxMesh { Size = new Vector3(boxSize.X * 1.005f, 0.004f, 0.035f) },
            MaterialOverride = tapeMat,
            Position = new Vector3(0, boxSize.Y / 2.0f, 0),
        });
    }
}
