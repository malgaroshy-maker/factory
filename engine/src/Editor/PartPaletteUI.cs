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
        CustomMinimumSize = new Vector2(180, 350);
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft, LayoutPresetMode.KeepSize, 20);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(180, 350),
        };
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

        AddPaletteButton(mainBox, "Conveyor Belt", "ConveyorBelt");
        AddPaletteButton(mainBox, "Photoelectric Sensor", "PhotoelectricSensor");
        AddPaletteButton(mainBox, "Retroreflective Sensor", "RetroreflectiveSensor");
        AddPaletteButton(mainBox, "Inductive Sensor", "InductiveSensor");
        AddPaletteButton(mainBox, "Light Array", "LightArray");
        AddPaletteButton(mainBox, "Pneumatic Pusher", "PusherMechanism");
        AddPaletteButton(mainBox, "Ramp (Chute)", "Chute");
        AddPaletteButton(mainBox, "Stack Light", "StackLight");
        AddPaletteButton(mainBox, "Digital Display", "DigitalDisplay");
        AddPaletteButton(mainBox, "Weight Conveyor", "WeighingConveyor");
        AddPaletteButton(mainBox, "Roller Conveyor", "RollerConveyor");
        AddPaletteButton(mainBox, "Box Emitter", "Emitter");
        AddPaletteButton(mainBox, "Box Remover", "Remover");
        AddPaletteButton(mainBox, "Control Panel", "ButtonPanel");
        AddPaletteButton(mainBox, "Level Tank", "LevelTank");
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
