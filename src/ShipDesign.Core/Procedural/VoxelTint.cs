namespace ShipDesign.Core.Procedural;

/// <summary>
/// Per-voxel colour variation: the mottling that makes hand-authored voxel art read as built out of
/// individually placed blocks rather than poured out of a mould.
///
/// Ambient occlusion and cast shadow already describe the <em>relief</em>. What they cannot describe
/// is the <em>material</em>: with a single colour per material role, two unoccluded hull voxels side
/// by side are pixel-identical, and the eye reads that flatness as plastic. Three signals fix it,
/// summed into one tone and quantised:
///
/// <list type="bullet">
/// <item><b>Patch noise</b>, on cells several voxels across rather than per voxel. Per-voxel noise is
/// television snow; patches read as plating, and are what the reference art actually shows.</item>
/// <item><b>A vertical gradient</b>, belly darker than upper decks. Not noise at all -- it is the
/// ambient sky/ground term a directional rig has no way to produce.</item>
/// <item><b>An edge highlight</b>, brightening voxels with many empty neighbours. Occlusion darkens
/// concave corners and nothing was lightening convex ones; this is the missing half, and it is the
/// single change that makes voxel silhouettes pop.</item>
/// </list>
///
/// Two properties are deliberate rather than incidental. The tone is a function of the voxel, not of
/// the face, so all six faces of a cube agree -- vary them independently and the cube dissolves into
/// confetti. And it is mirrored in X, because the generator makes every ship symmetric by
/// construction; variation that broke that symmetry would read as a bug on the turnaround sheet,
/// where both halves are side by side in one elevation.
/// </summary>
public sealed class VoxelTint
{
    /// <summary>
    /// Number of tones the variation is quantised to. Quantised for two reasons: WPF batches
    /// geometry by material, so a continuous colour would mean one draw call per voxel; and
    /// discrete tones drawn from a small family is what voxel artists do by hand, which looks
    /// deliberate where a continuous jitter looks noisy.
    /// </summary>
    public const int Variants = 5;

    /// <summary>The untinted middle tone, for callers that need a variant without a voxel.</summary>
    public const int Neutral = Variants / 2;

    private readonly VoxelGrid _grid;
    private readonly int _minY;
    private readonly float _spanY;

    private VoxelTint(VoxelGrid grid)
    {
        _grid = grid;
        _minY = grid.MinY;
        _spanY = Math.Max(1, grid.MaxY - grid.MinY);
    }

    public static VoxelTint For(VoxelGrid grid) => new(grid);

    /// <summary>
    /// Tone index for a voxel, 0 (darkest, coolest) to <see cref="Variants"/>-1.
    ///
    /// The three signals are summed and then rounded, which means their spread has to be wide
    /// enough to actually reach the end buckets: rounding a value whose standard deviation is half
    /// a bucket puts nearly everything on the middle tone and the extremes are never seen. How far
    /// apart the tones look is a separate question, settled by <see cref="Offsets"/> -- keeping the
    /// two apart is what lets the variation be evenly spread *and* subtle.
    /// </summary>
    public int VariantFor(int x, int y, int z)
    {
        var tone = 0.5f + Vertical(y) + Patch(x, y, z) + Edge(x, y, z);
        return Math.Clamp((int)MathF.Round(tone * (Variants - 1)), 0, Variants - 1);
    }

    /// <summary>
    /// HSL offsets for a tone. Most of the range is spent on hue and saturation on purpose:
    /// lightness is already carrying occlusion and cast shadow, and a variation large enough to
    /// compete with those would flatten the form it is supposed to decorate. Hue and saturation are
    /// free channels, and the eye reads a 6 degree hue split between neighbouring plates clearly.
    /// </summary>
    public static (float Lightness, float Saturation, float Hue) Offsets(int variant)
    {
        var d = Math.Clamp(variant, 0, Variants - 1) / (float)(Variants - 1) - 0.5f;

        // Same direction as the shading curve in ColorMath: darker is cooler and richer, lighter is
        // warmer and calmer. Pulling the two apart would make a tinted plate look like a different
        // material rather than the same one catching the light differently.
        return (d * 0.13f, -d * 0.12f, -d * 12f);
    }

    /// <summary>
    /// The tint as a per-channel multiplier on a base colour, for baking into vertex colours where
    /// only a multiplier survives. Channels near zero are left alone: dividing by them turns a
    /// rounding difference into a wild colour shift.
    /// </summary>
    public static (float R, float G, float B) Multiplier(ShipColor baseColour, int variant)
    {
        var tinted = ColorMath.Tinted(baseColour, Offsets(variant));
        return (Ratio(tinted.R, baseColour.R), Ratio(tinted.G, baseColour.G), Ratio(tinted.B, baseColour.B));
    }

    private static float Ratio(float tinted, float original) =>
        original < 0.02f ? 1f : Math.Clamp(tinted / original, 0.5f, 1.8f);

    /// <summary>Belly darker, upper decks lighter. Wide enough on its own to move a voxel a full
    /// tone from bottom to top, so the gradient survives the rounding instead of only nudging the
    /// patch noise about.</summary>
    private float Vertical(int y) => ((y - _minY) / _spanY - 0.5f) * 0.60f;

    /// <summary>
    /// Two octaves of cell noise. One alone lays down a visibly regular grid of patches; a second at
    /// a coprime cell size breaks the repeat without needing real gradient noise.
    /// </summary>
    private static float Patch(int x, int y, int z)
    {
        var ax = Math.Abs(x);   // mirrored in X, like the geometry
        return 0.36f * Cell(ax, y, z, 4, 3, 5, 0x9E3779B9)
             + 0.18f * Cell(ax, y, z, 9, 7, 11, 0x85EBCA6B);
    }

    /// <summary>
    /// How exposed a voxel is, as a highlight. Thresholded at one empty neighbour so a plain wall
    /// -- which has exactly one -- gets nothing, and only genuine edges, corners and protruding
    /// studs are picked out. A stud with five faces open clears a whole tone on this alone.
    /// </summary>
    private float Edge(int x, int y, int z)
    {
        var empty = 0;
        if (!_grid.IsFilled(x + 1, y, z)) empty++;
        if (!_grid.IsFilled(x - 1, y, z)) empty++;
        if (!_grid.IsFilled(x, y + 1, z)) empty++;
        if (!_grid.IsFilled(x, y - 1, z)) empty++;
        if (!_grid.IsFilled(x, y, z + 1)) empty++;
        if (!_grid.IsFilled(x, y, z - 1)) empty++;

        return Math.Max(0, empty - 1) / 4f * 0.30f;
    }

    /// <summary>Value in -1..1, constant over a cell of the given size.</summary>
    private static float Cell(int x, int y, int z, int cx, int cy, int cz, uint salt) =>
        Hash(FloorDiv(x, cx), FloorDiv(y, cy), FloorDiv(z, cz), salt) / (float)uint.MaxValue * 2f - 1f;

    /// <summary>Floor division. Truncating division would fold -1 and 0 into the same cell, putting
    /// a double-width patch through the middle of every ship.</summary>
    private static int FloorDiv(int value, int divisor)
    {
        var q = value / divisor;
        return value % divisor < 0 ? q - 1 : q;
    }

    private static uint Hash(int x, int y, int z, uint salt)
    {
        unchecked
        {
            var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ salt;
            h ^= h >> 16;
            h *= 0x7FEB352D;
            h ^= h >> 15;
            h *= 0x846CA68B;
            h ^= h >> 16;
            return h;
        }
    }
}
