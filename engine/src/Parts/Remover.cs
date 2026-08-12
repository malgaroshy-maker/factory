using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Area3D zone that removes boxes when they reach the end of the conveyor or chute.
/// </summary>
public partial class Remover : Area3D
{
    [Export] public Vector3 ZoneSize { get; set; } = new(0.40f, 0.40f, 0.60f);

    /// <summary>
    /// Tag this remover counts into. Empty means the conventional
    /// "&lt;instanceId&gt;.count"; the sorting scene points its two removers at
    /// counter.tall and counter.short so the same PLC program reads the same
    /// tags whichever scene is running.
    /// </summary>
    [Export] public string CountTag { get; set; } = "";

    private CollisionShape3D _collisionShape = null!;

    /// <summary>Boxes despawned since the scene started.</summary>
    public int RemovedCount { get; private set; }

    public override void _Ready()
    {
        _collisionShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = ZoneSize }
        };
        AddChild(_collisionShape);

        BuildMarker();
        BodyEntered += OnBodyEntered;
    }

    /// <summary>
    /// An outfeed gate marking the catch zone. The remover was a bare Area3D
    /// with a collision shape and nothing to look at, so placing it from the
    /// palette appeared to do nothing at all.
    /// </summary>
    private void BuildMarker()
    {
        var frameMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.57f, 0.60f),
            Metallic = 0.35f,
            Roughness = 0.50f,
        };
        var stripeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.90f, 0.35f, 0.10f),
            Roughness = 0.45f,
        };

        float halfZ = ZoneSize.Z / 2;
        float top = ZoneSize.Y / 2;

        foreach (int side in new[] { -1, 1 })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.05f, ZoneSize.Y, 0.05f) },
                MaterialOverride = frameMat,
                Position = new Vector3(0, 0, side * halfZ),
            });
        }

        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, ZoneSize.Z) },
            MaterialOverride = stripeMat,
            Position = new Vector3(0, top, 0),
        });
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is BoxPhysics box)
        {
            RemovedCount++;
            box.QueueFree();
        }
    }
}
