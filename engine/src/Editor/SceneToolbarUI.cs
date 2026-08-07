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

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(560, 44);
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.KeepSize, 10);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(560, 44),
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
