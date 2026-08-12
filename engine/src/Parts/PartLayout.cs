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
}
