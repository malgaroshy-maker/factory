using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Shared ray/sphere intersection test for a part's own sub-regions -- a
/// panel's caps, a stack light's lamps, a tank's valves -- tested as spheres
/// in the part's local space rather than against its full mesh. See
/// <see cref="ButtonPanel.HitTest"/> for why: a bounding box covers the whole
/// part, not just the one control a click landed on.
/// </summary>
public static class RayHit
{
    public static float? Sphere(Vector3 origin, Vector3 direction, Vector3 centre, float radius)
    {
        Vector3 toCentre = origin - centre;
        float b = toCentre.Dot(direction);
        float c = toCentre.LengthSquared() - radius * radius;
        float disc = b * b - c;
        if (disc < 0.0f) return null;

        float t = -b - Mathf.Sqrt(disc);
        if (t < 0.0f) t = -b + Mathf.Sqrt(disc);
        return t < 0.0f ? null : t;
    }
}
