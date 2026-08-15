namespace ShipDesign.Core.Procedural;

/// <summary>Which material role a voxel plays -- determines its color/emissive when meshed.
/// Hull/HullDark/Panel are three shades derived from the same hull color: having more than one
/// grey is what lets the detail passes read as panel seams and plating rather than as noise.</summary>
public enum VoxelMaterial
{
    /// <summary>Primary plating -- the base hull color.</summary>
    Hull,

    /// <summary>Recesses, panel seams, engine housings -- a darkened hull shade.</summary>
    HullDark,

    /// <summary>Raised plates and secondary panels -- a slightly dimmed hull shade.</summary>
    Panel,

    /// <summary>Squadron stripes and wing markings.</summary>
    Accent,

    /// <summary>Warm emissive port lights along the flanks and deck.</summary>
    Window,

    /// <summary>Engine exhaust.</summary>
    Glow,

    /// <summary>Tinted, translucent canopy glass.</summary>
    Cockpit,
}
