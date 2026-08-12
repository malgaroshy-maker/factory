using System.IO;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Top toolbar UI providing Save Scene, Load Scene, and Clear Scene actions.
/// </summary>
public partial class SceneToolbarUI : Control
{
    [Signal] public delegate void SaveRequestedEventHandler();
    [Signal] public delegate void LoadRequestedEventHandler();
    [Signal] public delegate void ClearRequestedEventHandler();
    [Signal] public delegate void WiringRequestedEventHandler();
    [Signal] public delegate void DriverRequestedEventHandler();
    [Signal] public delegate void PauseToggledEventHandler();
    [Signal] public delegate void ResetRequestedEventHandler();
    [Signal] public delegate void RateSelectedEventHandler(float rate);

    private Button _pauseBtn = null!;
    private OptionButton _rateBox = null!;

    /// <summary>Reflect state the user may have changed by keyboard, so the
    /// button never claims the simulation is running while it is frozen.</summary>
    public void ShowState(bool paused, float rate)
    {
        _pauseBtn.Text = paused ? "▶ Run (Space)" : "⏸ Pause (Space)";
        for (int i = 0; i < Sim.SimulationControls.Rates.Length; i++)
        {
            if (Mathf.IsEqualApprox(Sim.SimulationControls.Rates[i], rate)) _rateBox.Selected = i;
        }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(860, 44);
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.KeepSize, 10);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(860, 44),
        };
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        panel.AddChild(margin);

        var hbox = new HBoxContainer();
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        margin.AddChild(hbox);

        // Simulation controls first: they are what you reach for most while
        // debugging a program, so they lead the toolbar.
        _pauseBtn = new Button { Text = "⏸ Pause (Space)", CustomMinimumSize = new Vector2(115, 32) };
        _pauseBtn.Pressed += () => EmitSignal(SignalName.PauseToggled);
        hbox.AddChild(_pauseBtn);

        var resetBtn = new Button { Text = "⏹ Reset (Ctrl+R)", CustomMinimumSize = new Vector2(118, 32) };
        resetBtn.Pressed += () => EmitSignal(SignalName.ResetRequested);
        hbox.AddChild(resetBtn);

        _rateBox = new OptionButton { CustomMinimumSize = new Vector2(78, 32) };
        foreach (float rate in Sim.SimulationControls.Rates)
        {
            _rateBox.AddItem(rate < 1.0f ? $"{rate:0.00}×" : $"{rate:0.##}×");
        }
        _rateBox.Selected = System.Array.IndexOf(Sim.SimulationControls.Rates, 1.0f);
        _rateBox.ItemSelected += (index) =>
            EmitSignal(SignalName.RateSelected, Sim.SimulationControls.Rates[index]);
        hbox.AddChild(_rateBox);

        hbox.AddChild(new VSeparator());

        var saveBtn = new Button { Text = "💾 Save Scene", CustomMinimumSize = new Vector2(95, 32) };
        saveBtn.Pressed += () => EmitSignal(SignalName.SaveRequested);
        hbox.AddChild(saveBtn);

        var loadBtn = new Button { Text = "📂 Load Scene", CustomMinimumSize = new Vector2(95, 32) };
        loadBtn.Pressed += () => EmitSignal(SignalName.LoadRequested);
        hbox.AddChild(loadBtn);

        var driverBtn = new Button { Text = "⚡ Driver (F5)", CustomMinimumSize = new Vector2(100, 32) };
        driverBtn.Pressed += () => EmitSignal(SignalName.DriverRequested);
        hbox.AddChild(driverBtn);

        var wiringBtn = new Button { Text = "🔌 I/O Wiring (F4)", CustomMinimumSize = new Vector2(115, 32) };
        wiringBtn.Pressed += () => EmitSignal(SignalName.WiringRequested);
        hbox.AddChild(wiringBtn);

        var clearBtn = new Button { Text = "🗑️ Clear Scene", CustomMinimumSize = new Vector2(95, 32) };
        clearBtn.Pressed += () => EmitSignal(SignalName.ClearRequested);
        hbox.AddChild(clearBtn);
    }
}
