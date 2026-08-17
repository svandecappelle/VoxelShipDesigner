namespace ShipDesign.Core.Procedural;

/// <summary>
/// Per-hull-class growth tuning for VoxelShipGrower: where the nose taper ends and the tail
/// taper begins (as a 0..1 fraction of length), how flat the cross-section is (height relative
/// to width), how jagged the random-walk envelope is (more jaggedness = more silhouette
/// variation between seeds), and the designation prefix.
/// </summary>
public sealed class HullClassPreset
{
    public required float NoseFraction { get; init; }
    public required float TailFraction { get; init; }

    /// <summary>
    /// How flat/wedge-like (low) vs. boxy/round (high) this class's cross-section reads.
    ///
    /// This used to be a fraction of the half-*width*, which made the beam slider silently control
    /// the height as well: widening a ship made it taller, and a wide flat hull was unreachable at
    /// any setting. It is now a modulation of the ship's own depth, relative to
    /// <see cref="ReferenceHeightRatio"/> -- so the class still has a characteristic section, but
    /// the dimension it scales is the one the user asked for.
    /// </summary>
    public required float HeightRatio { get; init; }

    /// <summary>The ratio a class of average boxiness has. A class sitting on this value takes the
    /// requested depth as-is; the others read flatter or deeper than it in the same proportion they
    /// always did.</summary>
    public const float ReferenceHeightRatio = 0.65f;

    /// <summary>Random-walk step noise amplitude, relative to the hull's half-width (so a class
    /// stays equally rough at any voxel resolution). 0 = a smooth, repeatable envelope; higher =
    /// a rougher, more distinctive silhouette per seed.</summary>
    public required float Jaggedness { get; init; }

    public required string Prefix { get; init; }

    public static readonly IReadOnlyDictionary<HullClass, HullClassPreset> All = new Dictionary<HullClass, HullClassPreset>
    {
        [HullClass.Fighter] = new() { NoseFraction = 0.22f, TailFraction = 0.78f, HeightRatio = 0.70f, Jaggedness = 0.6f, Prefix = "FTR" },
        [HullClass.Corvette] = new() { NoseFraction = 0.20f, TailFraction = 0.80f, HeightRatio = 0.65f, Jaggedness = 0.9f, Prefix = "COR" },
        [HullClass.Freighter] = new() { NoseFraction = 0.12f, TailFraction = 0.85f, HeightRatio = 0.90f, Jaggedness = 1.4f, Prefix = "FRT" },
        [HullClass.Cruiser] = new() { NoseFraction = 0.35f, TailFraction = 0.85f, HeightRatio = 0.45f, Jaggedness = 1.1f, Prefix = "CRU" },
    };
}
