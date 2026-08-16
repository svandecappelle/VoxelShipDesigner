namespace ShipDesign.Core.Procedural;

/// <summary>A sparse set of filled integer voxel coordinates, each tagged with a material role.</summary>
public sealed class VoxelGrid
{
    private readonly Dictionary<(int X, int Y, int Z), VoxelMaterial> _voxels = new();

    public IReadOnlyDictionary<(int X, int Y, int Z), VoxelMaterial> Voxels => _voxels;

    /// <summary>Bounds of the filled voxels, tracked as they are set. The surface-detail passes
    /// scan columns looking for the topmost/outermost filled voxel; without a real bound they
    /// would have to guess a generous search range, which gets expensive as resolution rises.</summary>
    public int MinX { get; private set; } = int.MaxValue;
    public int MaxX { get; private set; } = int.MinValue;
    public int MinY { get; private set; } = int.MaxValue;
    public int MaxY { get; private set; } = int.MinValue;
    public int MinZ { get; private set; } = int.MaxValue;
    public int MaxZ { get; private set; } = int.MinValue;

    public bool IsEmpty => _voxels.Count == 0;

    public void Set(int x, int y, int z, VoxelMaterial material)
    {
        _voxels[(x, y, z)] = material;

        if (x < MinX) MinX = x;
        if (x > MaxX) MaxX = x;
        if (y < MinY) MinY = y;
        if (y > MaxY) MaxY = y;
        if (z < MinZ) MinZ = z;
        if (z > MaxZ) MaxZ = z;
    }

    public bool IsFilled(int x, int y, int z) => _voxels.ContainsKey((x, y, z));

    public VoxelMaterial? Get(int x, int y, int z) => _voxels.TryGetValue((x, y, z), out var m) ? m : null;

    /// <summary>Clears a voxel. Bounds are deliberately not shrunk: they stay a conservative
    /// outer box, which is all the surface scans need, and recomputing them on every carve would
    /// cost more than the slightly wider scan does.</summary>
    public void Remove(int x, int y, int z) => _voxels.Remove((x, y, z));

    /// <summary>Fills (x,y,z) and its mirror (-x,y,z) with the same material -- the standard way
    /// every grower call adds voxels, so the ship comes out left-right symmetric by construction.</summary>
    public void SetMirrored(int x, int y, int z, VoxelMaterial material)
    {
        Set(x, y, z, material);
        Set(-x, y, z, material);
    }
}
