using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Scatters small panel/greeble boxes across the hull's mid-body (seeded by
/// ShipParameters.Seed, so the same seed always gives the same detailing), plus a thin
/// glowing ring at each deck line.
/// </summary>
public static class GreebleBuilder
{
    public static IReadOnlyList<(IMeshBuilder<MaterialBuilder> Mesh, Matrix4x4 Transform)> Build(ShipParameters p, HullClassPreset preset)
    {
        var results = new List<(IMeshBuilder<MaterialBuilder>, Matrix4x4)>();
        if (!p.Greebles)
            return results;

        var random = new Random(p.Seed);
        var greebleMaterial = new MaterialBuilder("greeble")
            .WithMetallicRoughness(0.3f, 0.7f)
            .WithBaseColor(p.AccentColor.ToVector4());
        var greebleMesh = BuildUnitBox(greebleMaterial);

        var count = (int)MathF.Round(p.GreebleDensity * p.Length * 3f);
        for (var i = 0; i < count; i++)
        {
            var u = preset.NoseFraction + (float)random.NextDouble() * (preset.TailFraction - preset.NoseFraction);
            var angle = (float)random.NextDouble() * MathF.PI * 2f;
            var r = HullBuilder.RadiusAt(u, angle, p, preset);
            var z = HullBuilder.ZAt(u, p.Length);
            var w = 0.12f + (float)random.NextDouble() * 0.25f;
            var h = 0.05f + (float)random.NextDouble() * 0.12f;
            var l = 0.15f + (float)random.NextDouble() * 0.35f;

            // Unit box scaled per-instance (h,w,l), rotated to the hull angle, translated onto
            // the surface -- one mesh reused for every greeble instead of rebuilding geometry.
            var transform = Matrix4x4.CreateScale(h, w, l)
                * Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, angle)
                * Matrix4x4.CreateTranslation(MathF.Cos(angle) * r * 0.98f, MathF.Sin(angle) * r * 0.98f, z);
            results.Add((greebleMesh, transform));
        }

        var lineMaterial = new MaterialBuilder("deck_line")
            .WithMetallicRoughness(0.1f, 0.9f)
            .WithBaseColor(p.AccentColor.ToVector4())
            .WithEmissive(p.AccentColor.ToVector3(), 0.6f);
        var decks = Math.Max(1, p.Decks);
        for (var d = 1; d < decks; d++)
        {
            var u = preset.NoseFraction + (float)d / decks * (preset.TailFraction - preset.NoseFraction);
            var (halfWidth, halfHeight) = HullBuilder.ProfileAt(u, p, preset);
            var r = (halfWidth + halfHeight) / 2f;
            var z = HullBuilder.ZAt(u, p.Length);
            var ringMesh = MeshUtil.BuildTorus(r * 1.01f, MathF.Max(r * 0.02f, 0.01f), lineMaterial,
                tubeSegments: 6, ringSegments: preset.RadialSegments);
            results.Add((ringMesh, Matrix4x4.CreateTranslation(0, 0, z)));
        }

        return results;
    }

    /// <summary>A 1x1x1 box spanning local X in [0,1] (the "sticking out" direction) and Y/Z
    /// in [-0.5,0.5], meant to be scaled non-uniformly per greeble instance.</summary>
    private static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildUnitBox(MaterialBuilder material)
    {
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("greeble_unit");
        var prim = mesh.UsePrimitive(material);

        var corners = new[]
        {
            new Vector3(0, -0.5f, -0.5f), new Vector3(1, -0.5f, -0.5f),
            new Vector3(1, 0.5f, -0.5f), new Vector3(0, 0.5f, -0.5f),
            new Vector3(0, -0.5f, 0.5f), new Vector3(1, -0.5f, 0.5f),
            new Vector3(1, 0.5f, 0.5f), new Vector3(0, 0.5f, 0.5f),
        };

        void Quad(int a, int b, int c, int d, Vector3 normal)
        {
            var va = new VertexPositionNormal(corners[a], normal);
            var vb = new VertexPositionNormal(corners[b], normal);
            var vc = new VertexPositionNormal(corners[c], normal);
            var vd = new VertexPositionNormal(corners[d], normal);
            prim.AddTriangle(Wrap(va), Wrap(vb), Wrap(vc));
            prim.AddTriangle(Wrap(va), Wrap(vc), Wrap(vd));
        }

        Quad(4, 5, 6, 7, Vector3.UnitZ);
        Quad(1, 0, 3, 2, -Vector3.UnitZ);
        Quad(0, 4, 7, 3, -Vector3.UnitX);
        Quad(5, 1, 2, 6, Vector3.UnitX);
        Quad(3, 7, 6, 2, Vector3.UnitY);
        Quad(0, 1, 5, 4, -Vector3.UnitY);

        return mesh;
    }

    private static IVertexBuilder Wrap(VertexPositionNormal v) => new VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>(v);
}
