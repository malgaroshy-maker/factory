using Godot;

namespace FactoryForge.View;

/// <summary>
/// Lighting, sky, floor and grid — the staging every scene needs and no factory
/// component owns. Shared so the deterministic scene and the physics scene are
/// lit and grounded identically, and a screenshot of one is comparable to the
/// other.
/// </summary>
public static class StudioEnvironment
{
    public const float FloorExtent = 7.0f;

    public static void AddEnvironment(Node parent)
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.9f,
            AmbientLightEnergy = 1.3f,
            SsaoEnabled = true,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        parent.AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.1f,
            ShadowBlur = 2.0f,
        };
        sun.RotateX(Mathf.DegToRad(-55));
        sun.RotateY(Mathf.DegToRad(-40));
        parent.AddChild(sun);

        var fill = new DirectionalLight3D { ShadowEnabled = false, LightEnergy = 0.35f };
        fill.RotateX(Mathf.DegToRad(-20));
        fill.RotateY(Mathf.DegToRad(140));
        parent.AddChild(fill);
    }

    public static void AddFloor(Node parent, bool withGrid = true)
    {
        var floorMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.24f, 0.22f, 0.21f),
            Roughness = 0.95f,
        };
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(FloorExtent, FloorExtent) },
            MaterialOverride = floorMat,
        });

        // Floor physics static body so boxes that fall or slide land on the ground
        var floorBody = new StaticBody3D();
        floorBody.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(FloorExtent, 0.1f, FloorExtent) },
            Position = new Vector3(0, -0.05f, 0)
        });
        floorBody.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f, Bounce = 0.0f };
        parent.AddChild(floorBody);

        if (withGrid)
        {
            parent.AddChild(new VoxelGrid
            {
                Name = "VoxelGrid",
                GridExtentX = (int)FloorExtent,
                GridExtentZ = (int)FloorExtent,
                CellSize = 0.5f,
            });
        }
    }
}
