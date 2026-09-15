using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// The floating name over a placed part (NV-01).
///
/// A part's instance id **is its tag prefix** — rename a pusher to `reject` and
/// its tags become `reject.extend`, `reject.extended`, `reject.retracted`. That
/// makes the id the single most important thing about a part once you stop
/// building and start writing a program against what you built, and until this
/// existed the only way to read it was to click each part in turn and look at
/// the property panel.
///
/// The label is a **child of the part**, not of the editor, so it follows every
/// move, drag, nudge and rotation for free and is freed with the part it names.
/// That is also why it is a <see cref="Label3D"/> and not a 2D overlay: an
/// overlay would need a projection per part per frame and would have to be told
/// about every one of those gestures.
///
/// It does not disturb anything that measures a part.
/// <see cref="PartBounds"/> accumulates <c>MeshInstance3D</c> only, and a
/// <c>Label3D</c> is not one — so the selection outline, the click box and the
/// duplicate offset are all unchanged by a part having a name over it.
/// </summary>
public static class PartNameLabel
{
    private const string NodeName = "PartNameLabel";

    /// <summary>Clearance above the part's own geometry. Enough that a label
    /// does not sit on the thing it names, small enough that it still reads as
    /// belonging to it rather than floating over the scene.</summary>
    private const float Clearance = 0.14f;

    /// <summary>Give a part its name tag, or update the one it has.</summary>
    public static void Apply(Node3D part, string instanceId, bool visible)
    {
        var label = part.GetNodeOrNull<Label3D>(NodeName);
        if (label is null)
        {
            label = new Label3D
            {
                Name = NodeName,
                FontSize = 48,
                // Fixed on screen rather than scaled by distance. A label whose
                // size depends on how far away it is makes the near end of a
                // line shout and the far end unreadable, and the thing being
                // read here is a name, not a feature of the machine.
                FixedSize = true,
                PixelSize = 0.00058f,
                Modulate = new Color(0.98f, 0.86f, 0.42f),
                OutlineSize = 14,
                OutlineModulate = new Color(0.04f, 0.05f, 0.06f, 0.85f),
                // Billboarded, so a name is readable from wherever the camera
                // happens to be — the one thing on a part that should not have
                // a front.
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                // Drawn through whatever is in front of it. A name you can only
                // read when nothing is in the way is a name you cannot use to
                // find the part hidden behind the conveyor.
                NoDepthTest = true,
                RenderPriority = 2,
            };
            part.AddChild(label);
        }

        label.Text = instanceId;
        label.Visible = visible;
        Reposition(part, label);
    }

    /// <summary>Show or hide the name a part already has.</summary>
    public static void SetVisible(Node3D part, bool visible)
    {
        if (part.GetNodeOrNull<Label3D>(NodeName) is not { } label) return;
        label.Visible = visible;
        if (visible) Reposition(part, label);
    }

    /// <summary>
    /// Sit the label over the top of the part, in the part's own space.
    ///
    /// Measured rather than assumed, and re-measured on every apply: a belt
    /// resized in the property panel, or a guard door given a longer travel,
    /// changes the height its name should clear. The label's own rotation is
    /// cancelled so a turned part does not tip its name over with it.
    /// </summary>
    private static void Reposition(Node3D part, Label3D label)
    {
        // Hide the label while measuring would be pointless — PartBounds reads
        // meshes and a Label3D is not one — but the label must not be counted
        // by anything else either, which is why it is named and looked up by
        // name rather than being "the last child".
        Aabb bounds = PartBounds.Measure(part);
        label.Position = new Vector3(0, bounds.End.Y + Clearance, 0);
    }
}
