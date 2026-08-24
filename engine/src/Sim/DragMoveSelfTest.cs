using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Headless check that a placed part can be dragged to a new spot, and that
/// the drag is one undoable step (OP-08):
///
/// <code>godot --headless --path engine -- --self-test=drag</code>
///
/// This is the interaction everyone tries first and the one the editor did not
/// have. Moving a part needed the M key, which nothing on screen mentioned, so
/// in practice a part placed in the wrong cell was deleted and placed again.
///
/// The three properties worth holding on to, none of which is visible in a
/// screenshot:
///
/// * the part lands where the cursor is, snapped to the same grid placement
///   uses — a part that snapped differently depending on how it got somewhere
///   would have a saved position that depended on how you moved it;
/// * one drag is one Ctrl+Z, not one per motion event;
/// * a drag that never moved anything pushes nothing onto the history, so
///   Ctrl+Z after a plain click still undoes whatever came before the click.
///
/// Rays stand in for the mouse, so this needs no camera and no viewport.
/// </summary>
public partial class DragMoveSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    private readonly List<string> _failures = new();
    private int _step;
    private Vector3 _origin;

    /// <summary>The part dragged: the operator station in the default sorting
    /// scene. Chosen because it stands alone — a ray straight down onto the
    /// belt's origin also passes through the low sensor, whose bounding box
    /// reaches across the lane, and which part such a ray *should* pick is a
    /// question about selection precedence, not about dragging.</summary>
    private const string Target = "panel";

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    /// <summary>A ray straight down onto a point on the work plane — what a
    /// cursor over that cell projects to.</summary>
    private static (Vector3 From, Vector3 Dir) Over(Vector3 planePoint) =>
        (planePoint + new Vector3(0, 6.0f, 0), Vector3.Down);

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        switch (_step)
        {
            case 1:
                Editor.SetMode(EditorMode.Edit);
                if (Editor.PositionOf(Target) is not { } start)
                {
                    Expect(false, $"the default scene has a part called '{Target}'");
                    Finish();
                    return;
                }
                _origin = start;
                break;

            case 2:
            {
                var (from, dir) = Over(_origin);
                Expect(Editor.BeginPartDragAtRay(from, dir),
                       "a press on a part starts a drag");
                Expect(Editor.IsDraggingPart, "the editor reports a drag in progress");
                Expect(Editor.SelectedInstanceId == Target,
                       $"the drag grabbed '{Target}' (got '{Editor.SelectedInstanceId}')");

                // Two motions, one drag: the undo step has to be the whole
                // gesture, not the last hop of it.
                var half = _origin + new Vector3(0.5f, 0, 0.5f);
                var (hf, hd) = Over(half);
                Editor.DragPartToRay(hf, hd);

                var target = _origin + new Vector3(1.0f, 0, 1.0f);
                var (tf, td) = Over(target);
                Editor.DragPartToRay(tf, td);

                Expect(Editor.PositionOf(Target) is { } mid && mid != _origin,
                       "the part follows the cursor while the button is held");
                Editor.EndPartDrag();
                break;
            }

            case 3:
            {
                Expect(!Editor.IsDraggingPart, "releasing ends the drag");
                var moved = Editor.PositionOf(Target);
                Expect(moved is not null && moved.Value != _origin,
                       $"the part stayed where it was dropped (origin {_origin}, now {moved})");
                // Snapped to the same 0.5m grid placement uses, and still on
                // the work plane: a dragged part that drifted off it would
                // hover or sink, and only a saved scene would show it.
                if (moved is { } m)
                {
                    Expect(Mathf.IsEqualApprox(m.Y, Parts.PartLayout.WorkPlaneY),
                           $"the drop stays on the work plane (Y={m.Y})");
                    Expect(Mathf.IsEqualApprox(m.X % 0.5f, 0.0f, 0.001f)
                           && Mathf.IsEqualApprox(m.Z % 0.5f, 0.0f, 0.001f),
                           $"the drop is snapped to the grid ({m.X}, {m.Z})");
                }

                Editor.Undo();
                break;
            }

            case 4:
            {
                Expect(Editor.PositionOf(Target) is { } back && back == _origin,
                       $"one Ctrl+Z puts the part back where it started "
                       + $"(want {_origin}, got {Editor.PositionOf(Target)})");

                // A press that never travels is a click. It must leave the
                // history alone, or every selection would cost an undo step.
                var (from, dir) = Over(_origin);
                Editor.BeginPartDragAtRay(from, dir);
                Editor.EndPartDrag();
                break;
            }

            case 5:
                // Nothing was pushed, so this Undo has nothing to pop and the
                // part must not move. Had the no-op drag recorded a step, this
                // would undo it and land the part back at the *dragged*
                // position -- which is exactly the bug being ruled out.
                Editor.Undo();
                Expect(Editor.PositionOf(Target) is { } still && still == _origin,
                       $"a drag that moved nothing pushes nothing onto the history "
                       + $"(want {_origin}, got {Editor.PositionOf(Target)})");
                break;

            case 6:
            {
                // Run mode is for operating the line, not rebuilding it.
                Editor.SetMode(EditorMode.Run);
                var (from, dir) = Over(_origin);
                Expect(!Editor.BeginPartDragAtRay(from, dir),
                       "Run mode refuses to drag a part");
                Expect(!Editor.IsDraggingPart, "no drag is left in progress after the refusal");
                Editor.SetMode(EditorMode.Edit);
                Finish();
                break;
            }
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("self-test drag: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test drag: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
