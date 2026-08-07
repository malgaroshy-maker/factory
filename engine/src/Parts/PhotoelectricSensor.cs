using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Diffuse photoelectric sensor using RayCast3D to detect objects across the conveyor belt.
/// </summary>
public partial class PhotoelectricSensor : Node3D
{
    [Export] public float HeightAboveBelt { get; set; } = 0.10f;
    [Export] public float Range { get; set; } = 0.60f;
    [Export] public string SensorName { get; set; } = "Sensor";

    private RayCast3D _rayCast = null!;
    private MeshInstance3D _beamMesh = null!;
    private StandardMaterial3D _beamMaterial = null!;

    private static readonly Color BeamOff = new(0.35f, 0.10f, 0.10f);
    private static readonly Color BeamOn = new(1.0f, 0.15f, 0.15f);

    public bool IsDetected => _rayCast.IsColliding();

    public override void _Ready()
    {
        var visual = IndustrialMeshBuilder.BuildDetailedSensor(Range);
        AddChild(visual);
        _rayCast = new RayCast3D
        {
            TargetPosition = new Vector3(0, 0, -Range),
            Enabled = true,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        AddChild(_rayCast);

        _beamMaterial = new StandardMaterial3D
        {
            AlbedoColor = BeamOff,
            EmissionEnabled = true,
            Emission = BeamOff,
            EmissionEnergyMultiplier = 0.4f,
        };

        _beamMesh = new MeshInstance3D
        {
            Name = "BeamVisual",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.012f,
                BottomRadius = 0.012f,
                Height = Range,
            },
            Position = new Vector3(0, 0, -Range / 2.0f),
            MaterialOverride = _beamMaterial,
        };
        _beamMesh.RotateX(Mathf.Pi / 2);
        AddChild(_beamMesh);
    }

    public override void _Process(double delta)
    {
        bool detected = IsDetected;
        _beamMaterial.AlbedoColor = detected ? BeamOn : BeamOff;
        _beamMaterial.Emission = detected ? BeamOn : BeamOff;
        _beamMaterial.EmissionEnergyMultiplier = detected ? 3.0f : 0.4f;
    }
}
