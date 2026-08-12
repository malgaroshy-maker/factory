using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// 3D 7-Segment Digital LED Display panel showing live integer tag values (e.g. box counts).
/// </summary>
public partial class DigitalDisplay : Node3D
{
    private Label3D _label3D = null!;
    private double _value;
    private bool _isAnalog;

    /// <summary>Integer reading, for the counter tags that drive it.</summary>
    [Export]
    public int Value
    {
        get => (int)System.Math.Round(_value);
        set { _value = value; _isAnalog = false; UpdateDisplay(); }
    }

    /// <summary>
    /// Analog reading. Separate from <see cref="Value"/> so the panel knows
    /// whether it shows a count or a measurement and formats accordingly — a
    /// level of 61.4 % rendered as "061" would be a lie.
    /// </summary>
    public double AnalogValue
    {
        get => _value;
        set { _value = value; _isAnalog = true; UpdateDisplay(); }
    }

    /// <summary>Unit suffix drawn after the reading, e.g. "%" or "mm".</summary>
    [Export] public string Unit { get; set; } = "";

    /// <summary>Panel centre above the work-plane origin, so the display stands
    /// at reading height on its own post instead of floating at belt level.</summary>
    private const float PanelY = 0.45f;

    public override void _Ready()
    {
        var postMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.31f, 0.33f),
            Metallic = 0.4f,
            Roughness = 0.5f,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Post",
            Mesh = new BoxMesh { Size = new Vector3(0.06f, PartLayout.FloorDrop + PanelY, 0.06f) },
            MaterialOverride = postMat,
            Position = new Vector3(0, PanelY / 2 - PartLayout.FloorDrop / 2, 0),
        });
        AddChild(new MeshInstance3D
        {
            Name = "BaseFoot",
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.03f, 0.22f) },
            MaterialOverride = postMat,
            Position = new Vector3(0, -PartLayout.FloorDrop, 0),
        });

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
            Position = new Vector3(0, PanelY, 0),
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
            Position = new Vector3(0, PanelY, 0.045f),
            MaterialOverride = screenMat,
        };
        AddChild(screen);

        // Glowing 3D LED Digital Label
        _label3D = new Label3D
        {
            Text = "000",
            Position = new Vector3(0, PanelY, 0.06f),
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
            string text = _isAnalog ? _value.ToString("0.0") : Value.ToString("D3");
            _label3D.Text = Unit.Length > 0 ? $"{text} {Unit}" : text;
        }
    }
}
