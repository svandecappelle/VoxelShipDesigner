using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Builds a stepped command tower (bridge) sitting on top of the hull, aft of center -- the
/// single structural element that most breaks up a "single smooth fuselage" silhouette into
/// something read as a capital ship (Star Destroyer bridge, Battlestar's CIC tower...) rather
/// than a rocket. Fighters are single-seat and skip it; their cockpit bubble already reads as
/// the "distinguishing top feature".
/// </summary>
public static class SuperstructureBuilder
{
    public static (IMeshBuilder<MaterialBuilder> Mesh, Matrix4x4 Transform)? Build(ShipParameters p, HullClassPreset preset)
    {
        if (!p.Superstructure || p.HullClass == HullClass.Fighter)
            return null;

        var u = MathF.Max(preset.TailFraction - 0.12f, preset.NoseFraction + 0.05f);
        var z = HullBuilder.ZAt(u, p.Length);
        var (halfWidth, halfHeight) = HullBuilder.ProfileAt(u, p, preset);

        var size = MathF.Max(p.SuperstructureSize, 0.1f);
        var baseHalf = new Vector3(halfWidth * 0.55f * size, halfHeight * 0.45f * size, p.Length * 0.06f * size);
        var topHalf = new Vector3(baseHalf.X * 0.55f, halfHeight * 0.3f * size, baseHalf.Z * 0.6f);

        var material = new MaterialBuilder("superstructure")
            .WithMetallicRoughness(0.4f, 0.5f)
            .WithBaseColor(p.HullColor.ToVector4());
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("superstructure");
        var prim = mesh.UsePrimitive(material);

        // Base tier sits directly on the hull surface; the top tier stacks on the base tier.
        var baseCenterY = halfHeight + baseHalf.Y;
        MeshUtil.AddBox(prim, new Vector3(0, baseCenterY, 0), baseHalf);

        var topCenterY = halfHeight + baseHalf.Y * 2f + topHalf.Y;
        MeshUtil.AddBox(prim, new Vector3(0, topCenterY, 0), topHalf);

        return (mesh, Matrix4x4.CreateTranslation(0, 0, z));
    }
}
