namespace ShipDesign.Core.Procedural;

/// <summary>
/// How a ship's hulls are arranged relative to each other.
///
/// The distinction is which axis the hulls are spread along, and it matters because it decides what
/// the ship reads as. Spread sideways and you get a catamaran or trimaran; spread fore-and-aft
/// *and* vertically, joined by a neck, and you get the Starfleet silhouette -- a saucer carried
/// ahead of and above an engineering hull.
/// </summary>
public enum HullArrangement
{
    /// <summary>Hulls side by side on the same axis, count set by HullCount.</summary>
    Parallel,

    /// <summary>A forward upper hull and an aft lower hull joined by a dorsal neck.</summary>
    Composite,
}
