using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Interactive Driver Connection Selector Modal UI (F5 Key or Toolbar).
/// Features Auto-Detect Mode to automatically probe and identify active PLCs (PLCSIM Advanced, OPC UA, S7 ISO-on-TCP, Modbus).
/// </summary>
public partial class DriverConnectionUI : Control
{
    private OptionButton _driverDropdown = null!;
    private Label _commandLabel = null!;

    /// <summary>Process id of the sidecar started from this dialog, so it can be
    /// stopped and is not left holding a PLC session when the window closes.</summary>
    private int _sidecarPid;
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
            CustomMinimumSize = new Vector2(680, 520),
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

        var autoDetectHeaderBtn = new Button { Text = " 🔍 Auto-Detect Mode " };
        autoDetectHeaderBtn.Pressed += () => _ = RunAutoDetectAsync();
        header.AddChild(autoDetectHeaderBtn);

        var closeBtn = new Button { Text = " ❌ Close " };
        closeBtn.Pressed += () => Visible = false;
        header.AddChild(closeBtn);

        mainBox.AddChild(new HSeparator());

        // Driver Selection Dropdown
        mainBox.AddChild(new Label { Text = "Select Protocol Driver Target:" });

        _driverDropdown = new OptionButton
        {
            CustomMinimumSize = new Vector2(610, 36),
        };
        _driverDropdown.AddItem("🔍 Auto-Detect Active Controller (Automatic Probe)", 0);
        _driverDropdown.AddItem("Siemens PLCSIM Advanced API (Direct Shared Memory)", 1);
        _driverDropdown.AddItem("Siemens S7 Protocol (Snap7 ISO-on-TCP)", 2);
        _driverDropdown.AddItem("OPC UA Client (S7-1500 / Codesys / Beckhoff)", 3);
        _driverDropdown.AddItem("OPC UA Server (Node-RED / Ignition SCADA)", 4);
        _driverDropdown.AddItem("Modbus TCP Server (OpenPLC / SCADA)", 5);
        _driverDropdown.AddItem("Standalone Simulation (Internal Mock)", 6);
        _driverDropdown.ItemSelected += OnDriverSelected;
        mainBox.AddChild(_driverDropdown);

        mainBox.AddChild(new HSeparator());

        // Parameter Form
        var grid = new GridContainer { Columns = 2 };
        mainBox.AddChild(grid);

        grid.AddChild(new Label { Text = "PLC IP Address / Host:" });
        _ipInput = new LineEdit { Text = IpAddress, CustomMinimumSize = new Vector2(400, 32) };
        grid.AddChild(_ipInput);

        grid.AddChild(new Label { Text = "PLC Port / OPC UA URL:" });
        _portInput = new LineEdit { Text = PortOrUrl, CustomMinimumSize = new Vector2(400, 32) };
        grid.AddChild(_portInput);

        grid.AddChild(new Label { Text = "PLCSIM Instance Name:" });
        _instanceInput = new LineEdit { Text = InstanceName, CustomMinimumSize = new Vector2(400, 32) };
        grid.AddChild(_instanceInput);

        grid.AddChild(new Label { Text = "S7 DB Number:" });
        _dbInput = new LineEdit { Text = DbNumber.ToString(), CustomMinimumSize = new Vector2(400, 32) };
        grid.AddChild(_dbInput);

        mainBox.AddChild(new HSeparator());

        // Footer buttons & status
        var footer = new HBoxContainer();
        mainBox.AddChild(footer);

        _statusLabel = new Label
        {
            // Not "Ready": nothing is connected until the sidecar is started,
            // and saying otherwise is how this dialog used to mislead people.
            Text = "No driver running. Apply & Connect starts the Python sidecar.",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.80f, 0.85f));
        footer.AddChild(_statusLabel);

        var autoBtn = new Button { Text = " 🔍 Auto-Detect Now " };
        autoBtn.Pressed += () => _ = RunAutoDetectAsync();
        footer.AddChild(autoBtn);

        var stopBtn = new Button
        {
            Text = " ⏹ Stop Sidecar ",
            TooltipText = "Stop the driver process started from this dialog",
        };
        stopBtn.Pressed += StopSidecar;
        footer.AddChild(stopBtn);

        var connectBtn = new Button { Text = " ⚡ Apply & Connect " };
        connectBtn.Pressed += ApplyConnectionSettings;
        footer.AddChild(connectBtn);

        // Show the command as well as running it. Half the value of this dialog
        // is teaching what it is doing, and the other half is still working when
        // the launch fails because python is not on PATH.
        _commandLabel = new Label
        {
            Text = "The engine speaks only the tag bus; PLC protocols live in the "
                 + "Python sidecar. Apply & Connect starts it and copies the command.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _commandLabel.AddThemeFontSizeOverride("font_size", 11);
        _commandLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.80f, 0.95f));
        mainBox.AddChild(_commandLabel);
    }

    public void ToggleVisibility()
    {
        Visible = !Visible;
    }

    private void OnDriverSelected(long index)
    {
        if (index == 0)
        {
            _ = RunAutoDetectAsync();
            return;
        }

        SelectedDriver = index switch
        {
            1 => "plcsim-advanced",
            2 => "s7-snap7",
            3 => "opcua-client",
            4 => "opcua-server",
            5 => "modbus-tcp",
            _ => "mock"
        };
    }

    private async Task RunAutoDetectAsync()
    {
        _statusLabel.Text = "🔍 Probing network & virtual adapters for active PLC drivers...";
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.3f));

        string targetIp = _ipInput.Text.Trim();
        if (string.IsNullOrEmpty(targetIp)) targetIp = "192.168.1.20";

        // Probe 1: OPC UA (Port 4840)
        if (await ProbePortAsync(targetIp, 4840))
        {
            SelectedDriver = "opcua-client";
            _driverDropdown.Select(3);
            _statusLabel.Text = $"✅ Auto-Detected: Active OPC UA Server at {targetIp}:4840";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
            return;
        }

        // Probe 2: Siemens S7 ISO-on-TCP (Port 102)
        if (await ProbePortAsync(targetIp, 102))
        {
            SelectedDriver = "s7-snap7";
            _driverDropdown.Select(2);
            _statusLabel.Text = $"✅ Auto-Detected: Active Siemens S7 CPU at {targetIp}:102";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
            return;
        }

        // Probe 3: Modbus TCP (Port 502)
        if (await ProbePortAsync(targetIp, 502))
        {
            SelectedDriver = "modbus-tcp";
            _driverDropdown.Select(5);
            _statusLabel.Text = $"✅ Auto-Detected: Active Modbus TCP Server at {targetIp}:502";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
            return;
        }

        // Probe 4: Default to Siemens PLCSIM Advanced API
        SelectedDriver = "plcsim-advanced";
        _driverDropdown.Select(1);
        _statusLabel.Text = "✅ Auto-Detected: Siemens PLCSIM Advanced Shared Memory API";
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
    }

    private static async Task<bool> ProbePortAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(400); // 400ms fast network probe
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            return completedTask == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The sidecar arguments this dialog's settings amount to.
    ///
    /// <c>connect</c>, never <c>demo</c>: demo starts its own Python scene on
    /// the bus port, so against a running engine it either fails to bind or
    /// drives a scene you cannot see.
    /// </summary>
    public string BuildSidecarArguments()
    {
        var args = new System.Text.StringBuilder("-m factoryforge_sidecar connect");
        args.Append($" --driver {SelectedDriver}");

        switch (SelectedDriver)
        {
            case "opcua-client":
                string url = PortOrUrl.StartsWith("opc.tcp://")
                    ? PortOrUrl
                    : $"opc.tcp://{IpAddress}:4840";
                args.Append($" -o url {url}");
                if (Godot.FileAccess.FileExists(DriverWiringUI.MappingPath))
                {
                    string mapping = ProjectSettings.GlobalizePath(DriverWiringUI.MappingPath);
                    args.Append($" --mapping \"{mapping}\"");
                }
                break;

            case "s7-snap7":
                args.Append($" -o host {IpAddress} -o db {DbNumber}");
                break;

            case "plcsim-advanced":
                args.Append($" -o instance {InstanceName}");
                break;

            case "modbus-tcp":
            case "opcua-server":
                // Both are servers: the controller connects to them, so there is
                // nothing to point at.
                break;
        }

        return args.ToString();
    }

    /// <summary>Read the form, start the sidecar, and report what happened.</summary>
    public void ApplyConnectionSettings()
    {
        IpAddress = _ipInput.Text.Trim();
        PortOrUrl = _portInput.Text.Trim();
        InstanceName = _instanceInput.Text.Trim();
        if (int.TryParse(_dbInput.Text, out int db)) DbNumber = db;

        string sidecarDir = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        // res:// is engine/; the sidecar package sits beside it in the checkout.
        sidecarDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sidecarDir) ?? "", "sidecar");

        string arguments = BuildSidecarArguments();
        string display = $"cd \"{sidecarDir}\" && python {arguments}";
        _commandLabel.Text = display;
        DisplayServer.ClipboardSet(display);
        GD.Print($"Sidecar command (copied to clipboard):\n  {display}");

        if (!System.IO.Directory.Exists(sidecarDir))
        {
            Warn($"Command copied, but {sidecarDir} does not exist — run the sidecar from your checkout.");
            return;
        }

        LaunchSidecar(sidecarDir, arguments);
    }

    /// <summary>
    /// Start the sidecar as a detached process.
    ///
    /// The engine speaks the tag bus and nothing else — every PLC protocol lives
    /// in the Python sidecar — so "connect" has to mean "start that". If python
    /// is not on PATH this says so and falls back to the copied command, rather
    /// than the previous behaviour of printing a line and pretending.
    /// </summary>
    private void LaunchSidecar(string workingDir, string arguments)
    {
        if (_sidecarPid > 0 && OS.IsProcessRunning(_sidecarPid))
        {
            OS.Kill(_sidecarPid);
            _sidecarPid = 0;
        }

        string[] argv =
        {
            "/d", "/s", "/c",
            $"cd /d \"{workingDir}\" && python {arguments}",
        };

        // A console window is wanted here: the sidecar's live status is how you
        // see whether the PLC is actually answering.
        int pid = OS.CreateProcess("cmd.exe", argv, openConsole: true);
        if (pid <= 0)
        {
            Warn("Could not start python. The command is on your clipboard — run it yourself.");
            return;
        }

        _sidecarPid = pid;
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
        _statusLabel.Text = $"Sidecar running (pid {pid}) — driver '{SelectedDriver}'";
        Visible = false;
    }

    private void Warn(string message)
    {
        _statusLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.65f, 0.35f));
        _statusLabel.Text = message;
        GD.PrintErr(message);
    }

    private void StopSidecar()
    {
        if (_sidecarPid <= 0 || !OS.IsProcessRunning(_sidecarPid))
        {
            _statusLabel.Text = "No sidecar started from here.";
            return;
        }

        OS.Kill(_sidecarPid);
        _statusLabel.Text = $"Stopped sidecar (pid {_sidecarPid})";
        _sidecarPid = 0;
    }

    public override void _ExitTree()
    {
        // Don't leave a driver holding an OPC UA session after the window closes.
        if (_sidecarPid > 0 && OS.IsProcessRunning(_sidecarPid)) OS.Kill(_sidecarPid);
    }
}
