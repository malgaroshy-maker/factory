using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// 3D 7-Segment Digital LED Display panel showing live integer tag values (e.g. box counts).
/// </summary>
public partial class DigitalDisplay : Node3D
{
    private Label3D _label3D = null!;
    private int _value;

    [Export]
    public int Value
    {
        get => _value;
        set
        {
            _value = value;
            UpdateDisplay();
        }
    }

    public override void _Ready()
    {
        // Dark industrial backing panel
        var housingMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.12f, 0.14f),
            Metallic = 0.8f,
            Roughness = 0.2f,
        };

        var housing = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.50f, 0.30f, 0.08f) },
            Position = new Vector3(0, 0.15f, 0),
            MaterialOverride = housingMat,
        };
        AddChild(housing);

        // Dark bezel screen
        var screenMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.05f),
        };
        var screen = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.42f, 0.22f, 0.02f) },
            Position = new Vector3(0, 0.15f, 0.045f),
            MaterialOverride = screenMat,
        };
        AddChild(screen);

        // Glowing 3D LED Digital Label
        _label3D = new Label3D
        {
            Text = "000",
            Position = new Vector3(0, 0.15f, 0.06f),
            Modulate = new Color(0.2f, 1.0f, 0.3f), // Bright green LED glow
            FontSize = 48,
            OutlineSize = 4,
            OutlineModulate = new Color(0, 0.2f, 0),
        };
        AddChild(_label3D);

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_label3D is not null)
        {
            _label3D.Text = _value.ToString("D3");
        }
    }
}
