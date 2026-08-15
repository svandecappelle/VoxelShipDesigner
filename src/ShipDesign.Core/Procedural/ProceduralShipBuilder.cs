using System.Numerics;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace ShipDesign.Core.Procedural;

/// <summary>Assembles hull + wings + engines + cockpit + greebles into a single glTF model,
/// the one entry point the UI layer needs for the procedural generator.</summary>
public static class ProceduralShipBuilder
{
    public static ModelRoot Build(ShipParameters p)
    {
        var preset = HullClassPreset.All[p.HullClass];
        var scene = new SceneBuilder();

        scene.AddRigidMesh(HullBuilder.Build(p, preset), Matrix4x4.Identity);

        foreach (var (mesh, transform) in WingBuilder.Build(p, preset))
            scene.AddRigidMesh(mesh, transform);

        foreach (var (mesh, transform) in EngineBuilder.Build(p, preset))
            scene.AddRigidMesh(mesh, transform);

        var cockpit = CockpitBuilder.Build(p, preset);
        if (cockpit is not null)
            scene.AddRigidMesh(cockpit.Value.Mesh, cockpit.Value.Transform);

        var superstructure = SuperstructureBuilder.Build(p, preset);
        if (superstructure is not null)
            scene.AddRigidMesh(superstructure.Value.Mesh, superstructure.Value.Transform);

        foreach (var (mesh, transform) in NacelleBuilder.Build(p, preset))
            scene.AddRigidMesh(mesh, transform);

        foreach (var (mesh, transform) in GreebleBuilder.Build(p, preset))
            scene.AddRigidMesh(mesh, transform);

        return scene.ToGltf2();
    }

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
