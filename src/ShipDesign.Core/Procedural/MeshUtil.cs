using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>Small helpers shared by the hull/wing/engine/cockpit builders for stitching
/// rings of vertices into quad bands — the common pattern behind lathes, cylinders and tori.</summary>
internal static class MeshUtil
{
    public static VertexPositionNormal[] Ring(float radius, float z, Vector2 normal2D, int segments)
    {
        var ring = new VertexPositionNormal[segments];
        for (var k = 0; k < segments; k++)
        {
            var theta = k / (float)segments * (2f * MathF.PI);
            var (sin, cos) = (MathF.Sin(theta), MathF.Cos(theta));
            var position = new Vector3(radius * cos, radius * sin, z);
            var normal = Vector3.Normalize(new Vector3(normal2D.X * cos, normal2D.X * sin, normal2D.Y));
            ring[k] = new VertexPositionNormal(position, normal);
        }
        return ring;
    }

    /// <summary>
    /// IPrimitiveBuilder.AddTriangle only accepts IVertexBuilder; going through the
    /// non-generic interface (needed so these helpers work for any vertex type) means the
    /// VertexPositionNormal -&gt; IVertexBuilder implicit conversion has to be spelled out.
    /// </summary>
    private static IVertexBuilder V(VertexPositionNormal v) => new VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>(v);

    /// <summary>Connects two same-size closed rings with a band of quads (as pairs of triangles).</summary>
    public static void StitchBand(IPrimitiveBuilder prim, IReadOnlyList<VertexPositionNormal> ringA, IReadOnlyList<VertexPositionNormal> ringB)
    {
        var n = ringA.Count;
        for (var k = 0; k < n; k++)
        {
            var k1 = (k + 1) % n;
            if (Vector3.DistanceSquared(ringA[k].Position, ringA[k1].Position) > 1e-10f)
                prim.AddTriangle(V(ringA[k]), V(ringB[k1]), V(ringA[k1]));
            if (Vector3.DistanceSquared(ringB[k1].Position, ringB[k].Position) > 1e-10f)
                prim.AddTriangle(V(ringA[k]), V(ringB[k]), V(ringB[k1]));
        }
    }

    /// <summary>Fans a ring in to a single apex point (a lathe nose/tail cap).</summary>
    public static void StitchFan(IPrimitiveBuilder prim, VertexPositionNormal apex, IReadOnlyList<VertexPositionNormal> ring, bool flip = false)
    {
        var n = ring.Count;
        for (var k = 0; k < n; k++)
        {
            var k1 = (k + 1) % n;
            if (flip)
                prim.AddTriangle(V(apex), V(ring[k]), V(ring[k1]));
            else
                prim.AddTriangle(V(apex), V(ring[k1]), V(ring[k]));
        }
    }

    /// <summary>A torus centered on the origin with its hole axis along Z (matching the hull's
    /// own revolution axis), built as a ring of tube cross-sections.</summary>
    public static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildTorus(
        float majorRadius, float minorRadius, MaterialBuilder material, int tubeSegments = 10, int ringSegments = 20)
    {
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("torus");
        var prim = mesh.UsePrimitive(material);

        var tubeRings = new VertexPositionNormal[ringSegments][];
        for (var i = 0; i < ringSegments; i++)
        {
            var phi = (float)i / ringSegments * MathF.PI * 2f;
            var (sinPhi, cosPhi) = (MathF.Sin(phi), MathF.Cos(phi));
            var ring = new VertexPositionNormal[tubeSegments];
            for (var j = 0; j < tubeSegments; j++)
            {
                var theta = (float)j / tubeSegments * MathF.PI * 2f;
                var (sinT, cosT) = (MathF.Sin(theta), MathF.Cos(theta));
                var r = majorRadius + minorRadius * cosT;
                var position = new Vector3(r * cosPhi, r * sinPhi, minorRadius * sinT);
                var normal = new Vector3(cosT * cosPhi, cosT * sinPhi, sinT);
                ring[j] = new VertexPositionNormal(position, normal);
            }
            tubeRings[i] = ring;
        }

        for (var i = 0; i < ringSegments; i++)
            StitchBand(prim, tubeRings[i], tubeRings[(i + 1) % ringSegments]);

        return mesh;
    }

    // ---- Flat-shaded helpers (each face gets its own normal, vertices duplicated per face) --
    // used for the hard-surface hull, where creased panel edges are the desired look rather
    // than smoothed-over ones.

    public static void AddFlatTriangle(IPrimitiveBuilder prim, Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() < 1e-12f)
            return;
        normal = Vector3.Normalize(normal);
        prim.AddTriangle(
            V(new VertexPositionNormal(a, normal)),
            V(new VertexPositionNormal(b, normal)),
            V(new VertexPositionNormal(c, normal)));
    }

    public static void AddFlatQuad(IPrimitiveBuilder prim, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        AddFlatTriangle(prim, a, b, c);
        AddFlatTriangle(prim, a, c, d);
    }

    /// <summary>Connects two same-size rings with a band of flat-shaded quads.</summary>
    public static void AddFlatBand(IPrimitiveBuilder prim, IReadOnlyList<Vector3> ringA, IReadOnlyList<Vector3> ringB)
    {
        var n = ringA.Count;
        for (var k = 0; k < n; k++)
        {
            var k1 = (k + 1) % n;
            AddFlatQuad(prim, ringA[k], ringA[k1], ringB[k1], ringB[k]);
        }
    }

    /// <summary>Fans a ring in to a single apex point with flat-shaded triangles (a faceted
    /// nose/tail cap, as opposed to StitchFan's smoothly-shaded lathe cap).</summary>
    public static void AddFlatFan(IPrimitiveBuilder prim, Vector3 apex, IReadOnlyList<Vector3> ring)
    {
        var n = ring.Count;
        for (var k = 0; k < n; k++)
        {
            var k1 = (k + 1) % n;
            AddFlatTriangle(prim, apex, ring[k], ring[k1]);
        }
    }
}
