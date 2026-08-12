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
    [Signal] public delegate void ModeToggledEventHandler();

    private Button _pauseBtn = null!;
    private OptionButton _rateBox = null!;
    private Button _modeBtn = null!;

    /// <summary>
    /// Show which mode the viewport is in. This is the only cue that a click
    /// means something different now, so it says what mode you are <em>in</em>
    /// rather than what the button would switch to — a toggle labelled with its
    /// destination reads as a status line half the time and lies the other half.
    /// </summary>
    public void ShowMode(bool running)
    {
        _modeBtn.Text = running ? "▶ RUN" : "✎ EDIT";
        _modeBtn.TooltipText = running
            ? "Running: click the panel buttons to operate the line. F1 to edit."
            : "Editing: click parts to select, M to move, Delete to remove. F1 to run.";
        _modeBtn.AddThemeColorOverride("font_color",
            running ? new Color(0.45f, 0.95f, 0.55f) : new Color(0.98f, 0.80f, 0.35f));
    }

    /// <summary>Reflect state the user may have changed by keyboard, so the
    /// button never claims the simulation is running while it is frozen.</summary>
    public void ShowState(bool paused, float rate)
    {
        _pauseBtn.Text = paused ? "▶ Run" : "⏸ Pause";
        for (int i = 0; i < Sim.SimulationControls.Rates.Length; i++)
        {
            if (Mathf.IsEqualApprox(Sim.SimulationControls.Rates[i], rate)) _rateBox.Selected = i;
        }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(740, 44);
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.KeepSize, 10);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(740, 44),
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

        // Mode leads the toolbar: it changes what every other click in the
        // viewport does, so it is the first thing worth knowing.
        //
        // Shortcuts moved from the labels into tooltips when this button was
        // added — nine buttons each carrying "(Key)" overflowed the bar at the
        // default window size and clipped Clear Scene off the right-hand end.
        _modeBtn = new Button { Text = "✎ EDIT", CustomMinimumSize = new Vector2(82, 32) };
        _modeBtn.Pressed += () => EmitSignal(SignalName.ModeToggled);
        hbox.AddChild(_modeBtn);

        hbox.AddChild(new VSeparator());

        // Simulation controls next: they are what you reach for most while
        // debugging a program.
        _pauseBtn = new Button
        {
            Text = "⏸ Pause",
            TooltipText = "Pause / resume the simulation (Space)",
            CustomMinimumSize = new Vector2(84, 32),
        };
        _pauseBtn.Pressed += () => EmitSignal(SignalName.PauseToggled);
        hbox.AddChild(_pauseBtn);

        var resetBtn = new Button
        {
            Text = "⏹ Reset",
            TooltipText = "Clear the items and zero the counters, keeping the line you built (Ctrl+R)",
            CustomMinimumSize = new Vector2(84, 32),
        };
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

        var saveBtn = new Button
        {
            Text = "💾 Save",
            TooltipText = "Save the scene to user://custom_scene.json",
            CustomMinimumSize = new Vector2(78, 32),
        };
        saveBtn.Pressed += () => EmitSignal(SignalName.SaveRequested);
        hbox.AddChild(saveBtn);

        var loadBtn = new Button
        {
            Text = "📂 Load",
            TooltipText = "Load the scene from user://custom_scene.json",
            CustomMinimumSize = new Vector2(78, 32),
        };
        loadBtn.Pressed += () => EmitSignal(SignalName.LoadRequested);
        hbox.AddChild(loadBtn);

        var driverBtn = new Button
        {
            Text = "⚡ Driver",
            TooltipText = "Connect the scene to a PLC or a driver (F5)",
            CustomMinimumSize = new Vector2(84, 32),
        };
        driverBtn.Pressed += () => EmitSignal(SignalName.DriverRequested);
        hbox.AddChild(driverBtn);

        var wiringBtn = new Button
        {
            Text = "🔌 Wiring",
            TooltipText = "Map scene tags onto controller addresses (F4)",
            CustomMinimumSize = new Vector2(88, 32),
        };
        wiringBtn.Pressed += () => EmitSignal(SignalName.WiringRequested);
        hbox.AddChild(wiringBtn);

        var clearBtn = new Button
        {
            Text = "🗑️ Clear",
            TooltipText = "Remove every part from the scene",
            CustomMinimumSize = new Vector2(78, 32),
        };
        clearBtn.Pressed += () => EmitSignal(SignalName.ClearRequested);
        hbox.AddChild(clearBtn);
    }
}
