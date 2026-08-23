using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Assert the Edit/Run contract as a pair (UX-44): a click selects in Edit
/// and does not in Run, a control operates in Run and does not in Edit, and
/// entering Run clears both the placement preview and the selection.
///
/// <code>godot --headless --path engine -- --self-test=modes</code>
///
/// <c>--self-test=click</c> (needs a display) and <c>--self-test=buttons</c>
/// (headless) each already cover one half of this -- a real click reaching a
/// tag, and a panel's own press behaviour -- but neither asserts the switch
/// itself: that the *same* action means something in one mode and nothing in
/// the other. Drives <see cref="SceneEditor.SelectPartAtRay"/> and
/// <see cref="SceneEditor.PressControlAtRay"/> directly with synthetic rays,
/// the same technique <c>--self-test=operate</c> (UX-37) uses, since headless
/// has no camera to project a screen click through.
/// </summary>
public partial class ModeSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    private readonly System.Collections.Generic.List<string> _failures = new();
    private int _step;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    private static (Vector3 From, Vector3 Dir) RayThrough(Node3D node) =>
        (node.GlobalPosition + Vector3.Up * 5f, Vector3.Down);

    public override void _PhysicsProcess(double delta)
    {
        _step++;
        if (_step != 2) return;

        // Not the conveyor: sensor_low sits directly above it on the same
        // work-plane column, so a straight-down ray hits the sensor's box
        // first -- a real overlap between two placed parts, not a bug in
        // either select or operate. The pusher's own column is clear of
        // every other default-scene part.
        PusherMechanism? pusher = null;
        foreach (var child in GetParent().GetChildren())
        {
            if (child is PusherMechanism p) { pusher = p; break; }
        }
        if (pusher is null)
        {
            Expect(false, "the default scene has a pusher to test against");
            Finish();
            return;
        }

        var ray = RayThrough(pusher);

        // "A click selects in Edit..."
        Editor.SetMode(EditorMode.Edit);
        Editor.SelectPartAtRay(ray.From, ray.Dir);
        Expect(Editor.SelectedInstanceId == "pusher", "Edit mode: a click on the pusher selects it");

        // "...and does not in Run." Entering Run clears the prior selection
        // on its own (checked first); with nothing selected, a further click
        // in Run mode has to stay that way.
        Editor.SetMode(EditorMode.Run);
        Expect(Editor.SelectedInstanceId is null, "entering Run clears the prior selection");
        Editor.SelectPartAtRay(ray.From, ray.Dir);
        Expect(Editor.SelectedInstanceId is null, "Run mode: a click on the pusher does not select it");

        // "A control operates in Run..."
        Expect(!Bit("pusher.extend"), "pusher.extend starts low");
        Editor.PressControlAtRay(ray.From, ray.Dir);
        Expect(Bit("pusher.extend"), "Run mode: a click on the pusher strokes it out");

        // "...and does not in Edit."
        Editor.SetMode(EditorMode.Edit);
        Editor.PressControlAtRay(ray.From, ray.Dir);
        Expect(Bit("pusher.extend"), "Edit mode: a click on the pusher leaves extend exactly as it was");

        // "Entering Run clears the preview and the selection" -- the
        // selection half is covered above; the preview half here, so this
        // file states the whole pairwise contract rather than half of it.
        Editor.SetMode(EditorMode.Edit);
        Editor.SetPlacementPart("ConveyorBelt");
        Expect(Editor.HasPlacementPreview, "Edit mode: picking a palette part starts a placement preview");
        Editor.SetMode(EditorMode.Run);
        Expect(!Editor.HasPlacementPreview, "entering Run clears an in-progress placement preview");
        Editor.SetPlacementPart("ConveyorBelt");
        Expect(!Editor.HasPlacementPreview, "Run mode refuses to start a new placement");

        Editor.SetMode(EditorMode.Edit);
        Finish();
    }

    private bool Bit(string id) => Tags.Contains(id) && (bool)Tags.Visible(id);

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("self-test modes: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test modes: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
