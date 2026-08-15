using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;

namespace ShipDesign.Core.Procedural;

/// <summary>Flat-shaded triangle/quad/box helpers (each face gets its own normal, vertices
/// duplicated per face) -- the building blocks voxel meshing assembles cube faces from.</summary>
public static class MeshUtil
{
    /// <summary>
    /// IPrimitiveBuilder.AddTriangle only accepts IVertexBuilder; going through the
    /// non-generic interface (needed so these helpers work for any vertex type) means the
    /// VertexPositionNormal -&gt; IVertexBuilder implicit conversion has to be spelled out.
    /// </summary>
    private static IVertexBuilder V(VertexPositionNormal v) => new VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>(v);

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

    /// <summary>Adds a flat-shaded box, centered at <paramref name="center"/>, to an existing
    /// primitive -- used to build up multi-block structures (superstructure tiers, turrets)
    /// as a single mesh rather than one mesh per box.</summary>
    public static void AddBox(IPrimitiveBuilder prim, Vector3 center, Vector3 halfExtents) =>
        AddBoxFaces(prim, center, halfExtents, true, true, true, true, true, true);

    /// <summary>
    /// Like <see cref="AddBox"/>, but each of the 6 faces can be independently skipped --
    /// the basis of voxel face-culled meshing (VoxelMesher only requests the faces that touch
    /// an empty neighbor, so adjacent filled voxels don't emit hidden internal geometry).
    /// </summary>
    public static void AddBoxFaces(IPrimitiveBuilder prim, Vector3 center, Vector3 halfExtents,
        bool posX, bool negX, bool posY, bool negY, bool posZ, bool negZ)
    {
        Vector3 C(float sx, float sy, float sz) =>
            center + new Vector3(sx * halfExtents.X, sy * halfExtents.Y, sz * halfExtents.Z);

        if (posZ) AddFlatQuad(prim, C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1));
        if (negZ) AddFlatQuad(prim, C(1, -1, -1), C(-1, -1, -1), C(-1, 1, -1), C(1, 1, -1));
        if (negX) AddFlatQuad(prim, C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1));
        if (posX) AddFlatQuad(prim, C(1, -1, 1), C(1, -1, -1), C(1, 1, -1), C(1, 1, 1));
        if (posY) AddFlatQuad(prim, C(-1, 1, 1), C(1, 1, 1), C(1, 1, -1), C(-1, 1, -1));
        if (negY) AddFlatQuad(prim, C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1));
    }
}
