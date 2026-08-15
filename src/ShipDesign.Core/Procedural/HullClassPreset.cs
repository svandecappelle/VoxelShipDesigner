namespace ShipDesign.Core.Procedural;

/// <summary>
/// A single cross-section along the hull, as a fraction of length (U, 0=nose, 1=tail) and
/// fractions of Beam for the half-width and half-height at that point. HullBuilder linearly
/// interpolates between consecutive points -- the slope changes at each point on purpose,
/// giving the hull sharp creases instead of a smooth curve (hard-surface look, not aerodynamic).
/// </summary>
public readonly record struct HullProfilePoint(float U, float Width, float Height);

/// <summary>
/// Per-hull-class shape: the width/height profile along the length (each class's silhouette
/// is a distinct sci-fi archetype -- needle fighter, elongated corvette, boxy freighter,
/// flat wedge cruiser), how sharp the cross-section's corners are, and the designation prefix.
/// </summary>
public sealed class HullClassPreset
{
    public required IReadOnlyList<HullProfilePoint> Profile { get; init; }
    public required float NoseFraction { get; init; }
    public required float TailFraction { get; init; }

    /// <summary>0 = sharp rectangular cross-section, ~0.4 = heavily chamfered (near-octagonal).</summary>
    public required float Chamfer { get; init; }

    public required int RadialSegments { get; init; }
    public required string Prefix { get; init; }

    public static readonly IReadOnlyDictionary<HullClass, HullClassPreset> All = new Dictionary<HullClass, HullClassPreset>
    {
        // Needle-nosed interceptor: narrow, aircraft-like proportions but faceted, not smooth.
        [HullClass.Fighter] = new()
        {
            Profile = new[]
            {
                new HullProfilePoint(0.00f, 0.00f, 0.00f),
                new HullProfilePoint(0.12f, 0.35f, 0.30f),
                new HullProfilePoint(0.25f, 0.70f, 0.55f),
                new HullProfilePoint(0.50f, 1.00f, 0.65f),
                new HullProfilePoint(0.75f, 0.75f, 0.55f),
                new HullProfilePoint(1.00f, 0.45f, 0.40f),
            },
            NoseFraction = 0.25f,
            TailFraction = 0.75f,
            Chamfer = 0.35f,
            RadialSegments = 8,
            Prefix = "FTR",
        },
        // Elongated patrol vessel: longer flat mid-section than the fighter, boxier corners.
        [HullClass.Corvette] = new()
        {
            Profile = new[]
            {
                new HullProfilePoint(0.00f, 0.00f, 0.00f),
                new HullProfilePoint(0.10f, 0.30f, 0.28f),
                new HullProfilePoint(0.22f, 0.65f, 0.50f),
                new HullProfilePoint(0.60f, 0.95f, 0.60f),
                new HullProfilePoint(0.80f, 0.70f, 0.50f),
                new HullProfilePoint(1.00f, 0.50f, 0.42f),
            },
            NoseFraction = 0.22f,
            TailFraction = 0.80f,
            Chamfer = 0.30f,
            RadialSegments = 10,
            Prefix = "COR",
        },
        // Blocky industrial hauler: near-blunt nose, long flat boxy body, minimal rounding.
        [HullClass.Freighter] = new()
        {
            Profile = new[]
            {
                new HullProfilePoint(0.00f, 0.05f, 0.05f),
                new HullProfilePoint(0.06f, 0.55f, 0.55f),
                new HullProfilePoint(0.15f, 0.95f, 0.85f),
                new HullProfilePoint(0.85f, 1.00f, 0.90f),
                new HullProfilePoint(0.95f, 0.85f, 0.75f),
                new HullProfilePoint(1.00f, 0.65f, 0.60f),
            },
            NoseFraction = 0.15f,
            TailFraction = 0.85f,
            Chamfer = 0.12f,
            RadialSegments = 6,
            Prefix = "FRT",
        },
        // Flat wedge, Star-Destroyer-like: wide and low (width >> height) with a long shallow
        // taper to a pointed bow instead of a short nose cone, sharp military edges.
        [HullClass.Cruiser] = new()
        {
            Profile = new[]
            {
                new HullProfilePoint(0.00f, 0.00f, 0.00f),
                new HullProfilePoint(0.40f, 0.85f, 0.35f),
                new HullProfilePoint(0.85f, 1.00f, 0.45f),
                new HullProfilePoint(1.00f, 0.75f, 0.42f),
            },
            NoseFraction = 0.40f,
            TailFraction = 0.85f,
            Chamfer = 0.15f,
            RadialSegments = 14,
            Prefix = "CRU",
        },
    };
}
