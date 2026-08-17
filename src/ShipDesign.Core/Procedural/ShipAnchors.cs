namespace ShipDesign.Core.Procedural;

/// <summary>A point on the ship, in voxel coordinates.</summary>
public readonly record struct ShipAnchor(int X, int Y, int Z);

/// <summary>
/// Where each family of parameters attaches to the ship, so a UI can point at the thing a slider
/// actually moves.
///
/// Every member is always present, and that is the whole design constraint. The obvious approach --
/// have each growth pass record its own anchor as it builds its piece -- fails on the case that
/// matters most: with nacelles switched off, <c>GrowNacelles</c> returns immediately, no anchor is
/// recorded, the nacelle marker disappears, and the user can never switch nacelles back on. So the
/// anchors are derived instead from what exists regardless of the toggles: the per-seed
/// <c>Layout</c> and the hull envelopes, both settled before any conditional pass runs.
///
/// Plain integers, deliberately: this assembly has no WPF dependency and should keep it that way.
/// Multiply by <see cref="VoxelShipGrower.VoxelSize"/> to get world coordinates -- the meshers use
/// identity transforms throughout, so there is no other transform to undo.
///
/// The points are *nominal*. The surface-detail pass and the fragment sweep both run afterwards and
/// may have emptied the exact voxel named here; that is fine for something whose job is to say
/// "roughly there", and validating each one against the finished grid would cost more than it is
/// worth. Anchors on mirrored features (wings, nacelles) are given for the starboard side only --
/// the port twin is the same point with X negated.
/// </summary>
public sealed record ShipAnchors(
    ShipAnchor Bow,
    ShipAnchor HullMid,
    ShipAnchor Wing,
    ShipAnchor Engine,
    ShipAnchor Cockpit,
    ShipAnchor Tower,
    ShipAnchor Nacelle,
    ShipAnchor Surface);
