using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Builds hull-like volumes as a sequence of flat-shaded octagonal (chamfered-rectangle)
/// cross-sections along the Z axis, linearly interpolated between profile control points.
/// Deliberately faceted rather than smoothly curved: the goal is a "hard sci-fi" military
/// silhouette (Star Destroyer / X-wing family) rather than the aerodynamic-aircraft look a
/// smooth lathe surface reads as. Used both for the ship's main hull (see HullClassPreset) and,
/// via the generic <see cref="BuildVolume"/>, for secondary volumes like NacelleBuilder's pods.
/// </summary>
public static class HullBuilder
{
    public static (float HalfWidth, float HalfHeight) ProfileAt(float u, ShipParameters p, HullClassPreset preset) =>
        ProfileAt(u, preset.Profile, p.Beam);

    public static (float HalfWidth, float HalfHeight) ProfileAt(float u, IReadOnlyList<HullProfilePoint> profile, float beamScale)
    {
        var i = 1;
        while (i < profile.Count - 1 && profile[i].U < u)
            i++;

        var a = profile[i - 1];
        var b = profile[i];
        var t = b.U > a.U ? Math.Clamp((u - a.U) / (b.U - a.U), 0f, 1f) : 0f;

        var width = a.Width + (b.Width - a.Width) * t;
        var height = a.Height + (b.Height - a.Height) * t;
        return (width * beamScale / 2f, height * beamScale / 2f);
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
        return RadiusAt(halfWidth, halfHeight, theta);
    }

    public static float RadiusAt(float halfWidth, float halfHeight, float theta)
    {
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
        return BuildVolume(preset.Profile, p.Length, p.Beam, preset.Chamfer, material, "hull");
    }

    /// <summary>
    /// The generic form of the main hull builder: a chamfered-octagon sweep along Z from a
    /// profile of (U, width-fraction, height-fraction) points, scaled by <paramref name="length"/>
    /// and <paramref name="beamScale"/>. Reused by NacelleBuilder for secondary hull-like pods.
    /// </summary>
    public static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildVolume(
        IReadOnlyList<HullProfilePoint> profile, float length, float beamScale, float chamfer,
        MaterialBuilder material, string meshName)
    {
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>(meshName);
        var prim = mesh.UsePrimitive(material);

        Vector3[]? previousRing = null;

        for (var i = 0; i < profile.Count; i++)
        {
            var point = profile[i];
            var z = ZAt(point.U, length);
            var halfWidth = point.Width * beamScale / 2f;
            var halfHeight = point.Height * beamScale / 2f;

            if (halfWidth < 1e-4f || halfHeight < 1e-4f)
            {
                // A point (nose/tail tip): fan out to the next ring instead of emitting a
                // zero-area octagon here.
                if (i + 1 < profile.Count)
                {
                    var next = profile[i + 1];
                    var nextRing = Octagon(
                        next.Width * beamScale / 2f, next.Height * beamScale / 2f, chamfer, ZAt(next.U, length));
                    MeshUtil.AddFlatFan(prim, new Vector3(0, 0, z), nextRing);
                }
                previousRing = null;
                continue;
            }

            var ring = Octagon(halfWidth, halfHeight, chamfer, z);
            if (previousRing is not null)
                MeshUtil.AddFlatBand(prim, previousRing, ring);

            previousRing = ring;
        }

        // The tail is left open where its profile ends at a non-zero radius -- whatever mounts
        // there (engines for the main hull) sits right behind it and masks the gap.
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
