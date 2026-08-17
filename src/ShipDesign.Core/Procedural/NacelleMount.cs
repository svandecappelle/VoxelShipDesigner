namespace ShipDesign.Core.Procedural;

/// <summary>
/// Which hull the nacelle pylons spring from.
///
/// On a composite ship this is what separates a plausible Starfleet layout from a wrong one: the
/// pylons belong on the engineering hull, well aft and below, not on the saucer. Defaulting to the
/// widest hull -- which is what the generator did before there was a choice -- puts them on the
/// saucer's rim, where they read as ordinary wing pods.
/// </summary>
public enum NacelleMount
{
    /// <summary>Whichever hull reaches furthest outboard at that slice.</summary>
    Widest,

    /// <summary>The forward or centreline hull.</summary>
    Primary,

    /// <summary>The aft or outboard hull -- the engineering hull on a composite ship.</summary>
    Secondary,
}
