using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Industrial 3-stage stack light post with dynamic indicator lamp emission glow.
/// </summary>
public partial class StackLight : Node3D
{
    private MeshInstance3D _greenLampMesh = null!;
    private MeshInstance3D _yellowLampMesh = null!;
    private MeshInstance3D _redLampMesh = null!;

    private StandardMaterial3D _greenMat = null!;
    private StandardMaterial3D _yellowMat = null!;
    private StandardMaterial3D _redMat = null!;

    public override void _Ready()
    {
        // Geometry is authored from the floor up; the part origin is on the work
        // plane like every other part, so the whole tower drops to stand on it.
        var column = new Node3D { Name = "Tower", Position = new Vector3(0, -PartLayout.FloorDrop, 0) };
        AddChild(column);

        var postMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.25f, 0.28f),
            Metallic = 0.8f,
            Roughness = 0.3f,
        };

        // Vertical post shaft
        var post = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.70f },
            Position = new Vector3(0, 0.35f, 0),
            MaterialOverride = postMat,
        };
        column.AddChild(post);

        // Lamps stack red-amber-green from the top, the industrial convention.
        // Green lamp dome (bottom)
        _greenMat = CreateLampMaterial(new Color(0.1f, 0.4f, 0.1f));
        _greenLampMesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.08f },
            Position = new Vector3(0, 0.45f, 0),
            MaterialOverride = _greenMat,
        };
        column.AddChild(_greenLampMesh);

        // Yellow lamp dome (middle)
        _yellowMat = CreateLampMaterial(new Color(0.4f, 0.35f, 0.1f));
        _yellowLampMesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.08f },
            Position = new Vector3(0, 0.55f, 0),
            MaterialOverride = _yellowMat,
        };
        column.AddChild(_yellowLampMesh);

        // Red lamp dome (top)
        _redMat = CreateLampMaterial(new Color(0.4f, 0.1f, 0.1f));
        _redLampMesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.08f },
            Position = new Vector3(0, 0.65f, 0),
            MaterialOverride = _redMat,
        };
        column.AddChild(_redLampMesh);
    }

    private static StandardMaterial3D CreateLampMaterial(Color baseColor)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = baseColor,
            EmissionEnabled = true,
            Emission = baseColor,
            EmissionEnergyMultiplier = 0.2f,
        };
    }

    public void SetGreenLamp(bool on)
    {
        if (_greenMat is null) return;
        Color color = on ? new Color(0.2f, 1.0f, 0.3f) : new Color(0.1f, 0.4f, 0.1f);
        _greenMat.AlbedoColor = color;
        _greenMat.Emission = color;
        _greenMat.EmissionEnergyMultiplier = on ? 3.0f : 0.2f;
    }

    public void SetYellowLamp(bool on)
    {
        if (_yellowMat is null) return;
        Color color = on ? new Color(1.0f, 0.9f, 0.2f) : new Color(0.4f, 0.35f, 0.1f);
        _yellowMat.AlbedoColor = color;
        _yellowMat.Emission = color;
        _yellowMat.EmissionEnergyMultiplier = on ? 3.0f : 0.2f;
    }

    public void SetRedLamp(bool on)
    {
        if (_redMat is null) return;
        Color color = on ? new Color(1.0f, 0.15f, 0.15f) : new Color(0.4f, 0.1f, 0.1f);
        _redMat.AlbedoColor = color;
        _redMat.Emission = color;
        _redMat.EmissionEnergyMultiplier = on ? 3.0f : 0.2f;
    }
}
