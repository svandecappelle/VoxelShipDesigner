namespace ShipDesign.Core.Procedural;

/// <summary>
/// Where a nacelle's light comes from, which is most of what distinguishes an engine pod from a
/// warp nacelle.
/// </summary>
public enum NacelleStyle
{
    /// <summary>A thruster: the pod is lit at its aft cap only, like any rocket.</summary>
    Thruster,

    /// <summary>
    /// A warp nacelle: a glowing collector dome capping the *front*, and a lit grille running along
    /// the outboard flank. The two together are the read -- a bright nose alone looks like a
    /// headlight, and a side stripe alone looks like paint.
    /// </summary>
    Warp,
}
