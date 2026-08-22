using System.IO;
using FactoryForge.Sim;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Top toolbar UI providing Save Scene, Load Scene, and Clear Scene actions.
/// </summary>
public partial class SceneToolbarUI : Control
{
    /// <summary>Set by Main so the status chip has something to poll. Before
    /// this, whether a driver was connected was visible nowhere in the UI —
    /// <c>HasClient</c> had zero references outside the bus itself, and a
    /// failed port bind only ever printed to a console a packaged build does
    /// not have. See FF-04.</summary>
    public TagBusServer? Bus { get; set; }

    /// <summary>Set by Main so the demo toggle has something to drive. See FF-23.</summary>
    public DemoDriver? Demo { get; set; }

    /// <summary>Set by Main so the status chip can show the live item count
    /// and the cap warning. See FF-12.</summary>
    public SceneEditor? Editor { get; set; }

    private Label _connectionChip = null!;
    private bool? _lastListening;
    private bool? _lastHasClient;
    private string? _lastSceneName;
    private int _lastTagCount = -1;
    private int _lastItemCount = -1;
    private bool? _lastItemCapHit;

    private Button _demoBtn = null!;
    private bool? _lastDemoActive;

    [Signal] public delegate void SaveRequestedEventHandler(string path);
    [Signal] public delegate void LoadRequestedEventHandler(string path);
    [Signal] public delegate void ClearRequestedEventHandler();
    [Signal] public delegate void WiringRequestedEventHandler();
    [Signal] public delegate void DriverRequestedEventHandler();
    [Signal] public delegate void PauseToggledEventHandler();
    [Signal] public delegate void ResetRequestedEventHandler();
    [Signal] public delegate void RateSelectedEventHandler(float rate);
    [Signal] public delegate void ModeToggledEventHandler();
    [Signal] public delegate void StartScreenRequestedEventHandler();
    [Signal] public delegate void DemoToggledEventHandler();

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

    /// <summary>Ctrl+S's target — the same dialog the Save button opens, so
    /// the keyboard path and the mouse path can never disagree about what
    /// "save" means. See FF-21.</summary>
    public void ShowSaveDialog() => ShowFileDialog(FileDialog.FileModeEnum.SaveFile);

    /// <summary>Ctrl+O's target. Routes through the same <c>LoadRequested</c>
    /// signal the Load button uses, which is also where the unsaved-changes
    /// guard (FF-02) lives — so the keyboard path gets it for free.</summary>
    public void ShowLoadDialog() => ShowFileDialog(FileDialog.FileModeEnum.OpenFile);

    /// <summary>
    /// Pick a scene file. Save and Load used to be hardwired to a single
    /// <c>user://custom_scene.json</c>, so there was exactly one saved scene and
    /// Save silently overwrote it — you could not keep two lines, and the file
    /// lived somewhere nobody could find to share it.
    /// </summary>
    private void ShowFileDialog(FileDialog.FileModeEnum mode)
    {
        var dialog = new FileDialog
        {
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.json ; FactoryForge scene" },
            CurrentDir = DefaultSceneDir(),
            CurrentFile = mode == FileDialog.FileModeEnum.SaveFile ? "my_scene.json" : "",
            Title = mode == FileDialog.FileModeEnum.SaveFile ? "Save scene as" : "Open scene",
            Size = new Vector2I(760, 520),
        };

        dialog.FileSelected += (path) =>
        {
            EmitSignal(mode == FileDialog.FileModeEnum.SaveFile
                ? SignalName.SaveRequested
                : SignalName.LoadRequested, path);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;

        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>Scenes go beside the project by default, where a person can
    /// actually find and share them.</summary>
    private static string DefaultSceneDir()
    {
        string documents = OS.GetSystemDir(OS.SystemDir.Documents);
        string dir = documents.Length > 0 ? $"{documents}/FactoryForge" : "user://";
        if (!DirAccess.DirExistsAbsolute(dir)) DirAccess.MakeDirRecursiveAbsolute(dir);
        return dir;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(1080, 44);
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.KeepSize, 10);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(1080, 44),
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
            TooltipText = "Save this scene to a file you choose (Ctrl+S)",
            CustomMinimumSize = new Vector2(78, 32),
        };
        saveBtn.Pressed += ShowSaveDialog;
        hbox.AddChild(saveBtn);

        var loadBtn = new Button
        {
            Text = "📂 Load",
            TooltipText = "Open a saved scene (Ctrl+O)",
            CustomMinimumSize = new Vector2(78, 32),
        };
        loadBtn.Pressed += ShowLoadDialog;
        hbox.AddChild(loadBtn);

        var driverBtn = new Button
        {
            Text = "⚡ Driver",
            TooltipText = "Connect the scene to a PLC or a driver (F5)",
            CustomMinimumSize = new Vector2(84, 32),
        };
        driverBtn.Pressed += () => EmitSignal(SignalName.DriverRequested);
        hbox.AddChild(driverBtn);

        _demoBtn = new Button
        {
            Text = "🎬 Demo",
            TooltipText = "Run the built-in demo driver — stands down automatically "
                         + "the moment a real driver connects",
            CustomMinimumSize = new Vector2(82, 32),
        };
        _demoBtn.Pressed += () => EmitSignal(SignalName.DemoToggled);
        hbox.AddChild(_demoBtn);

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

        var homeBtn = new Button
        {
            Text = "⌂",   // house
            TooltipText = "Back to the start screen — templates, recent scenes and the key list",
            CustomMinimumSize = new Vector2(40, 32),
        };
        homeBtn.Pressed += () => EmitSignal(SignalName.StartScreenRequested);
        hbox.AddChild(homeBtn);

        hbox.AddChild(new VSeparator());

        _connectionChip = new Label
        {
            CustomMinimumSize = new Vector2(150, 32),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _connectionChip.AddThemeFontSizeOverride("font_size", 12);
        hbox.AddChild(_connectionChip);
    }

    /// <summary>
    /// Polls rather than subscribes to a signal: whether a driver is
    /// connected changes on its own time (a sidecar attaching or dying), not
    /// in response to anything the editor does, so there is no natural event
    /// to hang this off. The chip only actually redraws on a real change.
    /// </summary>
    public override void _Process(double delta)
    {
        if (Demo is not null && Demo.Active != _lastDemoActive)
        {
            _lastDemoActive = Demo.Active;
            _demoBtn.Text = Demo.Active ? "⏹ Demo" : "🎬 Demo";
            _demoBtn.AddThemeColorOverride("font_color",
                Demo.Active ? new Color(0.45f, 0.95f, 0.55f) : Colors.White);
        }

        if (Bus is null) return;

        bool listening = Bus.IsListening;
        bool hasClient = Bus.HasClient;
        string sceneName = Bus.SceneName;
        int tagCount = Bus.Tags?.Count ?? 0;
        int itemCount = Editor?.LiveItemCount ?? 0;
        bool itemCapHit = Editor?.ItemCapHit ?? false;

        if (listening == _lastListening && hasClient == _lastHasClient
            && sceneName == _lastSceneName && tagCount == _lastTagCount
            && itemCount == _lastItemCount && itemCapHit == _lastItemCapHit) return;

        _lastListening = listening;
        _lastHasClient = hasClient;
        _lastSceneName = sceneName;
        _lastTagCount = tagCount;
        _lastItemCount = itemCount;
        _lastItemCapHit = itemCapHit;

        // The live item count matters regardless of connection state — an
        // unattended emitter with nothing downstream leaks cartons whether or
        // not a driver happens to be watching (FF-12).
        string items = $" · {itemCount} item{(itemCount == 1 ? "" : "s")}";

        if (!listening)
        {
            _connectionChip.Text = "● No tag bus" + items;
            _connectionChip.AddThemeColorOverride("font_color", new Color(1.0f, 0.35f, 0.35f));
            _connectionChip.TooltipText = "The tag bus port never bound — another FactoryForge "
                + "engine is probably already running. Close it; no driver can connect until "
                + "you do, however healthy the scene looks.";
        }
        else if (!hasClient)
        {
            _connectionChip.Text = "● No driver" + items;
            _connectionChip.AddThemeColorOverride("font_color", new Color(0.98f, 0.80f, 0.35f));
            _connectionChip.TooltipText = "Listening for a driver on the tag bus. Press F5 to start one.";
        }
        else
        {
            _connectionChip.Text = $"● {sceneName} ({tagCount})" + items;
            _connectionChip.AddThemeColorOverride("font_color", new Color(0.45f, 0.95f, 0.55f));
            _connectionChip.TooltipText = $"A driver is connected — scene '{sceneName}', {tagCount} tags.";
        }

        // The cap warning overrides whatever colour the connection state
        // picked: a leaking scene is the more urgent thing to notice.
        if (itemCapHit)
        {
            _connectionChip.AddThemeColorOverride("font_color", new Color(1.0f, 0.55f, 0.25f));
            _connectionChip.TooltipText += $"\n⚠ Item cap reached ({SceneEditor.LiveItemCap}) — "
                + "the emitter is paused until the count drops. A remover is probably missing "
                + "somewhere on this line.";
        }
    }
}
