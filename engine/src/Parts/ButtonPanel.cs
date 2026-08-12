using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Industrial control panel containing push buttons and indicator lamps.
/// </summary>
public partial class ButtonPanel : Node3D
{
    [Signal] public delegate void ButtonPressedEventHandler(string name);

    private MeshInstance3D _greenLamp = null!;
    private MeshInstance3D _redLamp = null!;
    private StandardMaterial3D _greenMat = null!;
    private StandardMaterial3D _redMat = null!;

    public bool IsGreenOn { get; private set; }
    public bool IsRedOn { get; private set; }

    public override void _Ready()
    {
        var panelMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.26f, 0.28f),
            Metallic = 0.45f,
            Roughness = 0.45f,
        };

        // The origin is on the work plane like every other part, so the station
        // needs a pedestal down to the floor rather than hovering at belt height.
        AddChild(new MeshInstance3D
        {
            Name = "Pedestal",
            Mesh = new BoxMesh { Size = new Vector3(0.09f, PartLayout.FloorDrop, 0.09f) },
            MaterialOverride = panelMat,
            Position = new Vector3(0, -PartLayout.FloorDrop / 2, 0),
        });
        AddChild(new MeshInstance3D
        {
            Name = "BaseFoot",
            Mesh = new BoxMesh { Size = new Vector3(0.26f, 0.03f, 0.26f) },
            MaterialOverride = panelMat,
            Position = new Vector3(0, -PartLayout.FloorDrop, 0),
        });

        // Main housing box, sitting on top of the pedestal
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.45f, 0.12f) },
            MaterialOverride = panelMat,
            Position = new Vector3(0, 0.225f, 0),
        });

        // Green indicator lamp
        _greenMat = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.4f, 0.15f) };
        _greenLamp = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f },
            Position = new Vector3(-0.08f, 0.345f, 0.07f),
            MaterialOverride = _greenMat,
        };
        AddChild(_greenLamp);

        // Red indicator lamp
        _redMat = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.1f, 0.1f) };
        _redLamp = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f },
            Position = new Vector3(0.08f, 0.345f, 0.07f),
            MaterialOverride = _redMat,
        };
        AddChild(_redLamp);
    }

    public void SetGreenLamp(bool on)
    {
        IsGreenOn = on;
        _greenMat.AlbedoColor = on ? new Color(0.2f, 1.0f, 0.3f) : new Color(0.1f, 0.4f, 0.15f);
        _greenMat.EmissionEnabled = on;
        _greenMat.Emission = on ? new Color(0.2f, 1.0f, 0.3f) : Colors.Black;
        _greenMat.EmissionEnergyMultiplier = on ? 2.0f : 0.0f;
    }

    public void SetRedLamp(bool on)
    {
        IsRedOn = on;
        _redMat.AlbedoColor = on ? new Color(1.0f, 0.2f, 0.2f) : new Color(0.4f, 0.1f, 0.1f);
        _redMat.EmissionEnabled = on;
        _redMat.Emission = on ? new Color(1.0f, 0.2f, 0.2f) : Colors.Black;
        _redMat.EmissionEnergyMultiplier = on ? 2.0f : 0.0f;
    }
}
