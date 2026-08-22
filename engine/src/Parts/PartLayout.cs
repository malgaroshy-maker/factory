namespace FactoryForge.Parts;

/// <summary>
/// The mounting convention every part shares.
///
/// A part's local origin sits on the **work plane** — the same plane the scene
/// editor snaps placements to (see <c>SceneEditor.UpdatePreviewPosition</c>).
/// Each part offsets its own geometry from there, so dropping any part on a grid
/// point puts it at the right height with no per-part fudging, and a scene file
/// only ever stores grid-aligned coordinates.
///
/// X and Z are snapped to <see cref="View.VoxelGrid.CellSize"/>; Y is always the
/// work plane. That is why every part in the default scene sits at y = 0.5.
/// </summary>
public static class PartLayout
{
    /// <summary>Height of the conveyor work plane above the floor.</summary>
    public const float WorkPlaneY = 0.5f;

    /// <summary>Conveyor frame thickness; the belt's origin is its centre line.</summary>
    public const float BeltThickness = 0.12f;

    /// <summary>Carrying surface, relative to a part's origin on the work plane.</summary>
    public const float BeltSurface = BeltThickness / 2.0f;

    /// <summary>Distance from the work plane down to the floor, for support legs.</summary>
    public const float FloorDrop = WorkPlaneY;

    // --- Reference dimensions, sourced from real equipment, so new parts get
    // built to the same scale system instead of each guessing independently.
    // See FF-25 -- a control panel modelled to no shared reference read as
    // roughly as tall as the transport line it stood next to.

    /// <summary>Conveyor carrying-surface width. <see cref="Parts.ConveyorBelt"/>'s
    /// default <c>Size</c> already matches this; new transport parts should too.</summary>
    public const float StandardBeltWidth = 0.5f;

    /// <summary>A waist-height pushbutton station's housing footprint --
    /// enough for a row of 22 mm caps, no wider.</summary>
    public const float PanelWidth = 0.40f;
    public const float PanelHeight = 0.60f;

    /// <summary>Stack light lamp dome diameter, matching a standard 70 mm
    /// beacon once its curvature reads in the render.</summary>
    public const float StackLightDiameter = 0.08f;
}
