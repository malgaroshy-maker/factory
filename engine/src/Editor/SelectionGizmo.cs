using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Renders a 3D visual selection bounding box outline around the currently selected part.
/// </summary>
public partial class SelectionGizmo : Node3D
{
    private MeshInstance3D _outlineMesh = null!;
    private Node3D? _targetNode;

    public override void _Ready()
    {
        var immediateMesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.2f, 0.8f, 1.0f, 0.9f),
            NoDepthTest = true,
        };

        _outlineMesh = new MeshInstance3D
        {
            Mesh = immediateMesh,
            MaterialOverride = material,
            Visible = false,
        };
        AddChild(_outlineMesh);
    }

    public void AttachToNode(Node3D? targetNode)
    {
        _targetNode = targetNode;
        if (targetNode is null)
        {
            _outlineMesh.Visible = false;
            return;
        }

        GlobalPosition = targetNode.GlobalPosition;
        GlobalRotation = targetNode.GlobalRotation;
        RebuildOutline(PartBounds.Measure(targetNode));
        _outlineMesh.Visible = true;
    }

    private void RebuildOutline(Aabb bounds)
    {
        if (_outlineMesh.Mesh is not ImmediateMesh mesh) return;

        // A little breathing room, so the outline reads as a selection rather
        // than as z-fighting against the part's own faces.
        bounds = bounds.Grow(0.02f);

        Vector3 lo = bounds.Position;
        Vector3 hi = bounds.End;

        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        // Four verticals plus the top and bottom rings.
        var corners = new[]
        {
            new Vector2(lo.X, lo.Z), new Vector2(hi.X, lo.Z),
            new Vector2(hi.X, hi.Z), new Vector2(lo.X, hi.Z),
        };

        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];

            mesh.SurfaceAddVertex(new Vector3(a.X, hi.Y, a.Y));
            mesh.SurfaceAddVertex(new Vector3(b.X, hi.Y, b.Y));

            mesh.SurfaceAddVertex(new Vector3(a.X, lo.Y, a.Y));
            mesh.SurfaceAddVertex(new Vector3(b.X, lo.Y, b.Y));

            mesh.SurfaceAddVertex(new Vector3(a.X, lo.Y, a.Y));
            mesh.SurfaceAddVertex(new Vector3(a.X, hi.Y, a.Y));
        }

        mesh.SurfaceEnd();
    }

    public override void _Process(double delta)
    {
        if (_targetNode is not null && IsInstanceValid(_targetNode))
        {
            GlobalPosition = _targetNode.GlobalPosition;
            GlobalRotation = _targetNode.GlobalRotation;
        }
        else
        {
            _outlineMesh.Visible = false;
        }
    }
}
