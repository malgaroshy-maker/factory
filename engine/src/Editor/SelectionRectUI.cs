using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// The box-select rectangle (ES-02).
///
/// A <c>Node3D</c> cannot draw a rectangle in screen pixels, so
/// <see cref="SceneEditor"/> emits where the box is and this draws it. Keeping
/// the geometry in the editor and the drawing here means the rectangle on
/// screen and the rectangle parts are tested against are the same numbers —
/// a second, private copy of the drag bounds is exactly how a selection box
/// ends up selecting things it does not cover.
///
/// It is mouse-transparent throughout: the box is drawn *over* the viewport
/// while the drag that creates it is still going on, and a Control that ate
/// the release would leave the box on screen for ever.
/// </summary>
public partial class SelectionRectUI : Control
{
    private Rect2 _rect;
    private bool _active;

    private static readonly Color Fill = new(0.25f, 0.85f, 1.0f, 0.12f);
    private static readonly Color Edge = new(0.35f, 0.90f, 1.0f, 0.85f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void SetRect(Rect2 rect, bool active)
    {
        _rect = rect;
        _active = active;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active) return;
        DrawRect(_rect, Fill, filled: true);
        DrawRect(_rect, Edge, filled: false, width: 1.0f);
    }
}
