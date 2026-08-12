using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// End-to-end check of Run mode: synthesize a real mouse click at a button's
/// screen position and assert the tag moved. Run it with:
///
/// <code>godot --path engine -- --self-test=click</code>
///
/// Needs a display, so it cannot run in headless CI — <c>--self-test=buttons</c>
/// is the headless half, covering the same panel from the tag side.
///
/// This half exists because the two halves fail differently, and only this one
/// catches the failure that actually happened: the press dispatch was correct,
/// the hit test was correct, and clicking still did nothing, because the click
/// handler asked the viewport where the cursor was instead of asking the event
/// where the click was. Every assertion below passed at the level the headless
/// test can see while the feature was, from a user's seat, completely dead.
/// </summary>
public partial class ClickPathSelfTest : Node
{
    public TagTable Tags { get; set; } = null!;
    public SceneEditor? Editor { get; set; }

    private ButtonPanel _panel = null!;
    private Camera3D _camera = null!;
    private int _step;
    private int _highTicks;
    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        foreach (var child in GetParent().GetChildren())
        {
            if (child is ButtonPanel found) { _panel = found; break; }
        }

        if (_panel is null || Editor is null)
        {
            GD.PrintErr("self-test click: no ButtonPanel or editor in the scene");
            GetTree().Quit(1);
        }
    }

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    private bool Tag(string suffix) =>
        Tags.Contains($"panel.{suffix}") && (bool)Tags.Visible($"panel.{suffix}");

    /// <summary>Click where a point on the panel appears on screen, the way a
    /// user would: an event carrying a screen position, pushed through the same
    /// input queue the window feeds.</summary>
    private void ClickAt(Vector3 localPoint)
    {
        Vector2 screen = _camera.UnprojectPosition(_panel.GlobalTransform * localPoint);
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = screen,
            GlobalPosition = screen,
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        // Give the camera a few frames to settle before projecting through it.
        if (_step < 20) return;
        _camera = GetViewport().GetCamera3D();
        if (_camera is null)
        {
            GD.Print("self-test click: SKIPPED (needs a display; use --self-test=buttons headless)");
            GetTree().Quit(0);
            return;
        }

        switch (_step)
        {
            case 20:
                // A click in Edit mode selects and must not press anything.
                Editor!.SetMode(EditorMode.Edit);
                ClickAt(new Vector3(-0.105f, 0.235f, 0.06f));
                break;

            case 24:
                Expect(!Tag("start"), "a click in Edit mode does not press Start");
                Editor!.SetMode(EditorMode.Run);
                break;

            case 30:
                ClickAt(new Vector3(-0.105f, 0.235f, 0.06f));
                _highTicks = 0;
                break;

            case >= 31 and <= 38:
                if (Tag("start")) _highTicks++;
                break;

            case 39:
                Expect(_highTicks == 1,
                       $"a click on the start cap pulses start for exactly 1 tick (saw {_highTicks})");
                Expect(!Tag("stop") && !Tag("reset"), "clicking Start presses nothing else");

                // Bare housing, well away from any cap.
                ClickAt(new Vector3(0.0f, 0.400f, 0.06f));
                _highTicks = 0;
                break;

            case >= 40 and <= 45:
                if (Tag("start") || Tag("stop") || Tag("reset")) _highTicks++;
                break;

            case 46:
                Expect(_highTicks == 0, "clicking the housing presses no button at all");
                Expect(Tag("estop"), "estop still healthy before the mushroom is struck");
                ClickAt(new Vector3(0.0f, 0.105f, 0.06f));
                break;

            case 50:
                Expect(!Tag("estop"), "clicking the mushroom breaks the estop circuit");
                ClickAt(new Vector3(0.0f, 0.105f, 0.06f));
                break;

            case 54:
                Expect(Tag("estop"), "clicking the mushroom again releases it");
                break;

            case 55:
                if (_failures.Count == 0)
                {
                    GD.Print("self-test click: PASS");
                    GetTree().Quit(0);
                }
                else
                {
                    GD.PrintErr($"self-test click: FAIL ({_failures.Count})");
                    GetTree().Quit(1);
                }
                break;
        }
    }
}
