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

            _ => 1f,
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

            _ => WidthAt(shape, u, taper),
        };
    }
}
