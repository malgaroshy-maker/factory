using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Pneumatic pusher mechanism with physics body and extend/retract limit switch state.
///
/// Local space convention: the origin is the mounting point at the near edge of
/// the belt. The barrel sits behind it (-Z) and the rod strokes towards the belt
/// (+Z), so placing the node at the belt edge is all a scene needs to do. Never
/// place it at the belt centre — the barrel would straddle the lane.
/// </summary>
public partial class PusherMechanism : Node3D
{
    [Export] public float StrokeLength { get; set; } = 0.55f;
    [Export] public float ExtendSpeed { get; set; } = 1.6f;

    /// <summary>
    /// True when this part is only a *view* of a pusher the simulation already
    /// owns: it animates from the extend tag but never writes the limit-switch
    /// tags back, so there is exactly one writer per tag.
    /// </summary>
    [Export] public bool VisualOnly { get; set; }

    /// <summary>Rod left showing when fully retracted — a real cylinder never
    /// swallows its rod completely.</summary>
    private const float RestGap = 0.06f;
    private const float PlateThickness = 0.05f;
    private const float BarrelDepth = 0.30f;
    private const float BarrelHeight = 0.20f;

    /// <summary>Clearance between the belt surface and the bottom of the plate,
    /// so the plate sweeps the box without scraping the belt.</summary>
    private const float BeltClearance = 0.01f;

    /// <summary>Tallest carton the diverter is built to handle. The face plate is
    /// sized to it: a plate shorter than the carton contacts below the centre of
    /// mass and topples the box instead of sliding it, which is exactly what a
    /// 0.20 m plate did to a 0.30 m carton.</summary>
    [Export] public float MaxCartonHeight { get; set; } = 0.30f;

    private float PlateHeight => MaxCartonHeight - 2 * BeltClearance;

    /// <summary>Rod axis height relative to the part origin. Aligned with the
    /// centre of mass of a full-height carton, so the push is a pure translation
    /// with no tipping moment.</summary>
    private float AxisY => PartLayout.BeltSurface + MaxCartonHeight / 2.0f;

    private AnimatableBody3D _pusherHead = null!;
    private MeshInstance3D _rod = null!;
    private CylinderMesh _rodMesh = null!;
    private float _currentExtension;

    public bool IsExtended => _currentExtension >= StrokeLength - 0.01f;
    public bool IsRetracted => _currentExtension <= 0.01f;

    public override void _Ready()
    {
        // The pedestal reaches the floor from the cylinder axis, so the part
        // supports itself wherever it is dropped on the work plane.
        var housing = IndustrialMeshBuilder.BuildPusherHousing(
            BarrelDepth, BarrelHeight, PartLayout.FloorDrop + AxisY);
        housing.Position = new Vector3(0, AxisY, 0);
        AddChild(housing);

        _rod = IndustrialMeshBuilder.BuildPusherRod(out _rodMesh);
        AddChild(_rod);

        var plateSize = new Vector3(0.34f, PlateHeight, PlateThickness);
        _pusherHead = new AnimatableBody3D
        {
            Name = "PusherHead",
            // Move on the physics clock, so the plate transfers momentum to the
            // cartons it sweeps and genuinely blocks the ones it does not. With
            // this off the plate teleports between frames and boxes tunnel or
            // get flung.
            SyncToPhysics = true,
        };
        _pusherHead.AddChild(IndustrialMeshBuilder.BuildPusherFacePlate(plateSize));
        _pusherHead.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = plateSize }
        });
        AddChild(_pusherHead);

        ApplyExtension();
    }

    public void UpdateExtension(bool extend, float delta)
    {
        float target = extend ? StrokeLength : 0.0f;
        _currentExtension = Mathf.MoveToward(_currentExtension, target, ExtendSpeed * delta);
        ApplyExtension();
    }

    private void ApplyExtension()
    {
        float rodLength = RestGap + _currentExtension;
        _rodMesh.Height = rodLength;
        _rod.Position = new Vector3(0, AxisY, rodLength / 2.0f);
        _pusherHead.Position = new Vector3(0, AxisY, rodLength + PlateThickness / 2.0f);
    }
}
