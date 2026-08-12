using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Spawns BoxPhysics instances onto the conveyor belt when triggered.
/// </summary>
public partial class Emitter : Node3D
{
    /// <summary>Gap between the belt surface and the underside of a new box.
    /// Boxes are *placed* on the belt, not dropped onto it: the old 0.20 m drop
    /// made every carton land, bounce and rock before it settled.</summary>
    [Export] public float DropClearance { get; set; } = 0.002f;

    /// <summary>
    /// A feed gantry straddling the belt, so the emitter is something you can
    /// see and select. It had no mesh at all: placing "Box Emitter" from the
    /// palette put an invisible node in the scene.
    /// </summary>
    public override void _Ready()
    {
        const float span = 0.62f;
        const float legHeight = PartLayout.FloorDrop + 0.42f;

        var frameMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.57f, 0.60f),
            Metallic = 0.35f,
            Roughness = 0.50f,
        };
        var spoutMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.75f, 0.10f),
            Roughness = 0.40f,
        };

        foreach (int side in new[] { -1, 1 })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.05f, legHeight, 0.05f) },
                MaterialOverride = frameMat,
                Position = new Vector3(0, 0.42f - legHeight / 2, side * span / 2),
            });
        }

        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.05f, span) },
            MaterialOverride = frameMat,
            Position = new Vector3(0, 0.42f, 0),
        });

        // Discharge spout the cartons appear from.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.16f, 0.30f) },
            MaterialOverride = spoutMat,
            Position = new Vector3(0, 0.30f, 0),
        });
    }

    /// <summary>Every Nth item is metal, so a material-sensing scene has
    /// something to sort. 0 disables it, which is the default and leaves
    /// existing scenes emitting cardboard only.</summary>
    [Export] public int MetalEvery { get; set; }

    private int _emitted;

    public BoxPhysics SpawnBox(bool isTall)
    {
        _emitted++;
        bool metal = MetalEvery > 0 && _emitted % MetalEvery == 0;
        var box = new BoxPhysics { IsTall = isTall, IsMetal = metal };
        GetParent()?.AddChild(box);

        // Height is only known once IsTall is set, so the resting position is
        // computed here rather than baked into a fixed offset.
        float centreY = PartLayout.BeltSurface + DropClearance + box.Height / 2.0f;
        box.GlobalPosition = GlobalPosition + new Vector3(0, centreY, 0);
        return box;
    }
}
