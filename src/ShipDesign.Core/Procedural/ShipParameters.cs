namespace ShipDesign.Core.Procedural;

/// <summary>All the knobs the procedural ship builder reads. Mutable, plain data — the UI
/// layer owns one instance and rebuilds the ship whenever a field changes.</summary>
public sealed class ShipParameters
{
    /// <summary>
    /// An independent copy. Generation runs on a worker thread while the UI keeps mutating the live
    /// instance, so the worker must be handed a snapshot -- reading fields that another thread is
    /// writing gives a ship built from half of one setting and half of the next.
    ///
    /// Copied by reflection rather than field by field for the same reason the silhouettes are: a
    /// parameter added later is carried without anyone remembering to add it here, and a snapshot
    /// that silently missed a field would be a very hard bug to see.
    /// </summary>
    public ShipParameters Clone()
    {
        var copy = new ShipParameters();
        foreach (var property in typeof(ShipParameters).GetProperties())
            if (property.CanWrite)
                property.SetValue(copy, property.GetValue(this));
        return copy;
    }

    public HullClass HullClass { get; set; } = HullClass.Fighter;

    /// <summary>Whether the hulls sit side by side or stack fore-and-aft with a neck between them.
    /// <see cref="HullCount"/> and <see cref="HullSpacing"/> only apply to the parallel
    /// arrangement.</summary>
    public HullArrangement HullArrangement { get; set; } = HullArrangement.Parallel;

    /// <summary>Share of the ship's length the forward hull takes on a composite ship. Around 0.45
    /// is the Starfleet proportion; lower makes a small saucer on a long engineering hull.</summary>
    public float PrimaryHullFraction { get; set; } = 0.45f;

    /// <summary>How far the aft hull hangs below the forward one, as a multiple of the forward
    /// hull's half-height. Larger values mean a longer, more visible neck.</summary>
    public float SecondaryHullDrop { get; set; } = 1.6f;

    /// <summary>
    /// How much of the length available to it the secondary hull actually uses, 0.25 to 1.
    ///
    /// <see cref="Length"/> is the whole ship's length and every hull was sized from it, so the
    /// length slider stretched the outrigger or the engineering hull along with the main one and
    /// there was no way to have a short sponson beside a long hull, or an engineering hull that stops
    /// short of the stern. At 1 the secondary fills its span as it always did.
    /// </summary>
    public float SecondaryHullLength { get; set; } = 1f;

    /// <summary>Number of parallel hulls: 1 is a conventional single hull, 2 a catamaran (two
    /// full-size hulls either side of the centreline), 3 a trimaran (a full-size centre hull with
    /// a smaller outrigger each side). Multi-hull ships are joined by lateral spars.</summary>
    public int HullCount { get; set; } = 1;

    /// <summary>Multiplier on the lateral gap between hulls. Only meaningful when
    /// <see cref="HullCount"/> is above 1.</summary>
    public float HullSpacing { get; set; } = 1f;

    /// <summary>Planform of the main hull.</summary>
    public HullShape HullShape { get; set; } = HullShape.Dart;

    /// <summary>Planform of the outboard hulls. Separate from <see cref="HullShape"/> so a
    /// trimaran can pair, say, a wedge centre hull with spindle outriggers. Ignored when
    /// <see cref="HullCount"/> is 1.</summary>
    public HullShape SecondaryHullShape { get; set; } = HullShape.Spindle;

    /// <summary>
    /// Radial arms joining an annular hull's band to its centre, turning a bare torus into a wheel.
    /// Ignored by every other planform.
    /// </summary>
    public int WheelSpokes { get; set; } = 4;

    /// <summary>A body at the hub of a wheel: the docking core a rotating ring turns around.</summary>
    public bool WheelHub { get; set; } = true;

    /// <summary>Hub size, as a multiple of its default. The default is a fraction of the ring's own
    /// inner radius, so it stays proportionate whatever the ring's size.</summary>
    public float WheelHubSize { get; set; } = 1f;

    public float Length { get; set; } = 14f;
    public float Beam { get; set; } = 3.2f;

    /// <summary>
    /// Moulded depth: how tall the hull is, independent of how wide it is.
    ///
    /// Height used to be derived from the beam alone, so the width slider moved both and a hull
    /// could not be made wide and flat, or narrow and tall, at any setting. The class's
    /// <see cref="HullClassPreset.HeightRatio"/> still modulates this, so a freighter stays boxier
    /// than a cruiser at the same depth.
    /// </summary>
    public float Depth { get; set; } = 1.9f;
    public float Taper { get; set; } = 0.5f;
    public int Decks { get; set; } = 2;

    /// <summary>
    /// How much of the hull's depth is moved above its mid-line, and how much of the lower chamfer
    /// is removed. At 0 the section is the symmetric flat-decked trapezoid; at 1 the underside is a
    /// single flat plane and all the taper is on top, which is the knife cross-section an Imperial
    /// wedge has and the single biggest thing separating that look from a smooth hull.
    /// </summary>
    public float KeelFlatness { get; set; }

    /// <summary>A terraced ridge running down the centreline and rising toward the stern.</summary>
    public bool DorsalSpine { get; set; }

    /// <summary>Height of that ridge, as a multiple of its default.</summary>
    public float SpineHeight { get; set; } = 1f;

    public WingStyle WingStyle { get; set; } = WingStyle.Swept;
    public float WingSpan { get; set; } = 6f;
    public float WingSweepDegrees { get; set; } = 30f;

    /// <summary>Gun barrels on the wingtips. On a cross planform that means four of them, which is
    /// most of what makes a snubfighter read as armed rather than as a shuttle.</summary>
    public bool WingtipCannons { get; set; }

    /// <summary>Scales barrel length and calibre together. One knob rather than two: they are not
    /// independent on a real gun, and a long thin barrel next to a short fat one reads as an
    /// error rather than as a choice.</summary>
    public float CannonSize { get; set; } = 1f;

    public int EngineCount { get; set; } = 2;
    public EngineStyle EngineStyle { get; set; } = EngineStyle.Standard;

    public CockpitStyle CockpitStyle { get; set; } = CockpitStyle.Bubble;
    public float CockpitSize { get; set; } = 1f;

    public bool Greebles { get; set; } = true;
    public float GreebleDensity { get; set; } = 0.5f;
    public int TurretCount { get; set; } = 4;
    public int Seed { get; set; } = 2291;

    public bool Superstructure { get; set; } = true;
    public float SuperstructureSize { get; set; } = 1f;

    /// <summary>
    /// Where the command tower sits along its hull, 0 at the bow and 1 at the stern. Previously this
    /// was only ever a per-seed jitter around mid-hull, so the aft-mounted bridge that defines an
    /// Imperial silhouette was unreachable at any setting.
    /// </summary>
    public float TowerPosition { get; set; } = 0.42f;

    /// <summary>The pair of geodesic sensor globes flanking the top of the tower.</summary>
    public bool TowerDomes { get; set; }

    public bool Nacelles { get; set; } = true;

    /// <summary>Which hull the pylons spring from.</summary>
    public NacelleMount NacelleMount { get; set; } = NacelleMount.Widest;

    /// <summary>Whether the pods read as thrusters or as warp nacelles.</summary>
    public NacelleStyle NacelleStyle { get; set; } = NacelleStyle.Thruster;

    /// <summary>Fore-and-aft depth of the pylon, as a multiple of its default thickness. Low values
    /// give a thin strut, high values the broad swept blade Starfleet pylons are.</summary>
    public float PylonChord { get; set; } = 1f;

    /// <summary>A large emissive dish set into the bow of the aft hull. Only meaningful on a
    /// composite ship, which is the only arrangement that has an aft hull with a bow.</summary>
    public bool Deflector { get; set; } = true;

    /// <summary>Pod cross-section scale. Split from <see cref="NacelleLength"/> so a pod can be
    /// stubby-and-fat or long-and-slim, which is a big part of what distinguishes an engine pod
    /// from a drop tank -- a single "size" knob could only scale both together.</summary>
    public float NacelleWidth { get; set; } = 1f;

    /// <summary>Pod length along the hull axis.</summary>
    public float NacelleLength { get; set; } = 1f;

    /// <summary>How far outboard the pods hang, as a multiplier on the clearance between the hull
    /// flank and the pod's inner edge. Low values tuck the pods against the hull; high values put
    /// them on long pylons well clear of it.</summary>
    public float NacelleSpacing { get; set; } = 1f;

    /// <summary>Vertical offset of the pods relative to the hull's mid-line, as a multiple of the
    /// pod radius. Negative slings them underneath; positive lifts them above the hull on raised
    /// pylons, the arrangement Star Trek's ships use.</summary>
    public float NacelleRise { get; set; } = -1f;

    /// <summary>How far aft of its pylon root each pod sits, as a fraction of the ship's length.
    /// Together with <see cref="NacelleRise"/> this gives the swept-back, raised pylon that reads
    /// as a warp nacelle rather than a slung-under engine pod.</summary>
    public float NacelleSweep { get; set; } = 0f;

    public ShipColor HullColor { get; set; } = ShipColor.HullDefault;
    public ShipColor AccentColor { get; set; } = ShipColor.AccentDefault;
    public ShipColor EngineGlowColor { get; set; } = ShipColor.EngineGlowDefault;
    public ShipColor CockpitTintColor { get; set; } = ShipColor.CockpitTintDefault;
}
