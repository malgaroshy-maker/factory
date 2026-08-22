using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Drive the part property panel's live I/O controls (UX-34) the way a click
/// and a drag would, and check the tag table actually changes.
///
/// <code>godot --headless --path engine -- --self-test=proppanel</code>
///
/// This is the panel that used to say nothing about a part's I/O at all
/// (§2.9) -- three indirections stood between the thing on screen and the
/// switch that turned it on. Loads the tank scene specifically because it
/// alone exercises every combination this panel has to get right: a bit
/// output (panel.green), an int output (level_readout.value), a float output
/// (tank.fill), and a bit input's override (panel.estop).
/// </summary>
public partial class PartPropertyPanelSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    private readonly List<string> _failures = new();
    private int _step;
    private PartPropertyInspectorUI _panel = null!;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        if (_step == 1)
        {
            Editor.LoadTemplate("res://templates/tank_level_control.json");
            return;
        }

        if (_step != 3) return;

        // Windowed (this self-test also doubles as a way to put real
        // controls on the real on-screen panel for a screenshot): reuse the
        // panel Main already built rather than a second, invisible one.
        _panel = Editor.PropertyInspector;
        if (_panel is null)
        {
            _panel = new PartPropertyInspectorUI { Name = "TestPropertyInspector", Editor = Editor };
            AddChild(_panel);   // _Ready() builds the base panel synchronously
        }

        Node3D? tank = null, panel = null, readout = null;
        foreach (var child in GetParent().GetChildren())
        {
            if (child is LevelTank t) tank = t;
            else if (child is ButtonPanel p) panel = p;
            else if (child is DigitalDisplay d) readout = d;
        }
        if (tank is null || panel is null || readout is null)
        {
            Expect(false, "tank-level-control: not every expected part was found after loading");
            Finish();
            return;
        }

        CheckFloatOutput(tank);
        CheckIntOutput(readout);
        CheckBitOutput(panel);
        CheckBitInputOverride(panel);

        Finish();
    }

    private void CheckFloatOutput(Node3D tank)
    {
        _panel.InspectNode(tank, "tank", "LevelTank");
        var slider = FindControl<HSlider>("tank.fill");
        if (slider is null) { Expect(false, "tank.fill: no slider in the panel"); return; }

        slider.Value = 42.0;
        Expect(Tags.IsForced("tank.fill"), "tank.fill: dragging the slider forces the tag");
        Expect(System.Math.Abs(System.Convert.ToDouble(Tags.Visible("tank.fill")) - 42.0) < 0.01,
               $"tank.fill: expected 42, got {Tags.Visible("tank.fill")}");
    }

    private void CheckIntOutput(Node3D readout)
    {
        _panel.InspectNode(readout, "level_readout", "DigitalDisplay");
        var spin = FindControl<SpinBox>("level_readout.value");
        if (spin is null) { Expect(false, "level_readout.value: no spin box in the panel"); return; }

        spin.Value = 77;
        Expect(Tags.IsForced("level_readout.value"), "level_readout.value: the spin box forces the tag");
        Expect(Equals(Tags.Visible("level_readout.value"), 77),
               $"level_readout.value: expected 77, got {Tags.Visible("level_readout.value")}");
    }

    private void CheckBitOutput(Node3D panel)
    {
        _panel.InspectNode(panel, "panel", "ButtonPanel");
        var toggle = FindControl<CheckButton>("panel.green");
        if (toggle is null) { Expect(false, "panel.green: no toggle in the panel"); return; }

        bool before = toggle.ButtonPressed;
        toggle.EmitSignal(BaseButton.SignalName.Toggled, !before);
        Expect(Tags.IsForced("panel.green"), "panel.green: the toggle forces the tag");
        Expect(Equals(Tags.Visible("panel.green"), !before),
               $"panel.green: expected {!before}, got {Tags.Visible("panel.green")}");
    }

    private void CheckBitInputOverride(Node3D panel)
    {
        _panel.InspectNode(panel, "panel", "ButtonPanel");
        var overrideBox = FindOverrideCheckbox("panel.estop");
        if (overrideBox is null) { Expect(false, "panel.estop: no Override checkbox in the panel"); return; }

        Expect(!Tags.IsForced("panel.estop"), "panel.estop: not forced before the override is switched on");

        overrideBox.EmitSignal(BaseButton.SignalName.Toggled, true);
        Expect(Tags.IsForced("panel.estop"), "panel.estop: switching Override on forces the tag");

        overrideBox.EmitSignal(BaseButton.SignalName.Toggled, false);
        Expect(!Tags.IsForced("panel.estop"), "panel.estop: switching Override off clears the force");
    }

    /// <summary>Find a tag's row by the tooltip its name label carries, then
    /// return the control of type T in that same row -- the way a user's eye
    /// would find it, not a test-only accessor into the panel's private state.</summary>
    private T? FindControl<T>(string tagId) where T : Control
    {
        var row = FindRow(_panel, tagId);
        if (row is null) return null;
        foreach (var child in row.GetChildren())
        {
            if (child is T match) return match;
        }
        return null;
    }

    private CheckBox? FindOverrideCheckbox(string tagId)
    {
        var row = FindRow(_panel, tagId);
        if (row is null) return null;
        foreach (var child in row.GetChildren())
        {
            if (child is CheckBox box) return box;
        }
        return null;
    }

    private static HBoxContainer? FindRow(Node node, string tagId)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is HBoxContainer row)
            {
                foreach (var rowChild in row.GetChildren())
                {
                    if (rowChild is Label l && l.TooltipText == tagId) return row;
                }
            }
            var found = FindRow(child, tagId);
            if (found is not null) return found;
        }
        return null;
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("self-test proppanel: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test proppanel: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
