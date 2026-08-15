using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Builds the hull as a surface of revolution around the Z axis (ship's length axis), from a
/// radius profile that tapers at the nose, optionally bulges in the mid-body, and narrows
/// (but doesn't fully close) at the tail. Port of vessel-forge.html's radiusAt/buildHull,
/// using an analytic tangent-based normal instead of relying on a library's auto-smoothing.
/// </summary>
public static class HullBuilder
{
    private const int Segments = 48;

    public static float RadiusAt(float u, ShipParameters p, HullClassPreset preset)
    {
        var beamR = p.Beam / 2f;
        float r;
        if (u < preset.NoseFraction)
        {
            var t = u / preset.NoseFraction;
            var exp = 1f + p.Taper * 3f;
            r = beamR * MathF.Pow(t, exp);
        }
        else if (u < preset.TailFraction)
        {
            var t = (u - preset.NoseFraction) / (preset.TailFraction - preset.NoseFraction);
            r = beamR * (1f + preset.Bulge * MathF.Sin(t * MathF.PI) * 0.4f);
        }
        else
        {
            var t = (u - preset.TailFraction) / (1f - preset.TailFraction);
            r = beamR * (1f - t * (1f - preset.TailRatio));
        }
        return MathF.Max(r, 0.03f);
    }

    public static float ZAt(float u, float length) => length / 2f - u * length;

    public static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> Build(
        ShipParameters p, HullClassPreset preset)
    {
        var profile = new (float r, float z)[Segments + 1];
        for (var i = 0; i <= Segments; i++)
        {
            var u = i / (float)Segments;
            profile[i] = (RadiusAt(u, p, preset), ZAt(u, p.Length));
        }

        Vector2 ProfileNormal(int i)
        {
            var prev = profile[Math.Max(i - 1, 0)];
            var next = profile[Math.Min(i + 1, Segments)];
            var dz = next.z - prev.z;
            var dr = next.r - prev.r;
            var len = MathF.Sqrt(dz * dz + dr * dr);
            return len < 1e-6f ? new Vector2(1f, 0f) : new Vector2(-dz / len, dr / len);
        }

        var material = new MaterialBuilder("hull")
            .WithMetallicRoughness(0.4f, 0.55f)
            .WithBaseColor(p.HullColor.ToVector4());
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("hull");
        var prim = mesh.UsePrimitive(material);

        // profile[0] collapses to a point (radius ~0), so it's the nose apex rather than a ring.
        var rings = new VertexPositionNormal[Segments + 1][];
        for (var i = 1; i <= Segments; i++)
            rings[i] = MeshUtil.Ring(profile[i].r, profile[i].z, ProfileNormal(i), preset.RadialSegments);

        var noseApex = new VertexPositionNormal(new Vector3(0, 0, profile[0].z), new Vector3(0, 0, 1));
        MeshUtil.StitchFan(prim, noseApex, rings[1], flip: true);

        for (var i = 1; i < Segments; i++)
            MeshUtil.StitchBand(prim, rings[i], rings[i + 1]);

        // The tail is left open (its radius is > 0, unlike the nose) -- engines sit right
        // behind it and mask the gap, same as the reference generator.
        return mesh;
    }
}
