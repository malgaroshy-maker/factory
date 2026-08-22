using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Part palette UI panel allowing users to select factory components for placement.
/// </summary>
public partial class PartPaletteUI : Control
{
    [Signal] public delegate void PartSelectedEventHandler(string partType);

    /// <summary>Hide the palette while the line is running. Offering parts you
    /// cannot place would be a menu of dead buttons, and it frees the left of
    /// the screen for the machine you are actually operating.</summary>
    public void ShowForMode(bool running) => Visible = !running;

    public override void _Ready()
    {
        // Anchored to stretch with the window's height, not a fixed pixel
        // count: fifteen buttons at 36px plus headings never fit in the old
        // 350px minimum, and Godot's own default window (1152x648, before
        // FF-32) ran them off the bottom entirely with no way to scroll to
        // the rest. Tracking the viewport means this stays correct at any
        // window size instead of needing a second hand-picked constant.
        AnchorLeft = 0; AnchorTop = 0; AnchorRight = 0; AnchorBottom = 1;
        OffsetLeft = 20; OffsetTop = 20; OffsetRight = 220; OffsetBottom = -20;

        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        panel.AddChild(margin);

        var mainBox = new VBoxContainer();
        margin.AddChild(mainBox);

        var title = new Label
        {
            Text = "PARTS PALETTE",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 16);
        mainBox.AddChild(title);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        mainBox.AddChild(scroll);

        var groups = new VBoxContainer();
        groups.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(groups);

        // Grouped rather than one flat list of fifteen: the roadmap plans
        // more parts as contributors ask for them, which only makes a flat
        // list harder to scan over time.
        AddGroup(groups, "TRANSPORT",
            ("Conveyor Belt", "ConveyorBelt"),
            ("Weight Conveyor", "WeighingConveyor"),
            ("Roller Conveyor", "RollerConveyor"));

        AddGroup(groups, "SENSORS",
            ("Photoelectric Sensor", "PhotoelectricSensor"),
            ("Retroreflective Sensor", "RetroreflectiveSensor"),
            ("Inductive Sensor", "InductiveSensor"),
            ("Light Array", "LightArray"));

        AddGroup(groups, "ACTUATORS",
            ("Pneumatic Pusher", "PusherMechanism"),
            ("Ramp (Chute)", "Chute"));

        AddGroup(groups, "PROCESS",
            ("Box Emitter", "Emitter"),
            ("Box Remover", "Remover"),
            ("Level Tank", "LevelTank"));

        AddGroup(groups, "OPERATOR",
            ("Control Panel", "ButtonPanel"),
            ("Stack Light", "StackLight"),
            ("Digital Display", "DigitalDisplay"));
    }

    private void AddGroup(VBoxContainer parent, string title, params (string Label, string PartType)[] parts)
    {
        var header = new Button
        {
            Text = $"▾ {title}",
            Alignment = HorizontalAlignment.Left,
            Flat = true,
            FocusMode = FocusModeEnum.None,
        };
        header.AddThemeFontSizeOverride("font_size", 12);
        header.AddThemeColorOverride("font_color", new Color(0.98f, 0.80f, 0.35f));
        parent.AddChild(header);

        var body = new VBoxContainer();
        parent.AddChild(body);
        foreach (var (label, partType) in parts) AddPaletteButton(body, label, partType);

        header.Pressed += () =>
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾ " : "▸ ") + title;
        };
    }

    private void AddPaletteButton(VBoxContainer container, string labelText, string partType)
    {
        var button = new Button
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(160, 36),
        };
        button.Pressed += () => EmitSignal(SignalName.PartSelected, partType);
        container.AddChild(button);
    }
}
