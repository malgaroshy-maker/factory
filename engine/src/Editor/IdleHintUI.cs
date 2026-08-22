using FactoryForge.Sim;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// A dismissible line telling a new user why nothing is moving, instead of
/// leaving a still factory to read as "this is broken" — the empirical
/// finding behind FF-23: two twelve-second runs of the shipped demo both
/// ended <c>tall=0 short=0</c> with nothing on screen saying why.
///
/// Shows after a few idle seconds with no driver connected, no demo running,
/// and nothing forced by hand — the three ways a scene is legitimately
/// "being driven" already, so this never appears over a session that is
/// actually working.
/// </summary>
public partial class IdleHintUI : Control
{
    public TagBusServer Bus { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;
    public DemoDriver? Demo { get; set; }

    private const double IdleDelaySeconds = 5.0;

    private PanelContainer _panel = null!;
    private double _idleFor;
    private bool _dismissed;

    public override void _Ready()
    {
        // CustomMinimumSize before the anchor preset, not after: KeepSize
        // positions the control using whatever Size it already has, and a
        // plain Control (unlike a Container) never inherits size from its
        // children — without this the panel was placed against a 0x0 rect
        // and its content overflowed off the bottom of the window.
        CustomMinimumSize = new Vector2(560, 56);
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.KeepSize, 28);

        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 10);
        _panel.AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        margin.AddChild(row);

        var label = new Label
        {
            Text = "No driver connected. Press F5 to connect a PLC, Force a tag in the "
                 + "inspector to drive it by hand, or press 🎬 Demo to watch it run.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(460, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(label);

        var dismiss = new Button
        {
            Text = " × ",
            Flat = true,
            TooltipText = "Dismiss for this session",
        };
        dismiss.Pressed += () => { _dismissed = true; _panel.Visible = false; };
        row.AddChild(dismiss);
    }

    public override void _Process(double delta)
    {
        if (_dismissed || Bus is null || Tags is null) return;

        bool beingDriven = Bus.HasClient || (Demo?.Active ?? false) || Tags.AnyForced;
        if (beingDriven)
        {
            _idleFor = 0;
            _panel.Visible = false;
            return;
        }

        _idleFor += delta;
        _panel.Visible = _idleFor >= IdleDelaySeconds;
    }
}
