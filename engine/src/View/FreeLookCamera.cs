using Godot;

namespace FactoryForge.View;

/// <summary>
/// Free-look / fly camera: WASD for movement, space/shift for height/speed,
/// right-click + mouse motion to look around.
/// </summary>
public partial class FreeLookCamera : Camera3D
{
    [Export] public float MoveSpeed { get; set; } = 4.0f;
    [Export] public float FastMoveMultiplier { get; set; } = 2.5f;
    [Export] public float MouseSensitivity { get; set; } = 0.003f;

    private float _yaw;
    private float _pitch;
    private bool _isLooking;

    public override void _Ready()
    {
        SyncRotationFromTransform();
    }

    public void SyncRotationFromTransform()
    {
        var euler = GlobalRotation;
        _yaw = euler.Y;
        _pitch = euler.X;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                _isLooking = mouseButton.Pressed;
                Input.MouseMode = _isLooking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
        }
        else if (@event is InputEventMouseMotion motion && _isLooking)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -Mathf.Pi / 2.05f, Mathf.Pi / 2.05f);
            Rotation = new Vector3(_pitch, _yaw, 0);
        }
    }

    public override void _Process(double delta)
    {
        if (!Current) return;

        float speed = MoveSpeed * (Input.IsKeyPressed(Key.Shift) ? FastMoveMultiplier : 1.0f);
        Vector3 dir = Vector3.Zero;

        if (Input.IsKeyPressed(Key.W)) dir -= Transform.Basis.Z;
        if (Input.IsKeyPressed(Key.S)) dir += Transform.Basis.Z;
        if (Input.IsKeyPressed(Key.A)) dir -= Transform.Basis.X;
        if (Input.IsKeyPressed(Key.D)) dir += Transform.Basis.X;
        if (Input.IsKeyPressed(Key.E) || Input.IsKeyPressed(Key.Space)) dir += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) dir -= Vector3.Up;

        if (dir.LengthSquared() > 0)
        {
            dir = dir.Normalized();
            Position += dir * speed * (float)delta;
        }
    }
}
