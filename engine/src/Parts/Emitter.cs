using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Spawns BoxPhysics instances onto the conveyor belt when triggered.
/// </summary>
public partial class Emitter : Node3D
{
    [Export] public Vector3 SpawnOffset { get; set; } = new(0, 0.20f, 0);

    public BoxPhysics SpawnBox(bool isTall)
    {
        var box = new BoxPhysics
        {
            IsTall = isTall,
            GlobalPosition = GlobalPosition + SpawnOffset,
        };
        GetParent()?.AddChild(box);
        return box;
    }
}
