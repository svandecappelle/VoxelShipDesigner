namespace ShipDesign.Core.Procedural;

/// <summary>A sparse set of filled integer voxel coordinates, each tagged with a material role.</summary>
public sealed class VoxelGrid
{
    private readonly Dictionary<(int X, int Y, int Z), VoxelMaterial> _voxels = new();

    public IReadOnlyDictionary<(int X, int Y, int Z), VoxelMaterial> Voxels => _voxels;

    public void Set(int x, int y, int z, VoxelMaterial material) => _voxels[(x, y, z)] = material;

    public bool IsFilled(int x, int y, int z) => _voxels.ContainsKey((x, y, z));

    public VoxelMaterial? Get(int x, int y, int z) => _voxels.TryGetValue((x, y, z), out var m) ? m : null;

    public void Remove(int x, int y, int z) => _voxels.Remove((x, y, z));

    /// <summary>Fills (x,y,z) and its mirror (-x,y,z) with the same material -- the standard way
    /// every grower call adds voxels, so the ship comes out left-right symmetric by construction.</summary>
    public void SetMirrored(int x, int y, int z, VoxelMaterial material)
    {
        Set(x, y, z, material);
        Set(-x, y, z, material);
    }
}
