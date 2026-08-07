using Godot;
using FactoryForge.Editor;
using FactoryForge.Scenes;
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

    private SortingScene _scene = new();
    private TagBusServer _bus = null!;
    private double _accumulator;
    private double _runFor = -1;   // seconds; -1 = forever
    private double _elapsed;
    private string? _screenshotPath;
    private double _screenshotAt = -1;

    public override void _Ready()
    {
        _bus = new TagBusServer { Name = "TagBus", Tags = _scene.Tags, SceneName = _scene.Name };
        AddChild(_bus);

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--duration="))
                _runFor = arg.Substring("--duration=".Length).ToFloat();
            else if (arg.StartsWith("--screenshot="))
                _screenshotPath = arg.Substring("--screenshot=".Length);
            else if (arg.StartsWith("--screenshot-at="))
                _screenshotAt = arg.Substring("--screenshot-at=".Length).ToFloat();
        }

        // The renderer is optional: headless CI runs the same scene with no view
        // at all, which is why SceneView only ever reads simulation state.
        if (DisplayServer.GetName() != "headless")
        {
            var view = new SceneView { Name = "View" };
            AddChild(view);
            view.Build(_scene);

            var orbitCam = new OrbitCamera { Name = "OrbitCamera", Current = true };
            var flyCam = new FreeLookCamera { Name = "FreeLookCamera", Current = false };
            AddChild(orbitCam);
            AddChild(flyCam);

            var inspectorUI = new TagInspectorUI { Name = "TagInspectorUI" };
            AddChild(inspectorUI);
            inspectorUI.Setup(_scene.Tags);

            var propertyInspector = new PartPropertyInspectorUI { Name = "PartPropertyInspectorUI" };
            AddChild(propertyInspector);

            var voxelGrid = view.GetNode<VoxelGrid>("VoxelGrid");
            var editor = new SceneEditor
            {
                Name = "SceneEditor",
                Grid = voxelGrid,
                Tags = _scene.Tags,
                TagInspector = inspectorUI,
                PropertyInspector = propertyInspector
            };
            AddChild(editor);
            editor.RegisterDefaultSceneParts();

            var paletteUI = new PartPaletteUI { Name = "PartPaletteUI" };
            paletteUI.PartSelected += (partType) => editor.SetPlacementPart(partType);
            AddChild(paletteUI);

            var wiringUI = new DriverWiringUI { Name = "DriverWiringUI" };
            AddChild(wiringUI);
            wiringUI.Setup(_scene.Tags);

            var toolbarUI = new SceneToolbarUI { Name = "SceneToolbarUI" };
            toolbarUI.SaveRequested += () => editor.SaveSceneToFile();
            toolbarUI.LoadRequested += () => editor.LoadSceneFromFile();
            toolbarUI.ClearRequested += () => editor.ClearAllPlacedParts();
            toolbarUI.WiringRequested += () => wiringUI.ToggleVisibility();
            AddChild(toolbarUI);
        }

        GD.Print($"FactoryForge engine ready — scene '{_scene.Name}', {_scene.Tags.Count} tags");
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

    public override void _Process(double delta)
    {
        // Fixed timestep with a wall-clock accumulator. The scene always
        // advances by exactly TickMs, never by measured elapsed time: a
        // variable dt would make runs non-reproducible and defeat the
        // regression scene. See docs/PLAN.md.
        double interval = _bus.TickMs / 1000.0;
        _accumulator += delta;
        _elapsed += delta;

        int steps = 0;
        while (_accumulator >= interval && steps < MaxCatchUpSteps)
        {
            _scene.Tick(interval);
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
            GD.Print($"done: tick={_bus.TickCount} tall={_scene.SortedTall.Count} " +
                     $"short={_scene.SortedShort.Count} belt={_scene.Boxes.Count}");
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
