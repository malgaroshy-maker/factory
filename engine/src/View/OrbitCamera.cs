using Godot;

namespace FactoryForge.View;

/// <summary>
/// Orbit camera: drag to rotate, wheel to zoom, middle-drag to pan.
/// Deliberately simple — the editor camera work belongs to M4.
/// </summary>
public partial class OrbitCamera : Camera3D
{
    [Export] public Vector3 Target { get; set; } = new(1.6f, 0.68f, 0.05f);
    [Export] public float Distance { get; set; } = 2.9f;
    [Export] public float Yaw { get; set; } = -0.62f;
    [Export] public float Pitch { get; set; } = -0.30f;

    private const float MinPitch = -1.45f;
    private const float MaxPitch = -0.05f;

    public override void _Ready() => Apply();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            if (Input.IsMouseButtonPressed(MouseButton.Left))
            {
                Yaw -= motion.Relative.X * 0.006f;
                Pitch = Mathf.Clamp(Pitch - motion.Relative.Y * 0.006f, MinPitch, MaxPitch);
                Apply();
            }
            else if (Input.IsMouseButtonPressed(MouseButton.Middle))
            {
                var right = Transform.Basis.X;
                var up = Transform.Basis.Y;
                Target -= (right * motion.Relative.X + up * -motion.Relative.Y) * Distance * 0.0015f;
                Apply();
            }
        }
        else if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp) Zoom(-0.4f);
            else if (button.ButtonIndex == MouseButton.WheelDown) Zoom(0.4f);
        }
    }

    private void Zoom(float amount)
    {
        Distance = Mathf.Clamp(Distance + amount, 1.0f, 20f);
        Apply();
    }

    private void Apply()
    {
        var offset = new Vector3(
            Mathf.Cos(Pitch) * Mathf.Sin(Yaw),
            -Mathf.Sin(Pitch),
            Mathf.Cos(Pitch) * Mathf.Cos(Yaw)) * Distance;
        Position = Target + offset;
        LookAt(Target, Vector3.Up);
    }
}
