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
        RebuildOutline();
        _outlineMesh.Visible = true;
    }

    private void RebuildOutline()
    {
        if (_outlineMesh.Mesh is not ImmediateMesh mesh) return;

        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        Vector3 ext = new Vector3(0.8f, 0.4f, 0.4f);

        // Top rectangle
        mesh.SurfaceAddVertex(new Vector3(-ext.X, ext.Y, -ext.Z));
        mesh.SurfaceAddVertex(new Vector3(ext.X, ext.Y, -ext.Z));

        mesh.SurfaceAddVertex(new Vector3(ext.X, ext.Y, -ext.Z));
        mesh.SurfaceAddVertex(new Vector3(ext.X, ext.Y, ext.Z));

        mesh.SurfaceAddVertex(new Vector3(ext.X, ext.Y, ext.Z));
        mesh.SurfaceAddVertex(new Vector3(-ext.X, ext.Y, ext.Z));

        mesh.SurfaceAddVertex(new Vector3(-ext.X, ext.Y, ext.Z));
        mesh.SurfaceAddVertex(new Vector3(-ext.X, ext.Y, -ext.Z));

        // Bottom rectangle
        mesh.SurfaceAddVertex(new Vector3(-ext.X, -ext.Y, -ext.Z));
        mesh.SurfaceAddVertex(new Vector3(ext.X, -ext.Y, -ext.Z));

        mesh.SurfaceAddVertex(new Vector3(ext.X, -ext.Y, -ext.Z));
        mesh.SurfaceAddVertex(new Vector3(ext.X, -ext.Y, ext.Z));

        mesh.SurfaceAddVertex(new Vector3(ext.X, -ext.Y, ext.Z));
        mesh.SurfaceAddVertex(new Vector3(-ext.X, -ext.Y, ext.Z));

        mesh.SurfaceAddVertex(new Vector3(-ext.X, -ext.Y, ext.Z));
        mesh.SurfaceAddVertex(new Vector3(-ext.X, -ext.Y, -ext.Z));

        // Pillars
        mesh.SurfaceAddVertex(new Vector3(-ext.X, -ext.Y, -ext.Z));
        mesh.SurfaceAddVertex(new Vector3(-ext.X, ext.Y, -ext.Z));

        mesh.SurfaceAddVertex(new Vector3(ext.X, -ext.Y, -ext.Z));
        mesh.SurfaceAddVertex(new Vector3(ext.X, ext.Y, -ext.Z));

        mesh.SurfaceAddVertex(new Vector3(ext.X, -ext.Y, ext.Z));
        mesh.SurfaceAddVertex(new Vector3(ext.X, ext.Y, ext.Z));

        mesh.SurfaceAddVertex(new Vector3(-ext.X, -ext.Y, ext.Z));
        mesh.SurfaceAddVertex(new Vector3(-ext.X, ext.Y, ext.Z));

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
