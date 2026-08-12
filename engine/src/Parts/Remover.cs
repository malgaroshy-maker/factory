using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Area3D zone that removes boxes when they reach the end of the conveyor or chute.
/// </summary>
public partial class Remover : Area3D
{
    [Export] public Vector3 ZoneSize { get; set; } = new(0.40f, 0.40f, 0.60f);

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

        BodyEntered += OnBodyEntered;
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
