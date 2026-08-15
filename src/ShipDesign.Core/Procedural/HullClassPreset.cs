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

    /// <summary>Max half-height as a fraction of max half-width -- how flat/wedge-like
    /// (low) vs. boxy/round (high) the cross-section reads.</summary>
    public required float HeightRatio { get; init; }

    /// <summary>Random-walk step noise amplitude, in voxels. 0 = a smooth, repeatable
    /// envelope; higher = a rougher, more distinctive silhouette per seed.</summary>
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
