using System;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Interactive Driver Connection Selector Modal UI (F5 Key or Toolbar).
/// Allows users to pick their PLC connection type (PLCSIM Advanced, S7 Snap7, OPC UA, Modbus TCP)
/// and edit IP address, port, and instance parameters live in the 3D UI.
/// </summary>
public partial class DriverConnectionUI : Control
{
    private OptionButton _driverDropdown = null!;
    private LineEdit _ipInput = null!;
    private LineEdit _portInput = null!;
    private LineEdit _instanceInput = null!;
    private LineEdit _dbInput = null!;
    private Label _statusLabel = null!;

    public string SelectedDriver { get; private set; } = "plcsim-advanced";
    public string IpAddress { get; private set; } = "192.168.1.20";
    public string PortOrUrl { get; private set; } = "4840";
    public string InstanceName { get; private set; } = "Sorting_PLC";
    public int DbNumber { get; private set; } = 1;

    public override void _Ready()
    {
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Dark background overlay
        var overlay = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
        };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(overlay);

        // Center modal container
        var centerContainer = new CenterContainer();
        centerContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centerContainer);

        var modal = new PanelContainer
        {
            CustomMinimumSize = new Vector2(650, 480),
        };
        centerContainer.AddChild(modal);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        modal.AddChild(margin);

        var mainBox = new VBoxContainer();
        margin.AddChild(mainBox);

        // Header
        var header = new HBoxContainer();
        mainBox.AddChild(header);

        var title = new Label
        {
            Text = "🔌 SELECT PLC DRIVER & CONNECTION (F5)",
            HorizontalAlignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(title);

        var closeBtn = new Button { Text = " ❌ Close " };
        closeBtn.Pressed += () => Visible = false;
        header.AddChild(closeBtn);

        mainBox.AddChild(new HSeparator());

        // Driver Selection Dropdown
        mainBox.AddChild(new Label { Text = "Select Protocol Driver Target:" });

        _driverDropdown = new OptionButton
        {
            CustomMinimumSize = new Vector2(580, 36),
        };
        _driverDropdown.AddItem("Siemens PLCSIM Advanced API (Direct Shared Memory)", 0);
        _driverDropdown.AddItem("Siemens S7 Protocol (Snap7 ISO-on-TCP)", 1);
        _driverDropdown.AddItem("OPC UA Client (S7-1500 / Codesys / Beckhoff)", 2);
        _driverDropdown.AddItem("OPC UA Server (Node-RED / Ignition SCADA)", 3);
        _driverDropdown.AddItem("Modbus TCP Server (OpenPLC / SCADA)", 4);
        _driverDropdown.AddItem("Standalone Simulation (Internal Mock)", 5);
        _driverDropdown.ItemSelected += OnDriverSelected;
        mainBox.AddChild(_driverDropdown);

        mainBox.AddChild(new HSeparator());

        // Parameter Form
        var grid = new GridContainer { Columns = 2 };
        mainBox.AddChild(grid);

        grid.AddChild(new Label { Text = "PLC IP Address / Host:" });
        _ipInput = new LineEdit { Text = IpAddress, CustomMinimumSize = new Vector2(380, 32) };
        grid.AddChild(_ipInput);

        grid.AddChild(new Label { Text = "PLC Port / OPC UA URL:" });
        _portInput = new LineEdit { Text = PortOrUrl, CustomMinimumSize = new Vector2(380, 32) };
        grid.AddChild(_portInput);

        grid.AddChild(new Label { Text = "PLCSIM Instance Name:" });
        _instanceInput = new LineEdit { Text = InstanceName, CustomMinimumSize = new Vector2(380, 32) };
        grid.AddChild(_instanceInput);

        grid.AddChild(new Label { Text = "S7 DB Number:" });
        _dbInput = new LineEdit { Text = DbNumber.ToString(), CustomMinimumSize = new Vector2(380, 32) };
        grid.AddChild(_dbInput);

        mainBox.AddChild(new HSeparator());

        // Footer buttons & status
        var footer = new HBoxContainer();
        mainBox.AddChild(footer);

        _statusLabel = new Label
        {
            Text = "Active Driver: Siemens PLCSIM Advanced (Ready)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
        footer.AddChild(_statusLabel);

        var connectBtn = new Button { Text = " ⚡ Apply & Connect " };
        connectBtn.Pressed += ApplyConnectionSettings;
        footer.AddChild(connectBtn);
    }

    public void ToggleVisibility()
    {
        Visible = !Visible;
    }

    private void OnDriverSelected(long index)
    {
        SelectedDriver = index switch
        {
            0 => "plcsim-advanced",
            1 => "s7-snap7",
            2 => "opcua-client",
            3 => "opcua-server",
            4 => "modbus-tcp",
            _ => "mock"
        };
    }

    private void ApplyConnectionSettings()
    {
        IpAddress = _ipInput.Text;
        PortOrUrl = _portInput.Text;
        InstanceName = _instanceInput.Text;
        if (int.TryParse(_dbInput.Text, out int db)) DbNumber = db;

        _statusLabel.Text = $"Active Driver: {SelectedDriver} ({IpAddress})";
        GD.Print($"Applied Driver Settings: Driver={SelectedDriver}, IP={IpAddress}, Instance={InstanceName}");
        Visible = false;
    }
}
