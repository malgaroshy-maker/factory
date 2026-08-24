using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// End-to-end check of drag-to-move: synthesize a real press, motion and
/// release at screen positions and assert the part ended up where the cursor
/// did. Run it with:
///
/// <code>godot --path engine -- --self-test=dragpath</code>
///
/// Needs a display, so it cannot run in headless CI — <c>--self-test=drag</c>
/// is the headless half, covering the same move from the ray seam inward.
///
/// This half exists for the same reason <see cref="ClickPathSelfTest"/> does,
/// and it guards the one segment the headless test cannot reach: screen pixels
/// into a drag. That segment contains a threshold (a press has to travel
/// before it counts as a drag rather than a click) and a selection that has to
/// pick the part under the <i>press</i> rather than under wherever the cursor
/// happens to be when the event is processed — both invisible from the ray
/// seam, and both able to leave dragging completely dead while every headless
/// assertion still passes.
/// </summary>
public partial class DragPathSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;

    /// <summary>The operator station: it stands alone in the default scene, so
    /// a ray onto it is unambiguous about which part it grabs.</summary>
    private const string Target = "panel";

    private Camera3D _camera = null!;
    private int _step;
    private Vector3 _origin;
    private Vector2 _pressAt;
    private readonly List<string> _failures = new();

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    /// <summary>Where a world point lands in window pixels — what a real OS
    /// event would carry. <see cref="Camera3D.UnprojectPosition"/> answers in
    /// viewport space, and Godot's <c>canvas_items</c> stretch mode rescales a
    /// real click from window space into it before any script sees it, so an
    /// event built straight from UnprojectPosition gets that rescale applied
    /// twice (see ClickPathSelfTest, FF-32).</summary>
    private Vector2 ScreenOf(Vector3 worldPoint)
    {
        Vector2 viewportPos = _camera.UnprojectPosition(worldPoint);
        Vector2 windowSize = DisplayServer.WindowGetSize();
        Vector2 viewportSize = _camera.GetViewport().GetVisibleRect().Size;
        return viewportPos * (windowSize / viewportSize);
    }

    private static void Press(Vector2 at, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
            Position = at,
            GlobalPosition = at,
        });

    private void MoveTo(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Position = at,
            GlobalPosition = at,
            Relative = at - _pressAt,
        });
        _pressAt = at;
    }

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        // Give the camera a few frames to settle before projecting through it.
        if (_step < 20) return;
        _camera = GetViewport().GetCamera3D();
        if (_camera is null)
        {
            GD.Print("self-test dragpath: SKIPPED (needs a display; use --self-test=drag headless)");
            GetTree().Quit(0);
            return;
        }

        switch (_step)
        {
            case 20:
                Editor.SetMode(EditorMode.Edit);
                if (Editor.PositionOf(Target) is not { } start)
                {
                    Expect(false, $"the default scene has a part called '{Target}'");
                    Finish();
                    return;
                }
                _origin = start;
                break;

            case 24:
                // Aim a little above the work plane so the press lands on the
                // part's body rather than skimming its base.
                _pressAt = ScreenOf(_origin + new Vector3(0, 0.25f, 0));
                Press(_pressAt, pressed: true);
                break;

            case 26:
                Expect(Editor.SelectedInstanceId == Target,
                       $"the press selected '{Target}' (got '{Editor.SelectedInstanceId}')");
                break;

            case 28:
                // A motion of a couple of pixels is a shaky hand, not a drag.
                MoveTo(_pressAt + new Vector2(3, 2));
                break;

            case 30:
                Expect(Editor.PositionOf(Target) is { } unmoved && unmoved == _origin,
                       "a 3-pixel wobble does not move the part");
                MoveTo(ScreenOf(_origin + new Vector3(1.0f, 0.25f, 1.0f)));
                break;

            case 32:
                Expect(Editor.PositionOf(Target) is { } dragging && dragging != _origin,
                       "the part follows the cursor once the press has travelled");
                Press(_pressAt, pressed: false);
                break;

            case 36:
            {
                var dropped = Editor.PositionOf(Target);
                Expect(dropped is not null && dropped.Value != _origin,
                       $"the part stayed where it was dropped (origin {_origin}, now {dropped})");
                if (dropped is { } d)
                {
                    Expect(Mathf.IsEqualApprox(d.Y, PartLayout.WorkPlaneY),
                           $"the drop stays on the work plane (Y={d.Y})");
                    // Not an exact cell: where the cursor lands depends on the
                    // camera, and pinning it would test the camera rather than
                    // the drag. Roughly the right direction, snapped, is the
                    // honest claim from a synthesized screen position.
                    Expect(d.X > _origin.X && d.Z > _origin.Z,
                           $"the part moved the way the cursor did (from {_origin} to {d})");
                }
                Editor.Undo();
                break;
            }

            case 40:
                Expect(Editor.PositionOf(Target) is { } back && back == _origin,
                       $"one Ctrl+Z puts it back (want {_origin}, got {Editor.PositionOf(Target)})");
                Finish();
                break;
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("self-test dragpath: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test dragpath: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
