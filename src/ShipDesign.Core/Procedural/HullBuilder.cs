using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Builds the hull as a sequence of flat-shaded octagonal (chamfered-rectangle) cross-sections
/// along the Z axis, linearly interpolated between each hull class's profile control points
/// (see HullClassPreset). Deliberately faceted rather than smoothly curved: the goal is a
/// "hard sci-fi" military silhouette (Star Destroyer / X-wing family) rather than the
/// aerodynamic-aircraft look a smooth lathe surface reads as.
/// </summary>
public static class HullBuilder
{
    private const int CornerCount = 8;

    public static (float HalfWidth, float HalfHeight) ProfileAt(float u, ShipParameters p, HullClassPreset preset)
    {
        var profile = preset.Profile;
        var i = 1;
        while (i < profile.Count - 1 && profile[i].U < u)
            i++;

        var a = profile[i - 1];
        var b = profile[i];
        var t = b.U > a.U ? Math.Clamp((u - a.U) / (b.U - a.U), 0f, 1f) : 0f;

        var width = a.Width + (b.Width - a.Width) * t;
        var height = a.Height + (b.Height - a.Height) * t;
        return (width * p.Beam / 2f, height * p.Beam / 2f);
    }

    public static float ZAt(float u, float length) => length / 2f - u * length;

    /// <summary>
    /// Approximate (elliptical) distance from the centerline to the hull surface at a given
    /// polar angle. Exact at the cardinal directions the wings (theta=0, the side) and cockpit
    /// (theta=pi/2, the top) attach at; a reasonable placement approximation elsewhere, since
    /// the actual cross-section is an octagon, not a smooth ellipse.
    /// </summary>
    public static float RadiusAt(float u, float theta, ShipParameters p, HullClassPreset preset)
    {
        var (halfWidth, halfHeight) = ProfileAt(u, p, preset);
        if (halfWidth < 1e-4f || halfHeight < 1e-4f)
            return 0f;

        var cos = MathF.Cos(theta);
        var sin = MathF.Sin(theta);
        var denom = MathF.Sqrt(cos * cos / (halfWidth * halfWidth) + sin * sin / (halfHeight * halfHeight));
        return denom < 1e-6f ? 0f : 1f / denom;
    }

    public static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> Build(
        ShipParameters p, HullClassPreset preset)
    {
        var material = new MaterialBuilder("hull")
            .WithMetallicRoughness(0.4f, 0.55f)
            .WithBaseColor(p.HullColor.ToVector4());
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("hull");
        var prim = mesh.UsePrimitive(material);

        var profile = preset.Profile;
        Vector3[]? previousRing = null;

        for (var i = 0; i < profile.Count; i++)
        {
            var point = profile[i];
            var z = ZAt(point.U, p.Length);
            var halfWidth = point.Width * p.Beam / 2f;
            var halfHeight = point.Height * p.Beam / 2f;

            if (halfWidth < 1e-4f || halfHeight < 1e-4f)
            {
                // A point (nose/tail tip): fan out to the next ring instead of emitting a
                // zero-area octagon here.
                if (i + 1 < profile.Count)
                {
                    var next = profile[i + 1];
                    var nextRing = Octagon(
                        next.Width * p.Beam / 2f, next.Height * p.Beam / 2f, preset.Chamfer, ZAt(next.U, p.Length));
                    MeshUtil.AddFlatFan(prim, new Vector3(0, 0, z), nextRing);
                }
                previousRing = null;
                continue;
            }

            var ring = Octagon(halfWidth, halfHeight, preset.Chamfer, z);
            if (previousRing is not null)
                MeshUtil.AddFlatBand(prim, previousRing, ring);

            previousRing = ring;
        }

        // The tail is left open where its profile ends at a non-zero radius (matching every
        // current preset) -- engines sit right behind it and mask the gap.
        return mesh;
    }

    private static Vector3[] Octagon(float halfWidth, float halfHeight, float chamfer, float z)
    {
        var cw = halfWidth * chamfer;
        var ch = halfHeight * chamfer;
        return new[]
        {
            new Vector3(halfWidth - cw, halfHeight, z),
            new Vector3(-halfWidth + cw, halfHeight, z),
            new Vector3(-halfWidth, halfHeight - ch, z),
            new Vector3(-halfWidth, -halfHeight + ch, z),
            new Vector3(-halfWidth + cw, -halfHeight, z),
            new Vector3(halfWidth - cw, -halfHeight, z),
            new Vector3(halfWidth, -halfHeight + ch, z),
            new Vector3(halfWidth, halfHeight - ch, z),
        };
    }
}
