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

    public BoxPhysics SpawnBox(bool isTall)
    {
        var box = new BoxPhysics { IsTall = isTall };
        GetParent()?.AddChild(box);

        // Height is only known once IsTall is set, so the resting position is
        // computed here rather than baked into a fixed offset.
        float centreY = PartLayout.BeltSurface + DropClearance + box.Height / 2.0f;
        box.GlobalPosition = GlobalPosition + new Vector3(0, centreY, 0);
        return box;
    }
}
