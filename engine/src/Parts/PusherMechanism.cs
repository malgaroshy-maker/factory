using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Pneumatic pusher mechanism with physics body and extend/retract limit switch state.
/// </summary>
public partial class PusherMechanism : Node3D
{
    [Export] public float StrokeLength { get; set; } = 0.35f;
    [Export] public float ExtendSpeed { get; set; } = 2.0f;

    private AnimatableBody3D _pusherHead = null!;
    private CollisionShape3D _collisionShape = null!;
    private MeshInstance3D _headMesh = null!;
    private float _currentExtension;

    public bool IsExtended => _currentExtension >= StrokeLength - 0.01f;
    public bool IsRetracted => _currentExtension <= 0.01f;

    public override void _Ready()
    {
        // 1. Static pneumatic housing
        var housing = IndustrialMeshBuilder.BuildPusherHousing();
        AddChild(housing);

        // 2. Animated physics piston head
        _pusherHead = new AnimatableBody3D { Name = "PusherHead" };
        var headSize = new Vector3(0.35f, 0.16f, 0.04f);

        var pistonVisual = IndustrialMeshBuilder.BuildPusherPistonHead(StrokeLength);
        _pusherHead.AddChild(pistonVisual);

        _collisionShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = headSize }
        };
        _pusherHead.AddChild(_collisionShape);

        AddChild(_pusherHead);
        _pusherHead.Position = new Vector3(0, 0, -0.10f);
    }

    public void UpdateExtension(bool extend, float delta)
    {
        float target = extend ? StrokeLength : 0.0f;
        _currentExtension = Mathf.MoveToward(_currentExtension, target, ExtendSpeed * delta);
        _pusherHead.Position = new Vector3(0, 0, -0.10f + _currentExtension);
    }
}
