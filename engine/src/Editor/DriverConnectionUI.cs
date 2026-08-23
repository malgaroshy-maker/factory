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

    /// <summary>Set by Main so the empty-state offer (UX-33) can name and run
    /// the loaded scene's exercise.</summary>
    public SceneEditor? Editor { get; set; }
    public IdleHintUI? IdleHint { get; set; }

    public string SelectedDriver { get; private set; } = "plcsim-advanced";
    public string IpAddress { get; private set; } = "";
    public string PortOrUrl { get; private set; } = "4840";
    public string InstanceName { get; private set; } = "";
    public int DbNumber { get; private set; } = 1;

    private const string SettingsPath = "user://driver_connection.cfg";

    /// <summary>Remember whatever the user last typed, since there is no sane
    /// global default for someone else's PLC address or instance name.</summary>
    private void LoadLastSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return;
        IpAddress = (string)cfg.GetValue("driver", "ip", IpAddress);
        PortOrUrl = (string)cfg.GetValue("driver", "port_or_url", PortOrUrl);
        InstanceName = (string)cfg.GetValue("driver", "instance", InstanceName);
        DbNumber = (int)cfg.GetValue("driver", "db", DbNumber);
    }

    private void SaveLastSettings()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("driver", "ip", IpAddress);
        cfg.SetValue("driver", "port_or_url", PortOrUrl);
        cfg.SetValue("driver", "instance", InstanceName);
        cfg.SetValue("driver", "db", DbNumber);
        cfg.Save(SettingsPath);
    }

    public override void _Ready()
    {
        LoadLastSettings();
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

        // UX-33: the first thing a fresh open of this dialog offers, ahead of
        // any driver configuration -- "no PLC yet" is the normal state for
        // someone who has never touched TIA Portal, not a problem to route
        // around straight into IP addresses and instance names.
        var exerciseRow = new HBoxContainer();
        mainBox.AddChild(exerciseRow);

        var exerciseLabel = new Label
        {
            Text = "No PLC yet? Run the built-in exercise for this scene first.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        exerciseLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.90f, 0.80f));
        exerciseRow.AddChild(exerciseLabel);

        var exerciseBtn = new Button { Text = " 🧪 Try this scene " };
        exerciseBtn.Pressed += () => { if (Editor is not null) TryScene.Run(Editor, IdleHint); };
        exerciseRow.AddChild(exerciseBtn);

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
        _ipInput = new LineEdit
        {
            Text = IpAddress,
            PlaceholderText = "e.g. 192.168.1.20",
            CustomMinimumSize = new Vector2(400, 32),
        };
        grid.AddChild(_ipInput);

        grid.AddChild(new Label { Text = "PLC Port / OPC UA URL:" });
        _portInput = new LineEdit { Text = PortOrUrl, CustomMinimumSize = new Vector2(400, 32) };
        grid.AddChild(_portInput);

        grid.AddChild(new Label { Text = "PLCSIM Instance Name:" });
        _instanceInput = new LineEdit
        {
            Text = InstanceName,
            PlaceholderText = "the name in the PLCSIM control panel",
            CustomMinimumSize = new Vector2(400, 32),
        };
        grid.AddChild(_instanceInput);

        grid.AddChild(new Label { Text = "S7 DB Number:" });
        _dbInput = new LineEdit { Text = DbNumber.ToString(), CustomMinimumSize = new Vector2(400, 32) };
        grid.AddChild(_dbInput);

        mainBox.AddChild(new HSeparator());

        // Status gets its own full-width row, and it wraps.
        //
        // It used to sit inside the footer HBox with no AutowrapMode. A Label
        // without autowrap reports its whole single-line text as its minimum
        // width, and that minimum propagates up through the footer to the
        // PanelContainer. So the moment the status said anything long -- the
        // multi-sentence "no controller answered, PLCSIM cannot be probed"
        // explanation from FF-06 is ~200 characters -- the modal's minimum
        // width exceeded the viewport, CenterContainer had no slack left to
        // centre with, and Apply & Connect was pushed off the right edge of
        // the screen. The dialog broke precisely when it had something to say.
        _statusLabel = new Label
        {
            // Not "Ready": nothing is connected until the sidecar is started,
            // and saying otherwise is how this dialog used to mislead people.
            Text = "No driver running. Apply & Connect starts the Python sidecar.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 44),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.80f, 0.85f));
        mainBox.AddChild(_statusLabel);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        mainBox.AddChild(footer);

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

    /// <summary>
    /// Probe for a live controller, then apply the result in a single
    /// deferred callback.
    ///
    /// Everything after the first <c>await</c> in this method used to mutate
    /// Godot UI nodes directly — <c>await</c> resumes on a thread-pool thread
    /// here, since Godot has no synchronization context to capture, so that
    /// was a real cross-thread race on scene-tree nodes (FF-07). Structuring
    /// it as "decide, then apply once via CallDeferred" fixes that and
    /// happens to make the honest-fallback fix (FF-06) a single place too.
    /// </summary>
    /// <remarks>Public so <c>--self-test=layout</c> can drive the real probe
    /// rather than a copy of its messages: the longest text this dialog can
    /// display is produced here, and that length is what broke the layout.</remarks>
    public async Task RunAutoDetectAsync()
    {
        _statusLabel.Text = "🔍 Probing network & virtual adapters for active PLC drivers...";
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.3f));

        string targetIp = _ipInput.Text.Trim();
        string driver, message;
        int dropdownIndex;
        bool found;

        try
        {
            if (!string.IsNullOrEmpty(targetIp) && await ProbePortAsync(targetIp, 4840))
            {
                (driver, dropdownIndex, found) = ("opcua-client", 3, true);
                message = $"✅ Auto-Detected: Active OPC UA Server at {targetIp}:4840";
            }
            else if (!string.IsNullOrEmpty(targetIp) && await ProbePortAsync(targetIp, 102))
            {
                (driver, dropdownIndex, found) = ("s7-snap7", 2, true);
                message = $"✅ Auto-Detected: Active Siemens S7 CPU at {targetIp}:102";
            }
            else if (!string.IsNullOrEmpty(targetIp) && await ProbePortAsync(targetIp, 502))
            {
                (driver, dropdownIndex, found) = ("modbus-tcp", 5, true);
                message = $"✅ Auto-Detected: Active Modbus TCP Server at {targetIp}:502";
            }
            else
            {
                // Nothing was detected — PLCSIM Advanced was never probed, it
                // is a shared-memory API with no port to probe at all. Saying
                // so honestly, instead of a green tick claiming a CPU was
                // found, is the whole fix for FF-06.
                (driver, dropdownIndex, found) = ("plcsim-advanced", 1, false);
                message = targetIp.Length == 0
                    ? "No IP entered, so only PLCSIM Advanced could be assumed. It uses a "
                    + "shared-memory API with no network port — enter an IP to probe for "
                    + "OPC UA, S7 or Modbus instead. Check the instance name below."
                    : $"No controller answered on {targetIp}. Defaulting to PLCSIM Advanced, "
                    + "which uses a shared-memory API with no network port and cannot be "
                    + "probed — check the instance name below.";
            }
        }
        catch (Exception e)
        {
            // Was previously lost entirely: a fire-and-forget task's exception
            // vanishes unless something is watching for it.
            GD.PrintErr($"auto-detect failed: {e}");
            Callable.From(() => Warn($"Auto-detect failed: {e.Message}")).CallDeferred();
            return;
        }

        Callable.From(() =>
        {
            SelectedDriver = driver;
            _driverDropdown.Select(dropdownIndex);
            _statusLabel.Text = message;
            _statusLabel.AddThemeColorOverride("font_color",
                found ? new Color(0.3f, 1.0f, 0.4f) : new Color(0.98f, 0.80f, 0.35f));
        }).CallDeferred();
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

        // The F4 wiring panel writes one mapping file, and every *client*
        // driver reads tag->address translations from it — only opcua-client
        // used to actually receive it. modbus-tcp and opcua-server derive
        // their own addressing (a deterministic tag-sorted table and
        // ns=2;s=<tag_id> respectively) and ignore a mapping file entirely,
        // so passing one to them would be a no-op that looks like it did
        // something. See FF-05.
        bool driverReadsMapping = SelectedDriver is "opcua-client" or "s7-snap7" or "plcsim-advanced";
        if (driverReadsMapping && Godot.FileAccess.FileExists(DriverWiringUI.MappingPath))
        {
            string mapping = ProjectSettings.GlobalizePath(DriverWiringUI.MappingPath);
            args.Append($" --mapping \"{mapping}\"");
        }

        return args.ToString();
    }

    /// <summary>Whether <see cref="BuildSidecarArguments"/> will actually pass
    /// the F4 wiring file for the currently selected driver — used to tell the
    /// operator plainly rather than let them assume it always does.</summary>
    private bool WillUseMappingFile() =>
        SelectedDriver is "opcua-client" or "s7-snap7" or "plcsim-advanced"
        && Godot.FileAccess.FileExists(DriverWiringUI.MappingPath);

    /// <summary>Read the form, start the sidecar, and report what happened.</summary>
    public void ApplyConnectionSettings()
    {
        IpAddress = _ipInput.Text.Trim();
        PortOrUrl = _portInput.Text.Trim();
        InstanceName = _instanceInput.Text.Trim();
        if (int.TryParse(_dbInput.Text, out int db)) DbNumber = db;
        SaveLastSettings();

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

        int pid = TerminalLauncher.Spawn(workingDir, TerminalLauncher.PythonCommand(arguments));
        if (pid <= 0)
        {
            Warn("Could not start python. The command is on your clipboard — run it yourself.");
            return;
        }

        _sidecarPid = pid;
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
        // Naming the wiring file (or its absence) here is the difference
        // between "connected" and "connected to the addresses you actually
        // meant" — F4 silently not reaching three of the four drivers was
        // FF-05, and this is the line that would have caught it immediately.
        string wiring = WillUseMappingFile() ? "using F4 wiring" : "no wiring file — driver defaults";
        _statusLabel.Text = $"Sidecar running (pid {pid}) — driver '{SelectedDriver}', {wiring}";
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
