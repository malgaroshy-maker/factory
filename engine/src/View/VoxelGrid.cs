using System;
using Godot;

namespace FactoryForge.View;

/// <summary>
/// Discrete 3D voxel coordinate representing grid positions in the factory space.
/// </summary>
public readonly struct VoxelCoord : IEquatable<VoxelCoord>
{
    public int X { get; }
    public int Y { get; }
    public int Z { get; }

    public VoxelCoord(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public bool Equals(VoxelCoord other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is VoxelCoord other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"VoxelCoord({X}, {Y}, {Z})";

    public static bool operator ==(VoxelCoord left, VoxelCoord right) => left.Equals(right);
    public static bool operator !=(VoxelCoord left, VoxelCoord right) => !left.Equals(right);
}

/// <summary>
/// Manages factory floor voxel grid alignment, snapping, and grid rendering.
/// </summary>
public partial class VoxelGrid : Node3D
{
    [Export] public float CellSize { get; set; } = 0.5f;
    [Export] public int GridExtentX { get; set; } = 10;
    [Export] public int GridExtentZ { get; set; } = 10;
    [Export] public bool ShowGridLines { get; set; } = true;

    private MeshInstance3D? _gridMeshInstance;

    public override void _Ready()
    {
        if (ShowGridLines)
        {
            BuildGridMesh();
        }
    }

    public VoxelCoord WorldToVoxel(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt(worldPos.X / CellSize);
        int y = Mathf.RoundToInt(worldPos.Y / CellSize);
        int z = Mathf.RoundToInt(worldPos.Z / CellSize);
        return new VoxelCoord(x, y, z);
    }

    public Vector3 VoxelToWorld(VoxelCoord coord)
    {
        return new Vector3(
            coord.X * CellSize,
            coord.Y * CellSize,
            coord.Z * CellSize
        );
    }

    public Vector3 SnapToGrid(Vector3 worldPos)
    {
        return VoxelToWorld(WorldToVoxel(worldPos));
    }

    private void BuildGridMesh()
    {
        var immediate = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.4f, 0.45f, 0.5f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        immediate.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        float minX = -GridExtentX * CellSize;
        float maxX = GridExtentX * CellSize;
        float minZ = -GridExtentZ * CellSize;
        float maxZ = GridExtentZ * CellSize;
        float y = 0.005f; // slightly above floor to prevent z-fighting

        for (int x = -GridExtentX; x <= GridExtentX; x++)
        {
            float posX = x * CellSize;
            immediate.SurfaceAddVertex(new Vector3(posX, y, minZ));
            immediate.SurfaceAddVertex(new Vector3(posX, y, maxZ));
        }

        for (int z = -GridExtentZ; z <= GridExtentZ; z++)
        {
            float posZ = z * CellSize;
            immediate.SurfaceAddVertex(new Vector3(minX, y, posZ));
            immediate.SurfaceAddVertex(new Vector3(maxX, y, posZ));
        }

        immediate.SurfaceEnd();

        _gridMeshInstance = new MeshInstance3D
        {
            Name = "GridLines",
            Mesh = immediate,
        };
        AddChild(_gridMeshInstance);
    }
}
