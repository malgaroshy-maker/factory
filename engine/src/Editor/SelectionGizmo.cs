using System.Collections.Generic;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// The wireframe box around everything selected.
///
/// It draws in **world space** rather than parenting itself to one part, which
/// is what lets it outline a whole selection (ES-01). The previous version sat
/// on top of its single target and copied that target's transform every frame;
/// with five parts selected there is no single transform to copy, so the boxes
/// are built from each part's own transform instead and the gizmo node itself
/// stays at the origin.
///
/// The primary — the last part added to the selection, and the one the property
/// panel is describing — is drawn brighter than the rest, because a group
/// operation and a single-part operation look identical until you can see which
/// one the panel belongs to.
/// </summary>
public partial class SelectionGizmo : Node3D
{
    private MeshInstance3D _outlineMesh = null!;
    private readonly List<Node3D> _targets = new();
    private readonly List<Transform3D> _drawn = new();

    public override void _Ready()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1, 1, 1, 1),
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
        };

        _outlineMesh = new MeshInstance3D
        {
            Mesh = new ImmediateMesh(),
            MaterialOverride = material,
            Visible = false,
        };
        AddChild(_outlineMesh);
    }

    /// <summary>Outline these parts, primary last.</summary>
    public void AttachToNodes(IReadOnlyList<Node3D> targets)
    {
        _targets.Clear();
        foreach (var target in targets)
        {
            if (target is not null && IsInstanceValid(target)) _targets.Add(target);
        }

        if (_targets.Count == 0)
        {
            _outlineMesh.Visible = false;
            _drawn.Clear();
            return;
        }

        Rebuild();
        _outlineMesh.Visible = true;
    }

    /// <summary>Kept so existing callers of the single-target form still
    /// work.</summary>
    public void AttachToNode(Node3D? targetNode) =>
        AttachToNodes(targetNode is null
            ? System.Array.Empty<Node3D>()
            : new[] { targetNode });

    public override void _Process(double delta)
    {
        if (_targets.Count == 0) return;

        // Rebuild only when something actually moved. A drag moves the whole
        // selection every frame and has to be followed; a scene sitting still
        // must not rebuild an ImmediateMesh sixty times a second for nothing.
        bool stale = _drawn.Count != _targets.Count;
        for (int i = 0; !stale && i < _targets.Count; i++)
        {
            if (!IsInstanceValid(_targets[i])) { stale = true; break; }
            if (_targets[i].GlobalTransform != _drawn[i]) stale = true;
        }

        if (stale) Rebuild();
    }

    private static readonly Color PrimaryColour = new(0.25f, 0.85f, 1.0f, 0.95f);
    private static readonly Color SecondaryColour = new(0.20f, 0.60f, 0.78f, 0.75f);

    private void Rebuild()
    {
        if (_outlineMesh.Mesh is not ImmediateMesh mesh) return;

        // Vertices are world coordinates, so the node itself must not add a
        // transform of its own.
        GlobalTransform = Transform3D.Identity;

        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            if (!IsInstanceValid(_targets[i])) _targets.RemoveAt(i);
        }

        _drawn.Clear();
        mesh.ClearSurfaces();

        if (_targets.Count == 0)
        {
            _outlineMesh.Visible = false;
            return;
        }

        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            _drawn.Add(target.GlobalTransform);
            AddBox(mesh, target, i == _targets.Count - 1 ? PrimaryColour : SecondaryColour);
        }
        mesh.SurfaceEnd();

        _outlineMesh.Visible = true;
    }

    private static void AddBox(ImmediateMesh mesh, Node3D target, Color colour)
    {
        // A little breathing room, so the outline reads as a selection rather
        // than as z-fighting against the part's own faces.
        Aabb bounds = PartBounds.Measure(target).Grow(0.02f);
        Transform3D toWorld = target.GlobalTransform;

        Vector3 lo = bounds.Position;
        Vector3 hi = bounds.End;

        var corners = new[]
        {
            new Vector2(lo.X, lo.Z), new Vector2(hi.X, lo.Z),
            new Vector2(hi.X, hi.Z), new Vector2(lo.X, hi.Z),
        };

        void Line(Vector3 a, Vector3 b)
        {
            mesh.SurfaceSetColor(colour);
            mesh.SurfaceAddVertex(toWorld * a);
            mesh.SurfaceSetColor(colour);
            mesh.SurfaceAddVertex(toWorld * b);
        }

        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];

            Line(new Vector3(a.X, hi.Y, a.Y), new Vector3(b.X, hi.Y, b.Y));
            Line(new Vector3(a.X, lo.Y, a.Y), new Vector3(b.X, lo.Y, b.Y));
            Line(new Vector3(a.X, lo.Y, a.Y), new Vector3(a.X, hi.Y, a.Y));
        }
    }
}
