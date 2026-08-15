namespace ShipDesign.Core.Procedural;

/// <summary>
/// Per-hull-class shape tuning: where the nose taper ends and the tail taper begins (as a
/// 0..1 fraction of hull length), how much the mid-body bulges, how many radial segments to
/// use when revolving the profile, how much the tail narrows, and the designation prefix.
/// </summary>
public sealed class HullClassPreset
{
    public required float NoseFraction { get; init; }
    public required float TailFraction { get; init; }
    public required float Bulge { get; init; }
    public required int RadialSegments { get; init; }
    public required float TailRatio { get; init; }
    public required string Prefix { get; init; }

    public static readonly IReadOnlyDictionary<HullClass, HullClassPreset> All = new Dictionary<HullClass, HullClassPreset>
    {
        [HullClass.Fighter] = new() { NoseFraction = 0.32f, TailFraction = 0.78f, Bulge = 0.05f, RadialSegments = 8, TailRatio = 0.40f, Prefix = "FTR" },
        [HullClass.Corvette] = new() { NoseFraction = 0.24f, TailFraction = 0.70f, Bulge = 0.12f, RadialSegments = 10, TailRatio = 0.45f, Prefix = "COR" },
        [HullClass.Freighter] = new() { NoseFraction = 0.14f, TailFraction = 0.55f, Bulge = 0.35f, RadialSegments = 6, TailRatio = 0.60f, Prefix = "FRT" },
        [HullClass.Cruiser] = new() { NoseFraction = 0.20f, TailFraction = 0.64f, Bulge = 0.20f, RadialSegments = 14, TailRatio = 0.50f, Prefix = "CRU" },
    };
}
