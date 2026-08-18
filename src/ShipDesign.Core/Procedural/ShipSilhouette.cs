namespace ShipDesign.Core.Procedural;

/// <summary>
/// A named starting point: the twenty-odd knobs that together make a recognisable kind of ship.
///
/// The parameters are individually meaningful but the combinations that read as something are not
/// obvious from any one of them. Reaching the Starfleet layout, for instance, means setting the
/// arrangement, both hull shapes, the nacelle mount, the nacelle lighting, the pylon chord, the
/// pod rise and sweep, and turning the wings off -- nine decisions, none of which does much alone.
/// A silhouette is that whole set under one name.
///
/// Applied onto existing parameters rather than replacing them, and deliberately leaving the seed
/// and the four livery colours alone: those are the user's, and a preset that repainted the ship
/// and reshuffled it would be a reset button wearing a different label.
///
/// Each silhouette is a *complete* parameter set rather than a list of edits, and
/// <see cref="ApplyTo"/> copies every field across. A silhouette that only wrote the fields it
/// cared about would leave the previous one's values showing through wherever the two disagreed
/// about what was worth mentioning -- which is how clicking "Chasseur" after "Croiseur Starfleet"
/// produces a fighter with a cruiser's canopy. Copying wholesale also means a parameter added later
/// is carried by every silhouette without any of them being touched.
/// </summary>
public sealed record ShipSilhouette(string Universe, string Name, string Summary, Func<ShipParameters> Template)
{
    /// <summary>What a silhouette must not touch: the seed and the livery belong to the user, not
    /// to the shape.</summary>
    private static readonly HashSet<string> Preserved = new()
    {
        nameof(ShipParameters.Seed),
        nameof(ShipParameters.HullColor),
        nameof(ShipParameters.AccentColor),
        nameof(ShipParameters.EngineGlowColor),
        nameof(ShipParameters.CockpitTintColor),
    };

    public void ApplyTo(ShipParameters target)
    {
        var template = Template();
        foreach (var property in typeof(ShipParameters).GetProperties())
            if (property.CanWrite && !Preserved.Contains(property.Name))
                property.SetValue(target, property.GetValue(template));
    }

    private static ShipSilhouette Of(string universe, string name, string summary, Action<ShipParameters> build) =>
        new(universe, name, summary, () => { var p = new ShipParameters(); build(p); return p; });

    /// <summary>Names of the universes, in the order they should be offered. Free-form strings
    /// rather than an enum on purpose: adding a variant for a new franchise should be one entry in
    /// <see cref="All"/> and nothing else, with the group appearing by itself.</summary>
    public const string StarTrek = "Star Trek";
    public const string StarWars = "Star Wars";
    public const string Archetypes = "Archétypes";

    public static IReadOnlyList<ShipSilhouette> All { get; } = new[]
    {
        Of(StarTrek, "Croiseur Starfleet",
            "Soucoupe portée devant et au-dessus d'une coque d'ingénierie, nacelles warp sur pylônes en lame",
            p =>
            {
                p.HullClass = HullClass.Cruiser;
                p.HullArrangement = HullArrangement.Composite;
                p.HullShape = HullShape.Saucer;
                p.SecondaryHullShape = HullShape.Spindle;
                p.PrimaryHullFraction = 0.45f;
                p.SecondaryHullDrop = 1.6f;
                p.Deflector = true;
                p.Length = 24f;
                p.Beam = 6f;
                p.Depth = 2.6f;
                p.Taper = 0.45f;
                p.Decks = 3;
                p.WingStyle = WingStyle.None;
                p.EngineCount = 2;
                p.EngineStyle = EngineStyle.Standard;
                p.CockpitStyle = CockpitStyle.None;
                p.Superstructure = true;
                p.SuperstructureSize = 1.1f;
                p.Nacelles = true;
                p.NacelleMount = NacelleMount.Secondary;
                p.NacelleStyle = NacelleStyle.Warp;
                p.NacelleWidth = 0.9f;
                p.NacelleLength = 1.3f;
                p.NacelleSpacing = 2.2f;
                p.NacelleRise = 1.8f;
                p.NacelleSweep = 0.12f;
                p.PylonChord = 3.5f;
                p.TurretCount = 2;
                p.GreebleDensity = 0.55f;
            }),

        Of(StarWars, "Destroyer impérial",
            "Coin à quille plate, arête dorsale étagée montant vers une passerelle arrière coiffée de dômes",
            p =>
            {
                p.HullClass = HullClass.Cruiser;
                p.HullArrangement = HullArrangement.Parallel;
                p.HullCount = 1;
                p.HullShape = HullShape.Wedge;
                p.Length = 34f;
                p.Beam = 9f;
                p.Depth = 3.4f;

                // The taper is what makes the planform a dagger rather than a doorstop, and the flat
                // keel is what makes its section a knife. Neither alone reads as Imperial.
                p.Taper = 0.85f;
                p.KeelFlatness = 1f;

                p.Decks = 4;
                p.DorsalSpine = true;
                p.SpineHeight = 1.5f;

                p.WingStyle = WingStyle.None;
                p.EngineCount = 3;
                p.EngineStyle = EngineStyle.Standard;
                p.CockpitStyle = CockpitStyle.None;

                // Right at the stern, where an Imperial bridge sits. This is the setting that did
                // not exist before -- the tower's station used to be seed jitter and nothing else.
                p.Superstructure = true;
                p.SuperstructureSize = 1.3f;
                p.TowerPosition = 0.9f;
                p.TowerDomes = true;

                p.Nacelles = false;
                p.TurretCount = 10;
                p.GreebleDensity = 0.85f;
            }),

        Of(StarWars, "Chasseur X",
            "Fuselage court, quatre ailerons en croix, canons en bout d'aile",
            p =>
            {
                p.HullClass = HullClass.Fighter;
                p.HullArrangement = HullArrangement.Parallel;
                p.HullCount = 1;
                p.HullShape = HullShape.Spindle;
                p.Length = 8f;
                p.Beam = 2f;
                p.Depth = 1.1f;
                p.Taper = 0.7f;
                p.KeelFlatness = 0.2f;
                p.Decks = 1;

                p.WingStyle = WingStyle.Cross;
                p.WingSpan = 6f;
                p.WingSweepDegrees = 8f;
                p.WingtipCannons = true;
                p.CannonSize = 1f;

                p.EngineCount = 4;
                p.EngineStyle = EngineStyle.Standard;
                p.CockpitStyle = CockpitStyle.Bubble;
                p.CockpitSize = 1.4f;
                p.Superstructure = false;
                p.Nacelles = false;
                p.TurretCount = 0;
                p.GreebleDensity = 0.3f;
            }),

        Of(Archetypes, "Chasseur",
            "Coque en dard, ailes en flèche, pods de propulsion suspendus sous les ailes",
            p =>
            {
                p.HullClass = HullClass.Fighter;
                p.HullArrangement = HullArrangement.Parallel;
                p.HullCount = 1;
                p.HullShape = HullShape.Dart;
                p.Length = 9f;
                p.Beam = 2.6f;
                p.Depth = 1.4f;
                p.Taper = 0.65f;
                p.Decks = 1;
                p.WingStyle = WingStyle.Swept;
                p.WingSpan = 7f;
                p.WingSweepDegrees = 38f;
                p.EngineCount = 2;
                p.EngineStyle = EngineStyle.Standard;
                p.CockpitStyle = CockpitStyle.Bubble;
                p.CockpitSize = 1.2f;
                p.Superstructure = false;
                p.Nacelles = true;
                p.NacelleMount = NacelleMount.Widest;
                p.NacelleStyle = NacelleStyle.Thruster;
                p.NacelleWidth = 0.8f;
                p.NacelleLength = 0.8f;
                p.NacelleSpacing = 0.6f;
                p.NacelleRise = -1.1f;
                p.NacelleSweep = 0f;
                p.PylonChord = 1f;
                p.TurretCount = 0;
                p.GreebleDensity = 0.35f;
            }),

        Of(Archetypes, "Cargo lourd",
            "Coque en bloc, ponts empilés, tuyères en anneau, hérissé de superstructures",
            p =>
            {
                p.HullClass = HullClass.Freighter;
                p.HullArrangement = HullArrangement.Parallel;
                p.HullCount = 1;
                p.HullShape = HullShape.Slab;
                // Held back from the top of both ranges on purpose: a freighter is the boxiest
                // class, so the same numbers that make a lean cruiser make three times the volume
                // here, and a preset button that takes a second to answer feels broken.
                p.Length = 26f;
                p.Beam = 7.5f;

                // Boxy, but a freighter's class ratio already deepens it by 40% and the bridge mast
                // scales off the hull's half-height -- asking for 5.5 m here gave a 26 m ship a mast
                // a hundred voxels tall.
                p.Depth = 3.2f;
                p.Taper = 0.15f;
                p.Decks = 6;
                p.WingStyle = WingStyle.None;
                p.EngineCount = 4;
                p.EngineStyle = EngineStyle.Ring;
                p.CockpitStyle = CockpitStyle.FlatCanopy;
                p.CockpitSize = 0.9f;
                p.Superstructure = true;
                p.SuperstructureSize = 1.8f;
                p.Nacelles = false;
                p.TurretCount = 3;
                p.GreebleDensity = 0.9f;
            }),

        Of(Archetypes, "Catamaran",
            "Deux coques en coin reliées par des entretoises, ailerons verticaux",
            p =>
            {
                p.HullClass = HullClass.Corvette;
                p.HullArrangement = HullArrangement.Parallel;
                p.HullCount = 2;
                p.HullSpacing = 1.3f;
                p.HullShape = HullShape.Wedge;
                p.Length = 18f;
                p.Beam = 3.4f;
                p.Depth = 2.0f;
                p.Taper = 0.5f;
                p.Decks = 2;
                p.WingStyle = WingStyle.TwinFin;
                p.WingSpan = 5f;
                p.EngineCount = 2;
                p.EngineStyle = EngineStyle.Standard;
                p.CockpitStyle = CockpitStyle.FlatCanopy;
                p.Superstructure = true;
                p.SuperstructureSize = 0.8f;
                p.Nacelles = false;
                p.TurretCount = 4;
                p.GreebleDensity = 0.6f;
            }),

        Of(Archetypes, "Soucoupe",
            "Un seul disque terrassé, sans ailes ni pods : la soucoupe classique",
            p =>
            {
                p.HullClass = HullClass.Cruiser;
                p.HullArrangement = HullArrangement.Parallel;
                p.HullCount = 1;
                p.HullShape = HullShape.Saucer;
                p.Length = 20f;
                p.Beam = 5f;
                p.Taper = 0.4f;
                p.Depth = 1.6f;
                p.Decks = 5;
                p.WingStyle = WingStyle.None;
                p.EngineCount = 3;
                p.EngineStyle = EngineStyle.Standard;
                p.CockpitStyle = CockpitStyle.None;
                p.Superstructure = true;
                p.SuperstructureSize = 1.6f;
                p.Nacelles = false;
                p.TurretCount = 6;
                p.GreebleDensity = 0.7f;
            }),

        Of(Archetypes, "Anneau",
            "Roue : bande annulaire, six rayons, moyeu central et fuseau axial — une station, sans propulsion",
            p =>
            {
                p.HullClass = HullClass.Cruiser;
                p.HullArrangement = HullArrangement.Parallel;
                p.HullCount = 1;
                p.HullShape = HullShape.Ring;
                p.WheelSpokes = 6;
                p.WheelHub = true;
                p.WheelHubSize = 1.1f;
                p.Length = 20f;
                p.Beam = 5f;
                p.Taper = 0.3f;
                p.Depth = 2.2f;
                p.Decks = 3;
                p.WingStyle = WingStyle.None;
                p.EngineCount = 0;   // une station ne va nulle part
                p.EngineStyle = EngineStyle.Ring;
                p.CockpitStyle = CockpitStyle.None;
                p.Superstructure = true;
                p.SuperstructureSize = 1.2f;
                p.Nacelles = true;
                p.NacelleMount = NacelleMount.Widest;
                p.NacelleStyle = NacelleStyle.Warp;
                p.NacelleWidth = 0.8f;
                p.NacelleLength = 1.1f;
                p.NacelleSpacing = 1.2f;
                p.NacelleRise = 0.4f;
                p.NacelleSweep = 0.2f;
                p.PylonChord = 2f;
                p.TurretCount = 4;
                p.GreebleDensity = 0.6f;
            }),
    };

    /// <summary>
    /// The silhouettes grouped by universe, each group in the order its first member appears in
    /// <see cref="All"/>. Built from <see cref="All"/> rather than declared separately so the two
    /// cannot drift: a silhouette added to the list is in a group by construction, and one whose
    /// universe is misspelled shows up as its own group instead of vanishing.
    /// </summary>
    public static IReadOnlyList<SilhouetteGroup> Groups { get; } = All
        .GroupBy(s => s.Universe)
        .Select(g => new SilhouetteGroup(g.Key, g.ToArray()))
        .ToArray();
}

/// <summary>One franchise's worth of silhouettes, for a sidebar that offers them a family at a
/// time rather than as one undifferentiated column.</summary>
public sealed record SilhouetteGroup(string Universe, IReadOnlyList<ShipSilhouette> Silhouettes);
