using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>Builds the engine nozzles (cylinder or torus "ring" style) plus an emissive glow
/// disc behind each, arranged in a circle around the hull's tail for counts &gt; 1.</summary>
public static class EngineBuilder
{
    private const int RadialSegments = 14;
    private const int GlowSegments = 14;

    public static IReadOnlyList<(IMeshBuilder<MaterialBuilder> Mesh, Matrix4x4 Transform)> Build(ShipParameters p, HullClassPreset preset)
    {
        var tailZ = HullBuilder.ZAt(1f, p.Length) - 0.05f;
        var (tailHalfWidth, tailHalfHeight) = HullBuilder.ProfileAt(0.96f, p, preset);
        var tailR = (tailHalfWidth + tailHalfHeight) / 2f;
        var count = Math.Max(p.EngineCount, 1);
        var nozzleR = MathF.Max(tailR / (count > 1 ? 2.2f : 1.4f), 0.15f);
        var nozzleLen = p.Length * 0.14f;

        var nozzleMaterial = new MaterialBuilder("engine_nozzle")
            .WithMetallicRoughness(0.7f, 0.35f)
            .WithBaseColor(new Vector4(0.125f, 0.148f, 0.169f, 1f));
        var glowMaterial = new MaterialBuilder("engine_glow")
            .WithBaseColor(p.EngineGlowColor.ToVector4())
            .WithEmissive(p.EngineGlowColor.ToVector3(), 1.4f);

        var nozzleMesh = p.EngineStyle == EngineStyle.Ring
            ? MeshUtil.BuildTorus(nozzleR * 1.1f, nozzleR * 0.35f, nozzleMaterial)
            : BuildNozzleCylinder(nozzleR * 0.75f, nozzleR, nozzleLen, nozzleMaterial);
        var glowMesh = BuildGlowDisc(nozzleR * 0.6f, glowMaterial);

        var results = new List<(IMeshBuilder<MaterialBuilder>, Matrix4x4)>();
        foreach (var (x, y) in EnginePositions(count, p, preset))
        {
            results.Add((nozzleMesh, Matrix4x4.CreateTranslation(x, y, tailZ - nozzleLen / 2f)));
            results.Add((glowMesh, Matrix4x4.CreateTranslation(x, y, tailZ - nozzleLen - 0.02f)));
        }
        return results;
    }

    /// <summary>Engines sit inset from the actual (angular) hull boundary at their angle, so
    /// they visually conform to a boxy/wedge hull instead of floating on a perfect circle.</summary>
    private static IEnumerable<(float x, float y)> EnginePositions(int count, ShipParameters p, HullClassPreset preset)
    {
        if (count == 1)
        {
            yield return (0f, 0f);
            yield break;
        }

        for (var i = 0; i < count; i++)
        {
            var angle = (float)i / count * MathF.PI * 2f + MathF.PI / 4f;
            var radius = HullBuilder.RadiusAt(0.96f, angle, p, preset) * 0.55f;
            yield return (MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }
    }

    /// <summary>A tapered tube from z=0 (near the hull, radius=nearRadius) back to
    /// z=-length (the aft exhaust opening, radius=farRadius, normally the wider end).</summary>
    private static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildNozzleCylinder(
        float nearRadius, float farRadius, float length, MaterialBuilder material)
    {
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("engine_nozzle");
        var prim = mesh.UsePrimitive(material);

        var nearRing = MeshUtil.Ring(nearRadius, 0f, new Vector2(1f, 0f), RadialSegments);
        var farRing = MeshUtil.Ring(farRadius, -length, new Vector2(1f, 0f), RadialSegments);
        MeshUtil.StitchBand(prim, nearRing, farRing);

        return mesh;
    }

    private static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildGlowDisc(
        float radius, MaterialBuilder material)
    {
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("engine_glow");
        var prim = mesh.UsePrimitive(material);

        var ring = MeshUtil.Ring(radius, 0f, new Vector2(0f, -1f), GlowSegments);
        var apex = new VertexPositionNormal(Vector3.Zero, new Vector3(0, 0, -1));
        MeshUtil.StitchFan(prim, apex, ring);

        return mesh;
    }
}
