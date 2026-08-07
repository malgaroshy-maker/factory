using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Utility to generate detailed industrial 3D visual meshes for factory components.
/// </summary>
public static class IndustrialMeshBuilder
{
    private static StandardMaterial3D DarkMetalMat => new()
    {
        AlbedoColor = new Color(0.18f, 0.20f, 0.22f),
        Metallic = 0.85f,
        Roughness = 0.35f,
    };

    private static StandardMaterial3D SteelMat => new()
    {
        AlbedoColor = new Color(0.70f, 0.72f, 0.75f),
        Metallic = 0.90f,
        Roughness = 0.25f,
    };

    private static StandardMaterial3D IndustrialYellowMat => new()
    {
        AlbedoColor = new Color(0.95f, 0.75f, 0.10f),
        Roughness = 0.40f,
    };

    private static StandardMaterial3D OrangePusherMat => new()
    {
        AlbedoColor = new Color(0.95f, 0.45f, 0.10f),
        Roughness = 0.50f,
    };

    public static StandardMaterial3D CreateBeltMaterial(Vector3 size)
    {
        var gradient = new Gradient();
        gradient.AddPoint(0.0f, new Color(0.12f, 0.12f, 0.14f));
        gradient.AddPoint(0.47f, new Color(0.12f, 0.12f, 0.14f));
        gradient.AddPoint(0.50f, new Color(0.22f, 0.22f, 0.25f)); // Subtle dark grey tread line
        gradient.AddPoint(0.53f, new Color(0.12f, 0.12f, 0.14f));
        gradient.AddPoint(1.0f, new Color(0.12f, 0.12f, 0.14f));

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
            Roughness = 0.85f,
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

    public static Node3D BuildDetailedSensor(float range, float mountHeight = 0.25f)
    {
        var container = new Node3D { Name = "SensorVisual" };

        // 1. Vertical mount post matching exact mountHeight
        var post = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.03f, mountHeight, 0.03f) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, mountHeight / 2.0f, 0),
        };
        container.AddChild(post);

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

    public static Node3D BuildPusherHousing()
    {
        var container = new Node3D { Name = "PusherHousingVisual" };
        var housingMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.15f, 0.15f, 0.18f),
            Metallic = 0.90f,
            Roughness = 0.30f,
        };

        var housing = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.18f, 0.35f) },
            MaterialOverride = housingMat,
            Position = new Vector3(0, 0, -0.175f),
        };
        container.AddChild(housing);

        return container;
    }

    public static Node3D BuildPusherPistonHead(float strokeLength)
    {
        var container = new Node3D { Name = "PistonHeadVisual" };

        var shaftMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.90f, 0.92f, 0.95f),
            Metallic = 0.98f,
            Roughness = 0.10f,
        };

        var shaft = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = strokeLength },
            MaterialOverride = shaftMat,
            Position = new Vector3(0, 0, -strokeLength / 2.0f),
        };
        shaft.RotateX(Mathf.Pi / 2);
        container.AddChild(shaft);

        var facePlate = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.16f, 0.04f) },
            MaterialOverride = OrangePusherMat,
            Position = new Vector3(0, 0, 0),
        };
        container.AddChild(facePlate);

        return container;
    }
}
