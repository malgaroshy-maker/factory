using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Measures what a part actually occupies, rather than assuming it.
///
/// Parts mount their geometry at an offset from their origin — the chute's deck
/// hangs below and forward of its anchor, the stack light's tower drops to the
/// floor, the belt runs three metres from its centre. Anything that reasons
/// about a part's extent (the selection outline, click picking) has to measure
/// the meshes, or it ends up reasoning about a point.
/// </summary>
public static class PartBounds
{
    /// <summary>Fallback for a part that draws nothing of its own, so it is
    /// still selectable.</summary>
    private static readonly Aabb Fallback =
        new(new Vector3(-0.15f, -0.15f, -0.15f), new Vector3(0.3f, 0.3f, 0.3f));

    /// <summary>Bounding box of the part's meshes, in the part's own space.</summary>
    public static Aabb Measure(Node3D part)
    {
        Aabb? bounds = null;
        Accumulate(part, Transform3D.Identity, ref bounds);
        return bounds ?? Fallback;
    }

    private static void Accumulate(Node node, Transform3D xform, ref Aabb? bounds)
    {
        if (node is MeshInstance3D { Mesh: not null } meshInstance)
        {
            var local = meshInstance.Mesh.GetAabb();
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = xform * local.GetEndpoint(corner);
                bounds = bounds is { } b ? b.Expand(point) : new Aabb(point, Vector3.Zero);
            }
        }

        foreach (var child in node.GetChildren())
        {
            Accumulate(child, child is Node3D c3 ? xform * c3.Transform : xform, ref bounds);
        }
    }

    /// <summary>
    /// Distance along the ray at which it enters the part's box, or null if it
    /// misses. The ray is taken into the part's local space, so rotated parts
    /// are tested against their own box rather than a world-aligned envelope.
    /// </summary>
    public static float? RayDistance(Node3D part, Vector3 origin, Vector3 direction)
    {
        var toLocal = part.GlobalTransform.AffineInverse();
        Vector3 localOrigin = toLocal * origin;
        Vector3 localDir = toLocal.Basis * direction;

        var box = Measure(part);
        Vector3 lo = box.Position;
        Vector3 hi = box.End;

        float near = float.NegativeInfinity;
        float far = float.PositiveInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            float d = localDir[axis];
            float o = localOrigin[axis];

            if (Mathf.Abs(d) < 1e-6f)
            {
                // Parallel to this slab: a miss unless the ray starts inside it.
                if (o < lo[axis] || o > hi[axis]) return null;
                continue;
            }

            float t1 = (lo[axis] - o) / d;
            float t2 = (hi[axis] - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);

            near = Mathf.Max(near, t1);
            far = Mathf.Min(far, t2);
            if (near > far) return null;
        }

        if (far < 0) return null;          // box is behind the camera
        return near >= 0 ? near : 0.0f;    // 0 when the ray starts inside
    }
}
