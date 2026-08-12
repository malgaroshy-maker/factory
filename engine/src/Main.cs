using Godot;
using FactoryForge.Editor;
using FactoryForge.Scenes;
using FactoryForge.Sim;
using FactoryForge.TagBus;
using FactoryForge.View;

namespace FactoryForge;

/// <summary>
/// Engine entry point: owns the scene and the tag bus, and drives the fixed
/// timestep. Headless-friendly so CI can run it without a GPU.
/// </summary>
public partial class Main : Node
{
    /// <summary>Cap on catch-up steps per frame. Beyond this the backlog is
    /// dropped rather than spiralling: a simulation that can never keep up
    /// should run visibly slow, not freeze.</summary>
    private const int MaxCatchUpSteps = 5;

    private SortingScene? _scene;
    private SceneEditor? _editor;
    private SimulationControls _sim = null!;
    private bool _deterministic;
    private float _timeScale = 1.0f;

    /// <summary>Both scenes present the same tag interface, so they report the
    /// same name on the bus — a driver cannot and need not tell them apart.</summary>
    private const string SceneName = "sorting-by-height";
    private TagBusServer _bus = null!;
    private double _accumulator;
    private double _runFor = -1;   // seconds; -1 = forever
    private double _elapsed;
    private double _startedAt;
    private string? _screenshotPath;
    private double _screenshotAt = -1;
    private string? _selfTest;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--duration="))
                _runFor = arg.Substring("--duration=".Length).ToFloat();
            else if (arg.StartsWith("--screenshot="))
                _screenshotPath = arg.Substring("--screenshot=".Length);
            else if (arg.StartsWith("--screenshot-at="))
                _screenshotAt = arg.Substring("--screenshot-at=".Length).ToFloat();
            else if (arg == "--deterministic")
                _deterministic = true;
            else if (arg == "--physics")
                _deterministic = false;   // kept: it used to be the opt-in flag
            else if (arg.StartsWith("--time-scale="))
                _timeScale = arg.Substring("--time-scale=".Length).ToFloat();
            else if (arg.StartsWith("--self-test="))
                _selfTest = arg.Substring("--self-test=".Length);
        }

        // Rigid bodies by default — the parts are real colliders, so properties,
        // collisions and a held-out pusher all behave physically.
        //
        // --deterministic swaps in the fixed-timestep scene instead. That one is
        // the regression contract: it advances by exactly TickMs and mirrors
        // harness/scene.py, so the same sidecar drives both engines to the same
        // counts. Jolt cannot promise that, so anything asserting exact numbers
        // (tools/drive_engine.py, CI) must ask for it explicitly.
        _scene = _deterministic ? new SortingScene() : null;

        var tags = new TagTable();
        if (_scene is not null) tags = _scene.Tags;
        else SortingTags.Declare(tags);

        _bus = new TagBusServer { Name = "TagBus", Tags = tags, SceneName = SceneName };
        AddChild(_bus);

        _sim = new SimulationControls { Name = "SimulationControls" };
        AddChild(_sim);
        if (_timeScale > 0.0f) _sim.SetRate(_timeScale);
        _startedAt = Time.GetTicksMsec() / 1000.0;

        // The renderer is optional: headless CI runs the same scene with no view
        // at all, which is why SceneView only ever reads simulation state.
        if (DisplayServer.GetName() != "headless")
        {
            BuildView(tags);
        }
        else if (!_deterministic)
        {
            // Headless physics still needs a floor to land on and parts to run.
            StudioEnvironment.AddFloor(this, withGrid: false);
            BuildHeadlessPhysicsParts(tags);
        }

        GD.Print($"FactoryForge engine ready — {(_deterministic ? "DETERMINISTIC" : "PHYSICS")} " +
                 $"scene '{SceneName}', {tags.Count} tags");

        // Added last so it runs after the editor each tick, and therefore reads
        // the tags as the part dispatch left them rather than a tick behind.
        if (_selfTest == "buttons")
        {
            AddChild(new PanelSelfTest { Name = "PanelSelfTest", Tags = tags, Editor = _editor });
        }
        if (_selfTest == "io")
        {
            AddChild(new IoSelfTest { Name = "IoSelfTest", Tags = tags, Editor = _editor });
        }
        if (_selfTest == "click")
        {
            AddChild(new ClickPathSelfTest { Name = "ClickPathSelfTest", Tags = tags, Editor = _editor });
        }
    }

    private void BuildView(TagTable tags)
    {
        StudioEnvironment.AddEnvironment(this);
        StudioEnvironment.AddFloor(this);

        // The ghost-box view exists only for the deterministic scene; the
        // physics scene's cartons are real nodes that draw themselves.
        if (_scene is not null)
        {
            var view = new SceneView { Name = "View" };
            AddChild(view);
            view.Build(_scene);
        }

        AddChild(new OrbitCamera { Name = "OrbitCamera", Current = true });
        AddChild(new FreeLookCamera { Name = "FreeLookCamera", Current = false });

        var inspectorUI = new TagInspectorUI { Name = "TagInspectorUI" };
        AddChild(inspectorUI);
        inspectorUI.Setup(tags);

        var propertyInspector = new PartPropertyInspectorUI { Name = "PartPropertyInspectorUI" };
        AddChild(propertyInspector);

        var editor = new SceneEditor
        {
            Name = "SceneEditor",
            Grid = GetNode<VoxelGrid>("VoxelGrid"),
            Tags = tags,
            Scene = _scene,
            TagInspector = inspectorUI,
            PropertyInspector = propertyInspector
        };
        AddChild(editor);
        propertyInspector.Editor = editor;
        editor.RegisterDefaultSceneParts(physical: !_deterministic);
        _editor = editor;

        var paletteUI = new PartPaletteUI { Name = "PartPaletteUI" };
        paletteUI.PartSelected += (partType) => editor.SetPlacementPart(partType);
        AddChild(paletteUI);

        var wiringUI = new DriverWiringUI { Name = "DriverWiringUI" };
        AddChild(wiringUI);
        wiringUI.Setup(tags);

        var driverConnectionUI = new DriverConnectionUI { Name = "DriverConnectionUI" };
        AddChild(driverConnectionUI);

        var toolbarUI = new SceneToolbarUI { Name = "SceneToolbarUI" };
        toolbarUI.SaveRequested += (path) => editor.SaveSceneToFile(path);
        toolbarUI.LoadRequested += (path) =>
        {
            editor.LoadSceneFromFile(path);
            // A loaded scene is a different scene: republish under its own name
            // so a connected driver is not still told this is the sorting demo.
            _bus.SceneName = editor.SceneName;
            _bus.SendDescribe();
        };
        toolbarUI.ClearRequested += () => editor.ClearAllPlacedParts();
        toolbarUI.WiringRequested += () => wiringUI.ToggleVisibility();
        toolbarUI.DriverRequested += () => driverConnectionUI.ToggleVisibility();
        toolbarUI.PauseToggled += () => _sim.TogglePause();
        toolbarUI.ResetRequested += ResetSimulation;
        toolbarUI.RateSelected += (rate) => _sim.SetRate(rate);
        toolbarUI.ModeToggled += () => editor.ToggleMode();
        AddChild(toolbarUI);

        // Mode lives on the editor; the toolbar and palette only reflect it, so
        // the F1 key and the button can never disagree about which mode we are in.
        // Placing, deleting or renaming a part changes the I/O list. Republish
        // it, or a driver connected while you build keeps working from the tag
        // list it was handed at connect time and never sees the new parts.
        editor.TagsChanged += () =>
        {
            wiringUI.RebuildWiringList();
            _bus.SendDescribe();
        };

        editor.ModeChanged += (running) =>
        {
            toolbarUI.ShowMode(running);
            paletteUI.ShowForMode(running);
        };
        toolbarUI.ShowMode(editor.Mode == EditorMode.Run);
        paletteUI.ShowForMode(editor.Mode == EditorMode.Run);

        // The toolbar mirrors the state rather than owning it, so the keyboard
        // shortcuts and the buttons can never disagree.
        _sim.StateChanged += (paused, rate) => toolbarUI.ShowState(paused, rate);
        toolbarUI.ShowState(_sim.Paused, _sim.Rate);
    }

    /// <summary>Physics mode without a renderer: no UI, but the parts still have
    /// to exist or nothing moves.</summary>
    private void BuildHeadlessPhysicsParts(TagTable tags)
    {
        var editor = new SceneEditor { Name = "SceneEditor", Tags = tags };
        AddChild(editor);
        editor.RegisterDefaultSceneParts(physical: true);
        _editor = editor;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (DisplayServer.GetName() == "headless") return;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.F4)
            {
                var wiringUI = GetNodeOrNull<DriverWiringUI>("DriverWiringUI");
                wiringUI?.ToggleVisibility();
            }
            else if (keyEvent.Keycode == Key.F5)
            {
                var driverConnectionUI = GetNodeOrNull<DriverConnectionUI>("DriverConnectionUI");
                driverConnectionUI?.ToggleVisibility();
            }
            else if (keyEvent.Keycode == Key.Space)
            {
                _sim.TogglePause();
                GD.Print(_sim.Paused ? "Simulation paused" : "Simulation running");
            }
            else if (keyEvent.CtrlPressed && keyEvent.Keycode == Key.R)
            {
                ResetSimulation();
            }
            else if (keyEvent.Keycode == Key.C)
            {
                var orbitCam = GetNode<OrbitCamera>("OrbitCamera");
                var flyCam = GetNode<FreeLookCamera>("FreeLookCamera");

                if (orbitCam.Current)
                {
                    flyCam.GlobalTransform = orbitCam.GlobalTransform;
                    flyCam.SyncRotationFromTransform();
                    flyCam.MakeCurrent();
                    GD.Print("Camera mode: Free-Look Fly Camera (WASD + Right-Click drag)");
                }
                else
                {
                    orbitCam.MakeCurrent();
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    GD.Print("Camera mode: Orbit Camera (Left-Click drag + Scroll wheel)");
                }
            }
        }
    }

    /// <summary>Restart the run without disturbing the scene you built: boxes
    /// cleared, counters zeroed, machines left where they are.</summary>
    private void ResetSimulation()
    {
        _scene?.Reset();
        _editor?.ResetItems();
        _bus.SendUpdates();
        GD.Print("Simulation reset");
    }

    public override void _Process(double delta)
    {
        // Fixed timestep with a wall-clock accumulator. The scene always
        // advances by exactly TickMs, never by measured elapsed time: a
        // variable dt would make runs non-reproducible and defeat the
        // regression scene. See docs/PLAN.md.
        // The accumulator runs on *simulation* time, so the time-scale control
        // speeds the line up and down. --duration and --screenshot-at run on
        // wall-clock: they are harness controls, and a paused run whose clock
        // also stopped would never reach its duration and would hang forever.
        double interval = _bus.TickMs / 1000.0;
        _accumulator += delta;
        _elapsed = Time.GetTicksMsec() / 1000.0 - _startedAt;

        int steps = 0;
        while (_accumulator >= interval && steps < MaxCatchUpSteps)
        {
            // The physics scene advances itself on Jolt's clock; this loop then
            // only paces the bus.
            _scene?.Tick(interval);
            _bus.CountTick();
            _accumulator -= interval;
            steps++;
        }
        if (_accumulator > interval * MaxCatchUpSteps) _accumulator = 0;
        if (steps > 0) _bus.SendUpdates();

        if (_screenshotPath is not null && _screenshotAt >= 0 && _elapsed >= _screenshotAt)
        {
            _screenshotAt = -1;
            CallDeferred(nameof(SaveScreenshot));
        }

        if (_runFor > 0 && _elapsed >= _runFor)
        {
            if (_scene is { } scene)
                GD.Print($"done: tick={_bus.TickCount} tall={scene.SortedTall.Count} " +
                         $"short={scene.SortedShort.Count} belt={scene.Boxes.Count}");
            else
                GD.Print($"done: tick={_bus.TickCount} " +
                         $"tall={_bus.Tags.Visible(SortingTags.CounterTall)} " +
                         $"short={_bus.Tags.Visible(SortingTags.CounterShort)}");
            GetTree().Quit();
        }
    }

    private void SaveScreenshot()
    {
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(_screenshotPath);
        GD.Print(err == Error.Ok
            ? $"screenshot saved to {_screenshotPath}"
            : $"screenshot failed: {err}");
    }
}
