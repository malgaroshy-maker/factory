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

            // Glow, so the parts that are *meant* to be lights read as lit
            // rather than as brightly painted (CP-15). Every emissive surface
            // in the library was tuned to an energy multiplier above 1 — the
            // stack light's lamps at 3.0, a drive fault beacon at 2.4, a hot
            // plate at 3.2 — and without a bloom threshold above 1 none of that
            // headroom reached the screen: a lit lamp and an unlit one differed
            // only in albedo. The threshold is deliberately at 1.0 so ordinary
            // lit surfaces, which never exceed it, do not smear.
            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowStrength = 0.9f,
            GlowBloom = 0.05f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            // 1.15 rather than 1.0: a threshold right at white lets a merely
            // bright *lit* surface bloom -- a steel carton under a 90%-sky
            // ambient reaches it -- and bloom on something that is not a light
            // reads as a rendering fault. Every emissive part in the library
            // is tuned well above this, so nothing that should glow stops.
            GlowHdrThreshold = 1.15f,
        };
        parent.AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.1f,
            ShadowBlur = 2.0f,
            // Slightly warm, the way a real shop's roof lights are, so the
            // steel reads as machinery under lighting rather than as untextured
            // grey. Small enough not to tint the safety colours.
            LightColor = new Color(1.0f, 0.97f, 0.92f),
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
        // Poured concrete with a control joint around every two-metre bay, a
        // normal map so the sun catches the surface, and worn patches that are
        // smoother than the aggregate between them (EN-01). The previous floor
        // was a dark plane with a faint speckle on it: enough to stop it
        // reading as flat-shaded, not enough to read as anything in
        // particular, and with nothing on it a person could measure a conveyor
        // against.
        parent.AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(GroundExtent, GroundExtent) },
            MaterialOverride = ShopTextures.FloorMaterial(GroundExtent),
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

        AddFloorMarkings(parent);
        AddWalls(parent);

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

    /// <summary>How far the shop walls stand from the centre. Outside the
    /// build volume with room to spare, so nothing a person can place is ever
    /// against a wall, and close enough that they are in shot on a wide
    /// view.</summary>
    public const float WallDistance = 11.0f;

    /// <summary>Eaves height. A real light-industrial unit is five to six
    /// metres to the underside, and the number matters: cladding of a known
    /// height is the second thing in the scene a conveyor can be measured
    /// against, after the two-metre floor bays.</summary>
    public const float WallHeight = 5.0f;

    /// <summary>
    /// The shop the line stands in (EN-02).
    ///
    /// There were no walls at all. A factory simulator opened on a machine in
    /// a grey void, and the specific casualty was *scale*: with nothing around
    /// it, a conveyor could be two metres long or twenty and nothing on screen
    /// said which.
    ///
    /// Three things make this work rather than boxing the user in:
    ///
    /// * The panels are **one-sided and face inward**, so the camera can orbit
    ///   outside the shop and see straight through the wall behind it. A
    ///   double-sided wall would replace the void with a blank grey box, which
    ///   is not an improvement.
    /// * There is **no collision**. These are set dressing. Giving them
    ///   colliders would stop a carton that outran the line — which the README
    ///   advertises as normal, and which the kill plane already handles — and
    ///   would change the behaviour of every scene already authored against a
    ///   floor that went on for forty metres.
    /// * The tops are open to the sky, so the sky light and the fog that
    ///   <see cref="AddEnvironment"/> sets up reach the floor unchanged and a
    ///   screenshot taken before this still compares.
    /// </summary>
    private static void AddWalls(Node parent)
    {
        var shop = new Node3D { Name = "ShopWalls" };
        parent.AddChild(shop);

        float span = WallDistance * 2.0f;

        // Cladding above, a darker painted dado below. Every shop is painted
        // this way for the same reason: the bottom metre takes the knocks.
        const float dadoHeight = 1.2f;
        float cladHeight = WallHeight - dadoHeight;

        var cladMat = ShopTextures.WallMaterial(span, cladHeight, new Color(0.60f, 0.63f, 0.67f));
        var dadoMat = ShopTextures.WallMaterial(span, dadoHeight, new Color(0.30f, 0.34f, 0.38f));

        // (name, position, heading) — heading turns the quad's own +Z normal to
        // face the centre of the shop.
        foreach (var (name, offset, heading) in new (string, Vector3, float)[]
                 {
                     ("North", new Vector3(0, 0, -WallDistance), 0.0f),
                     ("South", new Vector3(0, 0, WallDistance), 180.0f),
                     ("West", new Vector3(-WallDistance, 0, 0), 90.0f),
                     ("East", new Vector3(WallDistance, 0, 0), -90.0f),
                 })
        {
            var wall = new Node3D { Name = $"Wall{name}", Position = offset };
            wall.RotateY(Mathf.DegToRad(heading));
            shop.AddChild(wall);

            wall.AddChild(new MeshInstance3D
            {
                Name = "Cladding",
                Mesh = new QuadMesh { Size = new Vector2(span, cladHeight) },
                MaterialOverride = cladMat,
                Position = new Vector3(0, dadoHeight + cladHeight / 2.0f, 0),
                // Nothing casts a shadow from out here: these are four large
                // surfaces well outside the build volume, and shadowing them
                // buys nothing but shadow-map resolution taken from the
                // machines, which is where it is wanted.
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            wall.AddChild(new MeshInstance3D
            {
                Name = "Dado",
                Mesh = new QuadMesh { Size = new Vector2(span, dadoHeight) },
                MaterialOverride = dadoMat,
                Position = new Vector3(0, dadoHeight / 2.0f, 0.002f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        AddWallStanchions(shop, span);
    }

    /// <summary>Steel columns down each wall. Cheap, and they are what stops
    /// four flat panels reading as a painted backdrop: a column has a near
    /// edge and a far edge, so it moves against the wall as the camera
    /// orbits.</summary>
    private static void AddWallStanchions(Node3D shop, float span)
    {
        var steelMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.34f, 0.37f, 0.41f),
            Metallic = 0.45f,
            Roughness = 0.55f,
        };

        const int perWall = 5;
        for (int i = 0; i < perWall; i++)
        {
            float along = -span / 2.0f + (i + 0.5f) * (span / perWall);

            foreach (var (name, position) in new (string, Vector3)[]
                     {
                         ($"North{i}", new Vector3(along, 0, -WallDistance + 0.12f)),
                         ($"South{i}", new Vector3(along, 0, WallDistance - 0.12f)),
                         ($"West{i}", new Vector3(-WallDistance + 0.12f, 0, along)),
                         ($"East{i}", new Vector3(WallDistance - 0.12f, 0, along)),
                     })
            {
                shop.AddChild(new MeshInstance3D
                {
                    Name = $"Stanchion{name}",
                    Mesh = new BoxMesh { Size = new Vector3(0.22f, WallHeight, 0.16f) },
                    MaterialOverride = steelMat,
                    Position = position + new Vector3(0, WallHeight / 2.0f, 0),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }
    }

    /// <summary>
    /// Safety-yellow border and hatching around the build volume (CP-14).
    ///
    /// Two things were wrong without it. The floor was a plain dark plane, so
    /// the scene read as a product render rather than as a factory — and every
    /// real shop floor is painted, precisely because an unmarked floor tells
    /// nobody where they may stand. And the build volume's edge existed only in
    /// the grid overlay, so turning the grid off left no way to see where parts
    /// stop being placeable.
    ///
    /// Drawn just above the floor plane rather than co-planar with it: two
    /// surfaces at the same height z-fight, and a flickering safety line is
    /// worse than none.
    /// </summary>
    private static void AddFloorMarkings(Node parent)
    {
        const float markingY = 0.004f;
        const float lineWidth = 0.08f;
        float edge = BuildVolumeExtent;

        // Muted safety yellow, not a highlighter. At 0.85/0.68 the border ran
        // as a saturated bright line across the top of every wide shot and
        // pulled the eye off the machine, which is the opposite of what floor
        // paint is for. Worn paint on a shop floor is closer to this.
        var yellowMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.68f, 0.55f, 0.12f),
            Roughness = 0.9f,
        };

        var markings = new Node3D { Name = "FloorMarkings" };
        parent.AddChild(markings);

        // The border of the build volume, one bar per side.
        foreach (var (name, size, position) in new (string, Vector3, Vector3)[]
                 {
                     ("BorderNorth", new Vector3(edge * 2, 0.002f, lineWidth), new Vector3(0, markingY, -edge)),
                     ("BorderSouth", new Vector3(edge * 2, 0.002f, lineWidth), new Vector3(0, markingY, edge)),
                     ("BorderWest", new Vector3(lineWidth, 0.002f, edge * 2), new Vector3(-edge, markingY, 0)),
                     ("BorderEast", new Vector3(lineWidth, 0.002f, edge * 2), new Vector3(edge, markingY, 0)),
                 })
        {
            markings.AddChild(new MeshInstance3D
            {
                Name = name,
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = yellowMat,
                Position = position,
            });
        }

        // Hazard hatching outside the border, on the two long sides. Angled
        // bars, the way a real keep-clear zone is painted.
        var hatchMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.60f, 0.49f, 0.12f),
            Roughness = 0.92f,
        };
        const int hatchCount = 26;
        for (int i = 0; i < hatchCount; i++)
        {
            float x = -edge + (i + 0.5f) * (edge * 2 / hatchCount);
            foreach (int side in new[] { -1, 1 })
            {
                var bar = new MeshInstance3D
                {
                    Name = $"Hatch{side}_{i}",
                    Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.002f, 0.42f) },
                    MaterialOverride = hatchMat,
                    Position = new Vector3(x, markingY, side * (edge + 0.30f)),
                };
                bar.RotateY(Mathf.DegToRad(35));
                markings.AddChild(bar);
            }
        }
    }
}
