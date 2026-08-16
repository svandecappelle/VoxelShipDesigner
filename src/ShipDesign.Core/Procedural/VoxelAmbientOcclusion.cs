namespace ShipDesign.Core.Procedural;

/// <summary>
/// Per-corner ambient occlusion for voxel faces -- the darkening that appears where blocks meet.
/// It is the single biggest thing separating a flat-shaded pile of cubes from something that reads
/// as built: without it, every face of a given material is exactly the same colour and the eye has
/// nothing to pick edges out with.
///
/// No ray casting is involved. For a given face corner only three voxels can shade it: the two
/// that share an edge with the corner, and the one diagonally across it. Counting those three is
/// exactly the technique voxel-game renderers use, and it is why this is cheap enough to run over
/// a hundred thousand voxels without being noticed.
/// </summary>
public static class VoxelAmbientOcclusion
{
    /// <summary>Number of distinct occlusion levels a corner can take, 0 (deepest crevice) to 3
    /// (fully open).</summary>
    public const int Levels = 4;

    /// <summary>
    /// Occlusion at one corner of one face, as 0..3. <paramref name="normal"/> is the face's
    /// outward direction; <paramref name="tangentA"/> and <paramref name="tangentB"/> are the two
    /// in-plane directions pointing at the corner being evaluated.
    /// </summary>
    public static int CornerLevel(
        VoxelGrid grid,
        (int X, int Y, int Z) voxel,
        (int X, int Y, int Z) normal,
        (int X, int Y, int Z) tangentA,
        (int X, int Y, int Z) tangentB)
    {
        // Everything is sampled one step out along the normal: occluders sit in the layer in
        // front of the face, not in the solid the face belongs to.
        var baseX = voxel.X + normal.X;
        var baseY = voxel.Y + normal.Y;
        var baseZ = voxel.Z + normal.Z;

        var sideA = grid.IsFilled(baseX + tangentA.X, baseY + tangentA.Y, baseZ + tangentA.Z);
        var sideB = grid.IsFilled(baseX + tangentB.X, baseY + tangentB.Y, baseZ + tangentB.Z);

        // Both edge neighbours filled means the corner is boxed in; whatever sits diagonally
        // cannot make it any darker, and checking it would be wasted work.
        if (sideA && sideB) return 0;

        var corner = grid.IsFilled(
            baseX + tangentA.X + tangentB.X,
            baseY + tangentA.Y + tangentB.Y,
            baseZ + tangentA.Z + tangentB.Z);

        return 3 - ((sideA ? 1 : 0) + (sideB ? 1 : 0) + (corner ? 1 : 0));
    }

    /// <summary>
    /// Brightness multiplier for an occlusion level. Deliberately bottoms out well above zero:
    /// crevices should read as shaded, not as holes, and a floor keeps the darkest corners from
    /// swallowing the hull colour entirely.
    /// </summary>
    public static float Shade(int level, float floor = 0.42f)
    {
        var t = Math.Clamp(level, 0, Levels - 1) / (float)(Levels - 1);
        return floor + (1f - floor) * t;
    }

    /// <summary>The two in-plane axes for a face direction, used to walk its four corners.</summary>
    public static ((int X, int Y, int Z) A, (int X, int Y, int Z) B) TangentsFor((int X, int Y, int Z) normal)
    {
        if (normal.X != 0) return ((0, 1, 0), (0, 0, 1));
        if (normal.Y != 0) return ((1, 0, 0), (0, 0, 1));
        return ((1, 0, 0), (0, 1, 0));
    }

    /// <summary>
    /// Mean occlusion over a face's four corners. The studio view shades whole faces rather than
    /// interpolating across them, because WPF's Media3D has no per-vertex colour to interpolate
    /// with -- so a single value per face is all it can carry.
    /// </summary>
    public static float FaceShade(VoxelGrid grid, (int X, int Y, int Z) voxel, (int X, int Y, int Z) normal)
    {
        var (a, b) = TangentsFor(normal);
        var total = 0;

        for (var i = 0; i < 4; i++)
        {
            var signA = (i & 1) == 0 ? 1 : -1;
            var signB = (i & 2) == 0 ? 1 : -1;
            total += CornerLevel(grid, voxel, normal,
                (a.X * signA, a.Y * signA, a.Z * signA),
                (b.X * signB, b.Y * signB, b.Z * signB));
        }

        return Shade((int)MathF.Round(total / 4f));
    }
}
