using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Headless check that the panel's setpoint pot is a real control (OP-01,
/// OP-02):
///
/// <code>godot --headless --path engine -- --self-test=setpoint</code>
///
/// The pot is the first control in the project a mouse <i>drags</i> rather than
/// clicks, and the first whose tag is written in both directions — the knob
/// drives the tag, a forced tag drives the knob. Both halves are easy to
/// half-implement in a way that looks right on screen: a knob that turns but
/// never publishes, or a tag that publishes but never turns the knob, and
/// neither shows up in a screenshot.
///
/// Rays are built from the panel's own transform rather than a camera, the
/// same way <see cref="PanelSelfTest"/> does it, so this needs no viewport.
/// </summary>
public partial class SetpointDialSelfTest : Node
{
    public TagTable Tags { get; set; } = null!;
    public SceneEditor Editor { get; set; } = null!;

    /// <summary>What the default scene's panel is configured with — the
    /// diverter timing pot (see SceneEditor.RegisterDefaultSceneParts). The
    /// literals are here on purpose: a test that read the range off the panel
    /// it is testing would pass no matter what the panel said.</summary>
    private const float Min = 0.30f;
    private const float Max = 1.80f;
    private const float Default = 0.90f;
    private const string Id = "panel.setpoint";

    /// <summary>Where the knob sits on the face. Mirrors what ButtonPanel
    /// derives from PartLayout (PanelWidth * 0.325, PanelHeight * 0.233) — the
    /// same black-box convention PanelSelfTest uses for the caps.</summary>
    private static readonly Vector3 DialCentre = new(0.13f, 0.1398f, 0.06f);

    private ButtonPanel _panel = null!;
    private int _step;
    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        foreach (var child in GetParent().GetChildren())
        {
            if (child is ButtonPanel found) { _panel = found; break; }
        }

        if (_panel is null)
        {
            GD.PrintErr("self-test: no ButtonPanel in the default scene");
            GetTree().Quit(1);
        }
    }

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    private float TagValue() =>
        Tags.Contains(Id) ? (float)System.Convert.ToDouble(Tags.Visible(Id)) : float.NaN;

    /// <summary>A ray aimed straight at a point on the panel's face, from
    /// outside it, in whatever direction the panel is currently facing.</summary>
    private (Vector3 From, Vector3 Dir) Aim(Vector3 local)
    {
        Vector3 outward = _panel.GlobalTransform.Basis.Z.Normalized();
        Vector3 target = _panel.GlobalTransform * local;
        return (target + outward * 1.5f, -outward);
    }

    private bool DialHit(Vector3 local)
    {
        var (from, dir) = Aim(local);
        return _panel.HitTestDial(from, dir);
    }

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        switch (_step)
        {
            case 1:
                Expect(Tags.Contains(Id), $"tag {Id} exists");
                Expect(Tags.Get(Id)?.Type == TagType.Float, $"{Id} is a Float, not a bit");
                Expect(Tags.Get(Id)?.Kind == TagKind.Input,
                       $"{Id} is an Input — the operator drives it, the controller reads it");
                Expect(Mathf.IsEqualApprox(_panel.Setpoint, Default),
                       $"the pot starts at the template's value (want {Default}, got {_panel.Setpoint})");
                break;

            case 2:
                // One tick of dispatch is all it should take for the knob to
                // reach the bus.
                Expect(Mathf.IsEqualApprox(TagValue(), Default),
                       $"the knob publishes itself (want {Default}, got {TagValue()})");
                CheckHitTest();
                CheckClamping();
                CheckModeGuard();
                break;

            case 3:
                // Clamping left the pot at the top of its range; the tag has to
                // agree, or the panel and the controller are aiming at
                // different numbers.
                Expect(Mathf.IsEqualApprox(TagValue(), Max),
                       $"a turned knob republishes (want {Max}, got {TagValue()})");
                // Now the other direction: a force is the authority and the
                // pointer follows it, so a setpoint changed over the wire is
                // visible on the panel instead of leaving the knob lying.
                Tags.Force(Id, (double)1.20f);
                break;

            case 4:
                Expect(Mathf.IsEqualApprox(_panel.Setpoint, 1.20f),
                       $"a forced tag turns the knob (want 1.20, got {_panel.Setpoint})");
                break;

            case 5:
            {
                Expect(Tags.IsForced(Id), "the force is still in place a tick later");
                Expect(Mathf.IsEqualApprox(_panel.Setpoint, 1.20f),
                       "the knob does not drift back while the tag is forced");

                // Taking hold of the knob takes the tag back. A force is sticky
                // everywhere else in the editor and deliberately is not here.
                Editor.SetMode(EditorMode.Run);
                var (from, dir) = Aim(DialCentre);
                Expect(Editor.BeginDialDragAtRay(from, dir), "Run mode grabs the knob");
                Expect(Editor.IsDraggingDial, "the editor reports a drag in progress");
                Expect(!Tags.IsForced(Id), "grabbing the knob clears the force on its tag");
                break;
            }

            case 6:
                // A drag is measured in screen pixels, positive upward, so
                // enough travel must reach either stop and go no further.
                Editor.DragDial(1000.0f);
                Expect(Mathf.IsEqualApprox(_panel.Setpoint, Max),
                       $"dragging up runs the pot to its stop (want {Max}, got {_panel.Setpoint})");
                Editor.DragDial(-1000.0f);
                Expect(Mathf.IsEqualApprox(_panel.Setpoint, Min),
                       $"dragging down runs it to the other stop (want {Min}, got {_panel.Setpoint})");
                Editor.EndDialDrag();
                Expect(!Editor.IsDraggingDial, "releasing ends the drag");
                Editor.SetMode(EditorMode.Edit);
                break;

            case 7:
                Expect(Mathf.IsEqualApprox(TagValue(), Min),
                       $"the tag followed the drag down (want {Min}, got {TagValue()})");
                break;

            case 8:
                if (_failures.Count == 0)
                {
                    GD.Print("self-test setpoint: PASS");
                    GetTree().Quit(0);
                }
                else
                {
                    GD.PrintErr($"self-test setpoint: FAIL ({_failures.Count})");
                    GetTree().Quit(1);
                }
                break;
        }
    }

    private void CheckHitTest()
    {
        CheckHitTestAtCurrentRotation("as placed");

        // A panel can be turned to any heading in the editor, and a hit test
        // that quietly assumed world axes would pass the first pass and miss
        // the knob entirely in the second.
        var was = _panel.Rotation;
        _panel.Rotation = new Vector3(0, 2.1f, 0);
        _panel.ForceUpdateTransform();
        CheckHitTestAtCurrentRotation("rotated");
        _panel.Rotation = was;
        _panel.ForceUpdateTransform();
    }

    private void CheckHitTestAtCurrentRotation(string when)
    {
        Expect(DialHit(DialCentre), $"a ray at the knob hits it ({when})");

        // The knob must not swallow the rest of the panel, and the mushroom
        // beside it must not swallow the knob. Both would be invisible on
        // screen and maddening in the hand.
        Expect(!DialHit(new Vector3(0.0f, 0.1398f, 0.06f)),
               $"the mushroom is not the knob ({when})");
        Expect(!DialHit(new Vector3(0.12f, 0.3132f, 0.06f)),
               $"the Reset cap above it is not the knob ({when})");
        Expect(!DialHit(new Vector3(0.0f, 0.50f, 0.06f)),
               $"the housing is not the knob ({when})");

        var (from, dir) = Aim(DialCentre);
        Expect(_panel.HitTest(from, dir) is null,
               $"a ray at the knob presses no button ({when})");
    }

    private void CheckClamping()
    {
        _panel.SetSetpoint(Max + 50.0f);
        Expect(Mathf.IsEqualApprox(_panel.Setpoint, Max),
               $"the pot stops at its top (want {Max}, got {_panel.Setpoint})");
        _panel.SetSetpoint(Min - 50.0f);
        Expect(Mathf.IsEqualApprox(_panel.Setpoint, Min),
               $"the pot stops at its bottom (want {Min}, got {_panel.Setpoint})");
        _panel.SetSetpoint(Max);
    }

    /// <summary>Edit mode is for building the line, not operating it. A knob
    /// that turned while you were dragging a conveyor into place would be the
    /// same half-a-mode UX-44 closed for clicks.</summary>
    private void CheckModeGuard()
    {
        Editor.SetMode(EditorMode.Edit);
        var (from, dir) = Aim(DialCentre);
        Expect(!Editor.BeginDialDragAtRay(from, dir), "Edit mode refuses to turn the knob");
        Expect(!Editor.IsDraggingDial, "no drag is left in progress after the refusal");
    }
}
