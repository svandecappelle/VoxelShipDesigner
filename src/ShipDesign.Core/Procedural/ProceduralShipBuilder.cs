using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace ShipDesign.Core.Procedural;

/// <summary>Grows a voxel ship and converts it to a single glTF model -- the one entry point
/// the UI layer needs for the procedural generator.</summary>
public static class ProceduralShipBuilder
{
    public static ModelRoot Build(ShipParameters p)
    {
        var scene = new SceneBuilder();
        VoxelMesher.AddToScene(scene, BuildVoxels(p), VoxelShipGrower.VoxelSize, p);
        return scene.ToGltf2();
    }

    /// <summary>
    /// The raw voxel grid behind a ship, before any meshing. Exposed so a renderer can build its
    /// own geometry from it -- the studio view needs per-face occlusion, which is a property of
    /// the grid's neighbourhood and is gone by the time the model is a triangle soup.
    /// </summary>
    public static VoxelGrid BuildVoxels(ShipParameters p) =>
        VoxelShipGrower.Grow(p, HullClassPreset.All[p.HullClass], out _);

    /// <summary>A call-sign-style code from the hull class prefix and the seed, e.g. "FTR-2291".</summary>
    public static string Designation(ShipParameters p) =>
        $"{HullClassPreset.All[p.HullClass].Prefix}-{1000 + (uint)p.Seed % 9000}";

    public static string MassClass(ShipParameters p)
    {
        var volume = p.Length * p.Beam * p.Beam;
        if (volume > 4000) return "CAPITAL";
        if (volume > 800) return "HEAVY";
        if (volume > 150) return "MEDIUM";
        return "LIGHT";
    }

    /// <summary>Counts triangles per node instance (not per unique mesh), matching what a
    /// real render/export triangle budget looks like when the same mesh is reused many times
    /// (mirrored wings, repeated greebles, ...).</summary>
    public static int CountTriangles(ModelRoot model)
    {
        var count = 0;
        foreach (var node in model.LogicalNodes)
        {
            if (node.Mesh is null) continue;
            foreach (var prim in node.Mesh.Primitives)
                count += prim.GetIndices().Count / 3;
        }
        return count;
    }
}
