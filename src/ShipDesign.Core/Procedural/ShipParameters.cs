namespace ShipDesign.Core.Procedural;

/// <summary>All the knobs the procedural ship builder reads. Mutable, plain data — the UI
/// layer owns one instance and rebuilds the ship whenever a field changes.</summary>
public sealed class ShipParameters
{
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

    public float Length { get; set; } = 14f;
    public float Beam { get; set; } = 3.2f;
    public float Taper { get; set; } = 0.5f;
    public int Decks { get; set; } = 2;

    public WingStyle WingStyle { get; set; } = WingStyle.Swept;
    public float WingSpan { get; set; } = 6f;
    public float WingSweepDegrees { get; set; } = 30f;

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
