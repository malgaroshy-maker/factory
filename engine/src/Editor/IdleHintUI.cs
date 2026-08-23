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
    private const double RefusalDisplaySeconds = 5.0;
    private const string DefaultText =
        "No driver connected. Press F5 to connect a PLC, Force a tag in the "
        + "inspector to drive it by hand, or press 🎬 Demo to watch it run.";

    private PanelContainer _panel = null!;
    private Label _label = null!;
    private double _idleFor;
    private double _totalElapsed;
    private double _refusalUntil = -1;
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

        _label = new Label
        {
            Text = DefaultText,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(460, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(_label);

        var dismiss = new Button
        {
            Text = " × ",
            Flat = true,
            TooltipText = "Dismiss for this session",
        };
        dismiss.Pressed += () => { _dismissed = true; _panel.Visible = false; };
        row.AddChild(dismiss);

        // A distinct signal rather than polling RefusalReason for a change:
        // pressing Demo twice on the same broken scene has to say so twice,
        // and a value that only changes when the *reason* changes would stay
        // silent the second time (UX-30).
        if (Demo is not null) Demo.Refused += OnDemoRefused;
    }

    /// <summary>Interrupts the idle timer with the specific reason Demo just
    /// refused, for a few seconds, even if the idle hint was dismissed — this
    /// is a direct response to something the user just clicked, not an
    /// ambient nag they already dismissed.</summary>
    private void OnDemoRefused(string reason)
    {
        ShowInterrupt($"Demo can't run this scene: {reason}. Pick a template with a "
                     + "built-in exercise, or connect a real driver instead (F5).");
    }

    /// <summary>Entering Run mode says what is clickable, or says plainly that
    /// nothing is (UX-39, §2.7) -- Run mode used to be a mode that silently did
    /// nothing on a line built without a Control Panel, the same class of
    /// dishonesty FF-06/FF-23/UX-30 already closed elsewhere.</summary>
    public void ShowModeEnteredHint(int operableCount, string kinds)
    {
        ShowInterrupt(operableCount == 0
            ? "Nothing in this scene responds to a click yet. Add a conveyor, "
              + "pusher, panel or other part from the palette in Build mode."
            : $"{operableCount} part{(operableCount == 1 ? "" : "s")} respond to a "
              + $"click: {kinds}. Hover to see which.");
    }

    /// <summary>Generic hook for anything else that needs to interrupt the
    /// idle hint with a message for a few seconds — currently the toolbar's
    /// "Try this scene" button (UX-31), for a scene with no matching exercise
    /// or a python launch failure.</summary>
    public void Announce(string text) => ShowInterrupt(text);

    /// <summary>Force the hint visible with specific text for a few seconds,
    /// even if the ambient idle nag was already dismissed — this is a direct
    /// response to something the user just did, not an ambient nag they
    /// already dismissed.</summary>
    private void ShowInterrupt(string text)
    {
        _label.Text = text;
        _panel.Visible = true;
        _refusalUntil = _totalElapsed + RefusalDisplaySeconds;
    }

    public override void _Process(double delta)
    {
        _totalElapsed += delta;

        if (_refusalUntil >= 0)
        {
            if (_totalElapsed < _refusalUntil) return;
            _refusalUntil = -1;
            _label.Text = DefaultText;
            _panel.Visible = false;
            _idleFor = 0;
        }

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
