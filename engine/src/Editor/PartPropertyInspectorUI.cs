using FactoryForge.Parts;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// UI Inspector panel for viewing and editing live properties of a selected 3D component.
/// </summary>
public partial class PartPropertyInspectorUI : Control
{
    private VBoxContainer _contentContainer = null!;
    private Node3D? _selectedNode;

    /// <summary>The editor that owns the selection, so the name field can act on
    /// it. Set by Main; null in headless builds, where the inspector never
    /// exists.</summary>
    public SceneEditor? Editor { get; set; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(280, 220);
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.KeepSize, 20);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(280, 220),
        };
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        panel.AddChild(margin);

        var mainBox = new VBoxContainer();
        margin.AddChild(mainBox);

        var title = new Label
        {
            Text = "PART PROPERTIES",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 14);
        mainBox.AddChild(title);

        _contentContainer = new VBoxContainer();
        mainBox.AddChild(_contentContainer);

        ShowNoSelection();
    }

    public void InspectNode(Node3D? node, string instanceId, string partType)
    {
        _selectedNode = node;
        foreach (var child in _contentContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (node is null)
        {
            ShowNoSelection();
            return;
        }

        var header = new Label { Text = partType };
        header.AddThemeFontSizeOverride("font_size", 13);
        _contentContainer.AddChild(header);

        AddNameRow(instanceId);

        // Every property here must actually reach the simulation. Anything whose
        // value is only read when the part is built needs a Rebuild() alongside
        // it, or the slider moves and nothing happens.
        if (node is WeighingConveyor weighBelt)
        {
            AddSliderProperty("Belt Speed (m/s)", weighBelt.Speed, 0.05f, 2.0f, 0.05f,
                              val => weighBelt.Speed = val);
        }
        else if (node is ConveyorBelt belt)
        {
            AddSliderProperty("Belt Speed (m/s)", belt.Speed, 0.05f, 2.0f, 0.05f,
                              val => belt.Speed = val);
            AddSliderProperty("Surface Friction", belt.SurfaceFriction, 0.05f, 1.5f, 0.05f,
                              val => belt.SurfaceFriction = val);
        }
        else if (node is PhotoelectricSensor sensor)
        {
            AddSliderProperty("Beam Range (m)", sensor.Range, 0.1f, 2.0f, 0.05f,
                              val => { sensor.Range = val; sensor.Rebuild(); });
            AddSliderProperty("Beam Height (m)", sensor.HeightAboveBelt, 0.01f, 0.6f, 0.01f,
                              val => { sensor.HeightAboveBelt = val; sensor.Rebuild(); });
        }
        else if (node is PusherMechanism pusher)
        {
            AddSliderProperty("Stroke Speed (m/s)", pusher.ExtendSpeed, 0.2f, 5.0f, 0.1f,
                              val => pusher.ExtendSpeed = val);
            AddSliderProperty("Stroke Length (m)", pusher.StrokeLength, 0.1f, 1.0f, 0.05f,
                              val => pusher.StrokeLength = val);
        }
        else if (node is LightArray curtain)
        {
            AddSliderProperty("Curtain Height (m)", curtain.CurtainHeight, 0.1f, 1.0f, 0.02f,
                              val => curtain.CurtainHeight = val);
        }
        else if (node is LevelTank tank)
        {
            AddSliderProperty("Fill Rate (%/s)", tank.FillRate, 1.0f, 60.0f, 1.0f,
                              val => tank.FillRate = val);
            AddSliderProperty("Drain Rate (%/s)", tank.DrainRate, 1.0f, 60.0f, 1.0f,
                              val => tank.DrainRate = val);
        }
        else if (node is Chute chute)
        {
            AddSliderProperty("Incline (deg)", chute.InclineAngleDegrees, 5.0f, 55.0f, 1.0f,
                              val => { chute.InclineAngleDegrees = val; chute.Rebuild(); });
            AddSliderProperty("Surface Friction", chute.SurfaceFriction, 0.02f, 1.0f, 0.02f,
                              val => { chute.SurfaceFriction = val; chute.Rebuild(); });
        }
    }

    /// <summary>
    /// The part's id, editable. This is the string that ends up in a mapping
    /// file and in the PLC program, so it is worth being able to write
    /// "reject_pusher" instead of living with "pushermechanism_2".
    ///
    /// Committing on Enter only, never on every keystroke: renaming per
    /// character would fire a rename for "r", "re", "rej"… each one moving the
    /// tags again.
    /// </summary>
    private void AddNameRow(string instanceId)
    {
        var row = new HBoxContainer();
        _contentContainer.AddChild(row);
        row.AddChild(new Label { Text = "Name", CustomMinimumSize = new Vector2(46, 0) });

        var field = new LineEdit
        {
            Text = instanceId,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Tag prefix for this part. Press Enter to apply.",
        };
        row.AddChild(field);

        var status = new Label { Text = "" };
        status.AddThemeFontSizeOverride("font_size", 11);
        _contentContainer.AddChild(status);

        field.TextSubmitted += (text) =>
        {
            if (Editor is null) return;

            if (Editor.TryRenameSelectedPart(text, out string problem))
            {
                status.AddThemeColorOverride("font_color", new Color(0.45f, 0.95f, 0.55f));
                status.Text = $"renamed to {text.Trim()}";
            }
            else
            {
                // Put the old name back, so the field never shows an id the
                // scene does not actually have.
                field.Text = instanceId;
                status.AddThemeColorOverride("font_color", new Color(1.0f, 0.55f, 0.45f));
                status.Text = problem;
            }
        };
    }

    private void ShowNoSelection()
    {
        foreach (var child in _contentContainer.GetChildren())
        {
            child.QueueFree();
        }
        // Wrapped, not clipped: the panel is anchored to the bottom-right, so an
        // unwrapped label wider than the panel pushes the whole thing off the
        // edge of the screen and takes its own last words with it.
        _contentContainer.AddChild(new Label
        {
            Text = "Click a placed part to inspect and rename it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(260, 0),
        });
    }

    private void AddSliderProperty(string labelText, float initialValue, float min, float max, float step, System.Action<float> onChanged)
    {
        var row = new HBoxContainer();
        _contentContainer.AddChild(row);

        var label = new Label { Text = labelText, CustomMinimumSize = new Vector2(140, 0) };
        row.AddChild(label);

        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = initialValue,
            CustomMinimumSize = new Vector2(90, 0),
        };
        spin.ValueChanged += (val) => { onChanged((float)val); Editor?.MarkDirty(); };
        row.AddChild(spin);
    }
}
