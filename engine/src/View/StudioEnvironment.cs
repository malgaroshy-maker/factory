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
    /// <summary>The area the grid displays and the editor clamps placement
    /// to — what "the factory" actually is. Left at its original size so
    /// existing saved scenes and templates, authored against this bound,
    /// keep every part exactly where it was.</summary>
    public const float BuildVolumeExtent = 7.0f;

    /// <summary>The visual and physical floor extends well past the build
    /// volume, so its edge is never in shot and a carton that outruns the
    /// build area (which the README advertises as normal) still has somewhere
    /// to land before the kill plane below catches it, instead of vanishing
    /// through empty space the instant it crosses x=3.5. See FF-24.</summary>
    public const float GroundExtent = 40.0f;

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
            // A soft haze so a 40m ground plane recedes into the sky instead
            // of reading as a stark, obviously-bounded slab — the cheap half
            // of FF-24's "fogged infinite shader" alternative, paired with
            // just making the floor big enough that its edge stays offscreen.
            FogEnabled = true,
            FogLightColor = new Color(0.62f, 0.65f, 0.7f),
            FogLightEnergy = 1.0f,
            FogDensity = 0.012f,
            FogSkyAffect = 0.3f,
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
        // Darker and less saturated than before, with a faint procedural
        // speckle so a 40m plane reads as a surface rather than a flat-shaded
        // slab. See FF-26. The speckle is a near-white multiplier on the
        // albedo (not a swing toward black), so it cannot accidentally wash
        // the floor out or blow it toward the "shiny metal slab" failure mode
        // the metal materials elsewhere in this file already had to avoid.
        var floorMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.15f, 0.14f, 0.14f),
            AlbedoTexture = new NoiseTexture2D
            {
                Width = 256,
                Height = 256,
                Seamless = true,
                ColorRamp = new Gradient
                {
                    Offsets = new[] { 0.0f, 1.0f },
                    Colors = new[] { new Color(0.82f, 0.82f, 0.82f), new Color(1.0f, 1.0f, 1.0f) },
                },
                Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.06f },
            },
            Uv1Scale = new Vector3(GroundExtent, GroundExtent, 1.0f),
            Roughness = 0.95f,
        };
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(GroundExtent, GroundExtent) },
            MaterialOverride = floorMat,
        });

        // Floor physics static body, sized to match the visual ground rather
        // than just the build volume — so cartons shoved past the grid still
        // land on something instead of falling into empty space right at its
        // edge, the way the README's "boxes outrun the diverter" implies.
        var floorBody = new StaticBody3D();
        floorBody.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(GroundExtent, 0.1f, GroundExtent) },
            Position = new Vector3(0, -0.05f, 0)
        });
        floorBody.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f, Bounce = 0.0f };
        parent.AddChild(floorBody);

        if (withGrid)
        {
            parent.AddChild(new VoxelGrid
            {
                Name = "VoxelGrid",
                GridExtentX = (int)BuildVolumeExtent,
                GridExtentZ = (int)BuildVolumeExtent,
                CellSize = 0.5f,
            });
        }
    }
}
