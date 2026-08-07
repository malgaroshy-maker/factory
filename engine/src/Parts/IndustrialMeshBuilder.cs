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

    private static StandardMaterial3D RubberMat => new()
    {
        AlbedoColor = new Color(0.12f, 0.12f, 0.14f),
        Roughness = 0.85f,
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

    public static Node3D BuildDetailedConveyor(Vector3 size)
    {
        var container = new Node3D { Name = "ConveyorVisual" };

        // 1. Belt surface (center)
        var beltMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, 0.04f, size.Z - 0.08f) },
            MaterialOverride = RubberMat,
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

    public static Node3D BuildDetailedSensor(float range)
    {
        var container = new Node3D { Name = "SensorVisual" };

        // 1. Vertical mount post
        var post = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.5f, 0.04f) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, 0.25f, 0),
        };
        container.AddChild(post);

        // 2. Sensor body head
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.08f, 0.10f) },
            MaterialOverride = IndustrialYellowMat,
            Position = new Vector3(0, 0.50f, -0.04f),
        };
        container.AddChild(body);

        // 3. Lens
        var lensMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.1f, 0.6f, 1.0f),
            Metallic = 0.9f,
            Roughness = 0.1f,
        };
        var lens = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.04f, 0.02f) },
            MaterialOverride = lensMat,
            Position = new Vector3(0, 0.50f, -0.10f),
        };
        container.AddChild(lens);

        return container;
    }

    public static Node3D BuildPusherHousing()
    {
        var container = new Node3D { Name = "PusherHousingVisual" };
        var cylinder = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.18f, 0.32f) },
            MaterialOverride = DarkMetalMat,
            Position = new Vector3(0, 0, -0.30f),
        };
        container.AddChild(cylinder);
        return container;
    }

    public static Node3D BuildPusherPistonHead(float strokeLength)
    {
        var container = new Node3D { Name = "PusherPistonVisual" };

        // 1. Chrome piston shaft rod
        var shaft = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, strokeLength + 0.15f) },
            MaterialOverride = SteelMat,
            Position = new Vector3(0, 0, -(strokeLength + 0.15f) / 2.0f),
        };
        container.AddChild(shaft);

        // 2. High-visibility orange pusher face plate
        var head = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.16f, 0.04f) },
            MaterialOverride = OrangePusherMat,
            Position = Vector3.Zero,
        };
        container.AddChild(head);

        return container;
    }
}
