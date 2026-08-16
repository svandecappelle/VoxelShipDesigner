namespace ShipDesign.Core.Procedural;

/// <summary>
/// The overall planform of a hull -- where along its length it is widest, and how it closes at
/// bow and stern. This is the coarsest shape control there is: two ships sharing a class, seed and
/// size still read as different designs if their planforms differ.
/// </summary>
public enum HullShape
{
    /// <summary>Long pointed bow opening into a broad after-body. The interceptor silhouette.</summary>
    Dart,

    /// <summary>Narrow at the bow, widening steadily to a broad stern -- the classic capital-ship
    /// arrowhead.</summary>
    Wedge,

    /// <summary>Widest amidships, closing toward both ends. Reads as a hauler or a liner.</summary>
    Spindle,

    /// <summary>Near-constant width with blunt ends: an industrial block.</summary>
    Slab,

    /// <summary>Broad bow, pinched waist, moderate stern -- a command-ship profile.</summary>
    Hammerhead,

    /// <summary>A broad, flat disc: wider than it is long, and thin. Unlike the elongated shapes
    /// its width comes from the ship's length rather than its beam, or it would just be a long
    /// ellipse.</summary>
    Saucer,

    /// <summary>A disc with the middle cut out -- an annulus. Closes to solid at bow and stern so
    /// the ring is a single continuous band.</summary>
    Ring,

    /// <summary>A hull split into two prongs at the bow that merge into a common after-body.</summary>
    Fork,
}

/// <summary>Width profiles for <see cref="HullShape"/>.</summary>
public static class HullShapeProfile
{
    /// <summary>
    /// Relative half-width at <paramref name="u"/> along the hull (0 at the bow, 1 at the stern),
    /// normalized so the widest point of every shape returns 1. <paramref name="taper"/> sharpens
    /// (high) or blunts (low) whichever end the shape tapers to, so the nose slider stays
    /// meaningful across all of them.
    /// </summary>
    public static float WidthAt(HullShape shape, float u, float taper)
    {
        u = Math.Clamp(u, 0f, 1f);
        var sharpness = 0.45f + taper * 1.1f;

        return shape switch
        {
            // Long point at the bow opening into a broad after-body that stays full to the stern.
            // The near-parallel rear is what separates it from a spindle, which closes at both ends.
            HullShape.Dart => u < 0.42f
                ? MathF.Pow(u / 0.42f, sharpness)
                : 1f - (u - 0.42f) / 0.58f * 0.08f,

            // Monotonic widening: the stern is the widest part of the ship.
            HullShape.Wedge => MathF.Pow(u, sharpness * 0.75f) * 0.88f + 0.12f * u,

            // Closes hard at both ends, fullest amidships.
            HullShape.Spindle => MathF.Pow(MathF.Sin(u * MathF.PI), 0.95f),

            // Blunt and near-parallel: only the extreme ends pull in at all.
            HullShape.Slab => u < 0.1f
                ? 0.55f + 0.45f * (u / 0.1f)
                : u > 0.93f
                    ? 1f - (u - 0.93f) / 0.07f * 0.25f
                    : 1f,

            // Wide bow, pinched waist around a third back, then a moderate after-body.
            HullShape.Hammerhead => u < 0.16f
                ? MathF.Pow(u / 0.16f, sharpness * 0.7f)
                : u < 0.4f
                    ? 1f - (u - 0.16f) / 0.24f * 0.42f
                    : 0.58f + (u - 0.4f) / 0.6f * 0.3f,

            // A true circular outline: the half-width traces a semicircle over the length.
            HullShape.Saucer or HullShape.Ring => MathF.Sqrt(MathF.Max(0f, 1f - Sq(2f * u - 1f))),

            // Broad forward where the prongs spread, narrowing into the joined after-body.
            HullShape.Fork => u < 0.55f
                ? 0.75f + 0.25f * MathF.Sin(u / 0.55f * MathF.PI)
                : 1f - (u - 0.55f) / 0.45f * 0.35f,

            _ => 1f,
        };
    }

    private static float Sq(float v) => v * v;

    /// <summary>
    /// Maximum half-width in voxels for this shape. Most planforms take it straight from the beam,
    /// but a disc has to derive it from the ship's *length* -- keyed to the beam, a saucer would
    /// come out as a long thin ellipse, which is the one thing a saucer must not be.
    /// </summary>
    public static int EffectiveHalfWidth(HullShape shape, int maxHalfWidth, int lengthVoxels) => shape switch
    {
        // Half the length: a saucer's diameter *is* its length, which is what makes the plan
        // circular. Anything less and it comes out an elongated oval instead of a disc.
        HullShape.Saucer or HullShape.Ring => Math.Max(maxHalfWidth, (int)MathF.Round(lengthVoxels * 0.5f)),
        HullShape.Fork => Math.Max(maxHalfWidth, (int)MathF.Round(lengthVoxels * 0.2f)),
        _ => maxHalfWidth,
    };

    /// <summary>Maximum half-height in voxels. Discs are flattened: their width now comes from the
    /// length, so keeping the full height would make a sphere rather than a saucer.</summary>
    public static int EffectiveHalfHeight(HullShape shape, int maxHalfHeight) => shape switch
    {
        HullShape.Saucer or HullShape.Ring => Math.Max(2, (int)MathF.Round(maxHalfHeight * 0.8f)),
        _ => maxHalfHeight,
    };

    /// <summary>
    /// Fraction of the outer half-width that is hollow at <paramref name="u"/>. 0 is a solid
    /// slice. This is what lets a planform have a hole in it at all -- an outline alone can only
    /// ever describe a solid lens.
    /// </summary>
    public static float InnerFractionAt(HullShape shape, float u)
    {
        u = Math.Clamp(u, 0f, 1f);

        return shape switch
        {
            // Annulus over the middle, ramping to solid before either end. The closure has to be
            // driven from u rather than left to the outer-width clamp: that clamp keeps a
            // one-voxel hole open right up to where the hull vanishes, which leaves two
            // unconnected crescents instead of a ring.
            HullShape.Ring => 0.55f * Math.Clamp((MathF.Min(u, 1f - u) - 0.12f) / 0.12f, 0f, 1f),

            // Hollow at the bow and closing by mid-length, which is what makes two prongs that
            // merge rather than two separate hulls.
            HullShape.Fork => u < 0.5f ? (0.5f - u) / 0.5f * 0.72f : 0f,

            _ => 0f,
        };
    }

    /// <summary>
    /// Relative height at <paramref name="u"/>. Kept separate from width because the two do not
    /// track each other: a wedge fans out sideways while staying flat, whereas a spindle swells in
    /// both axes at once, and using one curve for both would flatten that distinction.
    /// </summary>
    public static float HeightAt(HullShape shape, float u, float taper)
    {
        u = Math.Clamp(u, 0f, 1f);

        return shape switch
        {
            // Stays low and flat even where it is widest -- that flatness is the wedge's signature.
            HullShape.Wedge => 0.55f + 0.45f * MathF.Pow(u, 0.8f),

            // Swells in height with width, giving a rounded body rather than a plank.
            HullShape.Spindle => MathF.Pow(MathF.Sin(u * MathF.PI), 0.5f),

            // A tall slab-sided box.
            HullShape.Slab => WidthAt(HullShape.Slab, u, taper),

            // The bow flare is mostly lateral; the superstructure end carries the height.
            HullShape.Hammerhead => 0.6f + 0.4f * MathF.Pow(u, 0.7f),

            // Thickest at the centre, thinning to a rim -- a lens, not a cylinder.
            HullShape.Saucer => 0.45f + 0.55f * MathF.Sqrt(MathF.Max(0f, 1f - Sq(2f * u - 1f))),

            // The ring keeps a near-constant section all the way round its band.
            HullShape.Ring => 0.8f + 0.2f * MathF.Sin(u * MathF.PI),

            HullShape.Fork => 0.7f + 0.3f * MathF.Pow(u, 0.6f),

            _ => WidthAt(shape, u, taper),
        };
    }
}
