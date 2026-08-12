using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Utility to generate detailed industrial 3D visual meshes for factory components.
/// </summary>
public static class IndustrialMeshBuilder
{
    // Painted machine steel. The environment is 90% sky ambient, so anything
    // near mirror-metallic reflects the sky and renders as a white slab —
    // which is what these parts used to do at 0.85/0.35.
    private static readonly StandardMaterial3D DarkMetalMat = new()
    {
        AlbedoColor = new Color(0.18f, 0.20f, 0.22f),
        Metallic = 0.45f,
        Roughness = 0.50f,
    };

    // Brushed steel, not chrome: at 0.90/0.25 the sun blew the belt rails and
    // sensor posts out to pure white bars that read as glowing strip lights.
    private static readonly StandardMaterial3D SteelMat = new()
    {
        AlbedoColor = new Color(0.62f, 0.64f, 0.68f),
        Metallic = 0.35f,
        Roughness = 0.50f,
    };

    private static readonly StandardMaterial3D IndustrialYellowMat = new()
    {
        AlbedoColor = new Color(0.95f, 0.75f, 0.10f),
        Roughness = 0.40f,
    };

    private static readonly StandardMaterial3D OrangePusherMat = new()
    {
        AlbedoColor = new Color(0.95f, 0.45f, 0.10f),
        Roughness = 0.50f,
    };

    public static StandardMaterial3D CreateBeltMaterial(Vector3 size)
    {
        var rubber = new Color(0.12f, 0.12f, 0.14f);
        var tread = new Color(0.22f, 0.22f, 0.25f);

        // Assign the point arrays rather than calling AddPoint: a fresh Gradient
        // already carries default black->white points, and AddPoint keeps them.
        // That is what turned this "subtle tread line" into broad white ramps
        // sweeping down the belt.
        var gradient = new Gradient
        {
            Offsets = new[] { 0.0f, 0.44f, 0.46f, 0.54f, 0.56f, 1.0f },
            Colors = new[] { rubber, rubber, tread, tread, rubber, rubber },
        };

        var tex = new GradientTexture2D
        {
            Gradient = gradient,
            Width = 64,
            Height = 64,
        };

        return new StandardMaterial3D
        {
            AlbedoTexture = tex,
            Uv1Scale = new Vector3(size.X * 4.0f, 1.0f, 1.0f),
            // Matte rubber: without damping the specular the sun draws a broad
            // sheen down the lane and the belt reads as polished steel.
            Roughness = 0.95f,
            MetallicSpecular = 0.15f,
        };
    }

    public static Node3D BuildDetailedConveyor(Vector3 size, out StandardMaterial3D beltMat)
    {
        var container = new Node3D { Name = "ConveyorVisual" };
        beltMat = CreateBeltMaterial(size);

        // 1. Belt surface (center)
        var beltMesh = new MeshInstance3D
        {
            Name = "BeltSurfaceMesh",
            Mesh = new BoxMesh { Size = new Vector3(size.X, 0.04f, size.Z - 0.08f) },
            MaterialOverride = beltMat,
            Position = new Vector3(0, size.Y / 2 - 0.02f, 0),
        };
        container.AddChild(beltMesh);

        // 2. Steel side rails (front and back guard rails)
        float railThickness = 0.04f;
        float railZ = (size.Z - railThickness) / 2;

        var railFront = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, size.Y, railThickness) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, 0, railZ),
        };
        container.AddChild(railFront);

        var railBack = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, size.Y, railThickness) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, 0, -railZ),
        };
        container.AddChild(railBack);

        // 3. Structural support legs
        float legX1 = -size.X * 0.35f;
        float legX2 = size.X * 0.35f;
        float legH = 0.5f;

        var leg1 = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, legH, size.Z) },
            MaterialOverride = DarkMetalMat,
            Position = new Vector3(legX1, -legH / 2, 0),
        };
        container.AddChild(leg1);

        var leg2 = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, legH, size.Z) },
            MaterialOverride = DarkMetalMat,
            Position = new Vector3(legX2, -legH / 2, 0),
        };
        container.AddChild(leg2);

        return container;
    }

    /// <param name="postDrop">How far below the node the post continues, so the
    /// sensor stands on the floor instead of hovering beside the belt.</param>
    public static Node3D BuildDetailedSensor(float range, float mountHeight = 0.25f,
                                             float postDrop = 0.0f)
    {
        var container = new Node3D { Name = "SensorVisual" };

        // 1. Vertical mount post, from the floor (if asked) up to mountHeight
        float postHeight = mountHeight + postDrop;
        var post = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.03f, postHeight, 0.03f) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, mountHeight - postHeight / 2.0f, 0),
        };
        container.AddChild(post);

        if (postDrop > 0.0f)
        {
            container.AddChild(new MeshInstance3D
            {
                Name = "BaseFoot",
                Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.02f, 0.12f) },
                MaterialOverride = DarkMetalMat,
                Position = new Vector3(0, -postDrop, 0),
            });
        }

        // 2. Sensor body head mounted at top of post
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.08f, 0.10f) },
            MaterialOverride = IndustrialYellowMat,
            Position = new Vector3(0, mountHeight, 0.04f),
        };
        container.AddChild(body);

        // 3. Optical Lens
        var lens = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.04f, 0.02f) },
            MaterialOverride = DarkMetalMat,
            Position = new Vector3(0, mountHeight, -0.01f),
        };
        container.AddChild(lens);

        return container;
    }

    /// <summary>
    /// Cylinder barrel and its floor pedestal. The pusher's local origin is the
    /// mounting point beside the belt: the barrel sits entirely behind it, in
    /// -Z, so the rod emerges from the front face at z = 0 and the whole unit
    /// stays clear of the belt lane.
    /// </summary>
    public static Node3D BuildPusherHousing(float barrelDepth, float barrelHeight,
                                            float pedestalHeight)
    {
        var container = new Node3D { Name = "PusherHousingVisual" };

        var barrel = new MeshInstance3D
        {
            Name = "Barrel",
            Mesh = new BoxMesh { Size = new Vector3(0.20f, barrelHeight, barrelDepth) },
            MaterialOverride = DarkMetalMat,
            Position = new Vector3(0, 0, -barrelDepth / 2.0f),
        };
        container.AddChild(barrel);

        // End cap and port block, so the barrel reads as a pneumatic cylinder
        // rather than a plain box.
        container.AddChild(new MeshInstance3D
        {
            Name = "FrontCap",
            Mesh = new BoxMesh { Size = new Vector3(0.24f, barrelHeight + 0.03f, 0.035f) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, 0, -0.02f),
        });
        container.AddChild(new MeshInstance3D
        {
            Name = "RearCap",
            Mesh = new BoxMesh { Size = new Vector3(0.24f, barrelHeight + 0.03f, 0.035f) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, 0, -barrelDepth + 0.02f),
        });

        var port = new MeshInstance3D
        {
            Name = "AirPort",
            Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 0.07f },
            MaterialOverride = IndustrialYellowMat,
            Position = new Vector3(0, barrelHeight / 2 + 0.03f, -barrelDepth + 0.09f),
        };
        container.AddChild(port);

        if (pedestalHeight > 0.0f)
        {
            container.AddChild(new MeshInstance3D
            {
                Name = "Pedestal",
                Mesh = new BoxMesh { Size = new Vector3(0.10f, pedestalHeight, 0.10f) },
                MaterialOverride = DarkMetalMat,
                Position = new Vector3(0, -barrelHeight / 2 - pedestalHeight / 2, -barrelDepth / 2),
            });
            container.AddChild(new MeshInstance3D
            {
                Name = "BaseFoot",
                Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.03f, 0.24f) },
                MaterialOverride = SteelMat,
                Position = new Vector3(0, -barrelHeight / 2 - pedestalHeight, -barrelDepth / 2),
            });
        }

        return container;
    }

    /// <summary>
    /// Chrome piston rod. It is resized rather than translated as the cylinder
    /// strokes, so the rod grows out of the barrel instead of sliding through
    /// it like a fixed stick. Spans z ∈ [0, Height] once positioned.
    /// </summary>
    public static MeshInstance3D BuildPusherRod(out CylinderMesh rodMesh)
    {
        rodMesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.06f };
        var rod = new MeshInstance3D
        {
            Name = "PistonRod",
            Mesh = rodMesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.90f, 0.92f, 0.95f),
                Metallic = 0.98f,
                Roughness = 0.10f,
            },
        };
        rod.RotateX(Mathf.Pi / 2);   // cylinder axis Y -> +Z
        return rod;
    }

    public static Node3D BuildPusherFacePlate(Vector3 size)
    {
        var container = new Node3D { Name = "PusherFacePlate" };

        container.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = OrangePusherMat,
        });

        // Steel backing rib where the rod meets the plate.
        container.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, size.Y * 0.6f, 0.03f) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, 0, -size.Z / 2 - 0.015f),
        });

        return container;
    }
}
