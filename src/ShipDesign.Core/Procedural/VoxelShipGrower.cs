namespace ShipDesign.Core.Procedural;

/// <summary>
/// Grows a ship as filled voxels in a VoxelGrid: a random-walk hull envelope (instead of a
/// fixed profile curve -- the whole point is that two ships with the same class and different
/// seeds come out with genuinely different silhouettes, not just scaled copies of one another),
/// then stacked deck terraces, wings, engines, nacelles, superstructure and turrets, and finally
/// a surface-detail pass (panel seams, accent stripes, port lights, raised plates and recesses).
///
/// The detail pass runs last and works off the *actual* voxel surface rather than off the
/// envelope arrays, so decoration follows whatever shape the earlier structural passes produced
/// -- stripes wrap over wing roots, seams follow terrace steps, and windows sit on the real flank.
/// Everything is mirrored across X, so the ship is left-right symmetric by construction.
/// </summary>
public static class VoxelShipGrower
{
    /// <summary>World units per voxel. Sized so a default ship (length 14) is ~93 voxels long:
    /// fine enough for smooth chamfers and curved plating while keeping the culled-mesh triangle
    /// count in a sane range for a game asset.</summary>
    public const float VoxelSize = 0.15f;

    /// <summary>Voxel count that one "detail unit" was originally tuned against. Decorative
    /// feature sizes are expressed in detail units rather than raw voxels, so raising the
    /// resolution makes the *silhouette* finer without shrinking seams, ports and plates into
    /// speckle -- which is what would happen if a panel seam stayed one voxel wide forever.</summary>
    private const float DetailBaselineLength = 54f;

    private static int DetailUnit(int lengthVoxels) =>
        Math.Max(1, (int)MathF.Round(lengthVoxels / DetailBaselineLength));

    /// <summary>Fraction of the half-width that stays at full height before the flanks start
    /// chamfering down -- what makes the cross-section a flat-decked trapezoid rather than a box.</summary>
    private const float DeckFlatFraction = 0.5f;

    /// <summary>Largest wing sweep, forward or aft. Not a taste limit: the shift is a tangent, which
    /// runs to infinity at 90 degrees.</summary>
    public const float MaxWingSweepDegrees = 85f;

    /// <summary>Most deck terraces a hull will step. High enough that the slider is never the
    /// binding constraint -- past this the steps are thinner than a voxel and stop being visible.</summary>
    public const int MaxDecks = 12;

    /// <summary>
    /// Half-height of the hull, from the requested depth and the class's own boxiness.
    ///
    /// Deliberately not a function of the beam. It was, and that made the width slider move the
    /// height with it: there was no way to ask for a wide flat hull, and every ship widened to look
    /// broader came out taller in the same breath.
    /// </summary>
    public static int HalfHeightFor(ShipParameters p, HullClassPreset preset) =>
        Math.Max(2, (int)MathF.Round(
            p.Depth / VoxelSize * 0.5f * preset.HeightRatio / HullClassPreset.ReferenceHeightRatio));

    /// <summary>
    /// Bounding volume in voxels the ship would occupy, without growing it.
    ///
    /// Length and beam multiply -- the grid is a volume -- so neither slider's maximum tells you
    /// what the pair costs. This estimate lands within about 10% of the real voxel count across the
    /// whole range, which is enough for the UI to refuse a combination that would otherwise spend
    /// several seconds and a few gigabytes building a ship nobody asked for.
    /// </summary>
    /// <summary>
    /// The estimate broken into the dimensions it is a product of, so the UI can say *which*
    /// dimension is the problem. "Reduce the length or the beam" is useless advice on a saucer,
    /// whose width is its length -- the beam is not what is making it expensive.
    /// </summary>
    public readonly record struct BoundingBox(
        int Length, int Width, int Height, double HullFactor, bool WidthFollowsLength)
    {
        public long Voxels => (long)(Length * (long)Width * Height * HullFactor);

        /// <summary>
        /// The dimension carrying most of the volume, named as something a person can actually act
        /// on. On a disc that is never the width: a saucer's width is its length, and there is no
        /// slider that narrows it, so the advice has to point at the length even when the width is
        /// the larger number.
        /// </summary>
        public string Dominant =>
            Width >= Length && Width >= Height ? (WidthFollowsLength ? "la longueur" : "la largeur")
            : Height >= Length ? "le creux"
            : "la longueur";
    }

    public static long EstimateBoundingVoxels(ShipParameters p) => BoundingBoxFor(p).Voxels;

    public static BoundingBox BoundingBoxFor(ShipParameters p)
    {
        var preset = HullClassPreset.All[p.HullClass];

        var length = Math.Max(40, (int)MathF.Round(p.Length / VoxelSize));
        var beam = Math.Max(14, (int)MathF.Round(p.Beam / VoxelSize));
        var halfWidth = Math.Max(5, beam / 2);

        // Height comes from the depth, never from a width. A disc is wide but no taller for it -- a
        // saucer is a lens, not a sphere -- and deriving the height from the disc's radius overstated
        // a saucer by a factor of ten, which was enough to have the budget refuse shapes that cost
        // little.
        var halfHeight = HalfHeightFor(p, preset);

        // Across the beam, though, a disc really does take its width from the length.
        var planHalfWidth = HullShapeProfile.IsDisc(p.HullShape)
            ? Math.Max(halfWidth, length / 2)
            : halfWidth;

        // A composite ship stacks its hulls vertically, so it is the *height* of the box that grows
        // rather than its footprint -- and its forward hull only spans part of the length, so a
        // saucer on one is much smaller than a saucer filling the whole ship.
        if (p.HullArrangement == HullArrangement.Composite)
        {
            var fraction = Math.Clamp(p.PrimaryHullFraction, 0.2f, 0.8f);
            var primaryLength = Math.Max(12, (int)MathF.Round(length * fraction));

            var primaryHalfWidth = HullShapeProfile.IsDisc(p.HullShape)
                ? Math.Max(halfWidth, primaryLength / 2)
                : halfWidth;

            var stackedHeight = 4 * halfHeight
                + Math.Max(1, (int)MathF.Round(halfWidth * Math.Clamp(p.SecondaryHullDrop, 0.2f, 4f)));

            return new BoundingBox(length, 2 * Math.Max(primaryHalfWidth, halfWidth), stackedHeight, 1.0,
                HullShapeProfile.IsDisc(p.HullShape));
        }

        // Outriggers are smaller than the primary and sit beside it, so a trimaran is nearer twice
        // the volume than three times it.
        var parallel = Math.Clamp(p.HullCount, 1, 3) switch { 1 => 1.0, 2 => 2.0, _ => 2.3 };

        return new BoundingBox(length, 2 * planHalfWidth, 2 * halfHeight, parallel,
            HullShapeProfile.IsDisc(p.HullShape));
    }

    private sealed class Envelope
    {
        public required int[] HalfWidth { get; init; }
        public required int[] Top { get; init; }
        public required int[] Bottom { get; init; }

        /// <summary>Half-width of the hollow core at each slice; 0 where the slice is solid.
        /// Without this an envelope can only ever describe a solid lens, which rules out a ring
        /// or a split bow no matter what the outline says.</summary>
        public required int[] InnerHalfWidth { get; init; }

        /// <summary>
        /// Height of this hull's own mid-line. Zero for a hull sitting on the ship's axis; negative
        /// for one slung below it, which is how a Starfleet engineering hull hangs under the saucer.
        ///
        /// Kept beside <see cref="Top"/> rather than added into it because the section chamfers by
        /// *scaling* the height: fold the offset into Top and the flanks would taper toward y=0
        /// instead of toward the hull's own centre, and a dropped hull would come out sheared.
        /// </summary>
        public int CentreY { get; init; }

        /// <summary>0..1, carried through from the parameters so <see cref="Section"/> can flatten
        /// the underside. Kept on the envelope rather than passed around because every caller of
        /// Section would otherwise have to know about it.</summary>
        public float KeelFlatness { get; init; }

        /// <summary>Deck and keel heights in ship coordinates. Everything that seats a structure on
        /// a hull wants these rather than <see cref="Top"/>, which is measured from the hull's own
        /// mid-line and means nothing on its own once hulls can sit at different heights.</summary>
        public int DeckY(int z) => CentreY + Top[z];

        public int KeelY(int z) => CentreY - Bottom[z];

        /// <summary>A height part-way up the hull's side, for stripes and port rows.</summary>
        public int DeckFractionY(int z, float fraction) => CentreY + (int)MathF.Round(Top[z] * fraction);

        /// <summary>Raises the deck to an absolute height, for passes that build upward and then
        /// have to tell the surface passes where the new surface is.</summary>
        public void RaiseDeckTo(int z, int deckY)
        {
            var relative = deckY - CentreY;
            if (relative > Top[z]) Top[z] = relative;
        }

        /// <summary>X offset (from the hull centreline) of material that actually exists at this
        /// slice. Structures are seated here rather than at the centreline, which on a hollow hull
        /// is empty space -- a bridge tower placed at x=0 on a ring would hang in the hole.</summary>
        public int SpineOffset(int z)
        {
            var inner = InnerHalfWidth[z];
            return inner == 0 ? 0 : (inner + HalfWidth[z]) / 2;
        }

        /// <summary>First and last slice this hull actually occupies. A hull no longer necessarily
        /// runs the whole length of the ship -- a saucer stops well short of the stern -- so passes
        /// that want "the bow" or "the tail" have to ask the hull, not the ship.</summary>
        public int FirstZ
        {
            get
            {
                for (var z = 0; z < HalfWidth.Length; z++)
                    if (HalfWidth[z] > 0) return z;
                return 0;
            }
        }

        public int LastZ
        {
            get
            {
                for (var z = HalfWidth.Length - 1; z >= 0; z--)
                    if (HalfWidth[z] > 0) return z;
                return HalfWidth.Length - 1;
            }
        }

        /// <summary>
        /// The slice a fraction of the way along *this hull*, which is not the same as a fraction
        /// of the ship once a hull can occupy part of it. A bridge placed 42% along a ship whose
        /// saucer stops at 31% is placed past the end of the saucer, on nothing at all.
        /// </summary>
        public int SliceAt(float fraction)
        {
            int first = FirstZ, last = LastZ;
            return Math.Clamp(first + (int)MathF.Round(Math.Clamp(fraction, 0f, 1f) * (last - first)), first, last);
        }

        /// <summary>
        /// This envelope moved to a new position along the ship and a new height, padded with empty
        /// slices either side. Padding rather than shortening the arrays keeps every pass indexing
        /// by ship-wide z, which is what makes a hull that occupies part of the ship a small change
        /// rather than a rewrite of everything that reads an envelope.
        /// </summary>
        public Envelope PlacedAt(int totalLength, int zOffset, int centreY)
        {
            var halfWidth = new int[totalLength];
            var top = new int[totalLength];
            var bottom = new int[totalLength];
            var inner = new int[totalLength];

            for (var z = 0; z < HalfWidth.Length; z++)
            {
                var target = z + zOffset;
                if (target < 0 || target >= totalLength) continue;
                halfWidth[target] = HalfWidth[z];
                top[target] = Top[z];
                bottom[target] = Bottom[z];
                inner[target] = InnerHalfWidth[z];
            }

            return new Envelope
            {
                HalfWidth = halfWidth,
                Top = top,
                Bottom = bottom,
                InnerHalfWidth = inner,
                CentreY = centreY,
            };
        }
    }

    /// <summary>Per-seed placement jitter for the bolt-on structures. Drawn up front in a fixed
    /// order so the whole ship stays deterministic for a given seed, and applied so that two
    /// seeds differ in *where* things sit, not just in the hull outline.</summary>
    private sealed record Layout(float WingCenter, float TowerCenter, float NacelleCenter, float TurretSpread);

    /// <summary>One parallel hull: where its centreline sits in X, and its own envelope. Each hull
    /// carries a full envelope rather than a scale factor on a shared one, because hulls can have
    /// different planforms -- a scale factor can only ever produce a smaller copy of one shape.</summary>
    private sealed record HullColumn(int XOffset, Envelope Envelope, HullShape Shape);

    /// <summary>
    /// Where the parallel hulls sit. Only offsets at or right of the centreline are listed:
    /// every fill goes through <see cref="VoxelGrid.SetMirrored"/>, so the port side comes for
    /// free and the ship cannot come out asymmetric. That is why a catamaran is a single entry
    /// at +d rather than a pair at ±d -- listing both would build each hull twice.
    ///
    /// The first entry is the primary hull: it carries the deck terraces, bridge tower, canopy and
    /// turrets, and its envelope is the one those passes read.
    /// </summary>
    private static IReadOnlyList<HullColumn> HullLayout(
        Random rng, ShipParameters p, HullClassPreset preset, int len, int maxHalfWidth, int maxHalfHeight)
    {
        if (p.HullArrangement == HullArrangement.Composite)
            return CompositeLayout(rng, p, preset, len, maxHalfWidth, maxHalfHeight);

        var count = Math.Clamp(p.HullCount, 1, 3);
        var primary = GrowEnvelope(rng, p, preset, p.HullShape, len, maxHalfWidth, maxHalfHeight);

        if (count == 1)
            return new[] { new HullColumn(0, primary, p.HullShape) };

        // Space the hulls off their *actual* widths rather than the beam: a saucer is far wider
        // than its beam implies, and offsets computed from the beam would overlap the hulls.
        var primaryHalfWidth = primary.HalfWidth.Max();
        var gap = Math.Max(1, (int)MathF.Round(primaryHalfWidth * 0.8f * Math.Max(0.1f, p.HullSpacing)));

        // A catamaran is two copies of the primary hull, so it needs no second envelope -- the
        // single entry at +d is mirrored into the port hull.
        if (count == 2)
            return new[] { new HullColumn(primaryHalfWidth + gap, primary, p.HullShape) };

        const float outriggerScale = 0.62f;

        // Grown at its own length and centred, rather than always running the full length of the
        // ship. The length slider used to stretch the outrigger with the main hull, so a short
        // sponson beside a long hull was unreachable.
        var outriggerLen = Math.Clamp((int)MathF.Round(len * Math.Clamp(p.SecondaryHullLength, 0.25f, 1f)), 12, len);
        var outriggerStart = (len - outriggerLen) / 2;
        var outrigger = GrowEnvelope(rng, p, preset, p.SecondaryHullShape, outriggerLen, maxHalfWidth, maxHalfHeight, outriggerScale);

        return new[]
        {
            new HullColumn(0, primary, p.HullShape),
            new HullColumn(primaryHalfWidth + gap + outrigger.HalfWidth.Max(),
                outrigger.PlacedAt(len, outriggerStart, 0), p.SecondaryHullShape),
        };
    }

    /// <summary>
    /// The Starfleet arrangement: a forward hull carried high, an aft hull slung below it, and the
    /// two overlapping along the middle of the ship so a neck has somewhere to land.
    ///
    /// Both envelopes are grown at their own length and then placed into ship-wide arrays, which is
    /// what lets a saucer be a saucer -- a disc sizes itself off the length it is given, so growing
    /// one over the whole ship and hoping to use only its front half would produce a disc the size
    /// of the entire vessel.
    /// </summary>
    private static IReadOnlyList<HullColumn> CompositeLayout(
        Random rng, ShipParameters p, HullClassPreset preset, int len, int maxHalfWidth, int maxHalfHeight)
    {
        var fraction = Math.Clamp(p.PrimaryHullFraction, 0.2f, 0.8f);
        var primaryLen = Math.Max(12, (int)MathF.Round(len * fraction));

        // The aft hull starts under the forward one's rear third rather than behind it. Overlap is
        // what makes the pair read as one ship: butt them end to end and the neck becomes a
        // coupling between two vehicles.
        var secondaryStart = (int)MathF.Round(primaryLen * 0.55f);
        var secondaryLen = Math.Max(12, (int)MathF.Round(
            (len - secondaryStart) * Math.Clamp(p.SecondaryHullLength, 0.25f, 1f)));

        var primary = GrowEnvelope(rng, p, preset, p.HullShape, primaryLen, maxHalfWidth, maxHalfHeight);
        var secondary = GrowEnvelope(rng, p, preset, p.SecondaryHullShape, secondaryLen, maxHalfWidth, maxHalfHeight, 0.82f);

        // Measured off the forward hull's own depth so the neck stays proportional whatever the
        // saucer turns out to be, and floored so the two hulls cannot merge into one blob.
        var primaryDepth = Math.Max(1, primary.Bottom.Max());
        var secondaryDepth = Math.Max(1, secondary.Top.Max());
        var drop = -(primaryDepth + secondaryDepth
                     + Math.Max(1, (int)MathF.Round(maxHalfHeight * Math.Clamp(p.SecondaryHullDrop, 0.2f, 4f))));

        return new[]
        {
            new HullColumn(0, primary.PlacedAt(len, 0, 0), p.HullShape),
            new HullColumn(0, secondary.PlacedAt(len, secondaryStart, drop), p.SecondaryHullShape),
        };
    }

    /// <summary>
    /// The dorsal neck: a deep, thin blade joining the forward hull's keel to the aft hull's deck
    /// over the slices where the two overlap.
    ///
    /// Not decoration. Without it a composite ship is two solids that merely look assembled, and it
    /// would export as two disconnected pieces -- the same reason the parallel arrangement needs its
    /// spars. Built from the hulls' *real* sections rather than from their envelopes' mid-lines, so
    /// it meets actual material at both ends instead of stopping in mid-air over a chamfer.
    /// </summary>
    private static void GrowNeck(VoxelGrid grid, ShipParameters p, HullColumn forward, HullColumn aft, int len)
    {
        var detail = DetailUnit(len);

        // Where the two hulls overlap along the ship. The neck sits in the back half of that band,
        // which is where it lands on the forward hull's trailing underside rather than on its belly.
        var from = Math.Max(forward.Envelope.FirstZ, aft.Envelope.FirstZ);
        var to = Math.Min(forward.Envelope.LastZ, aft.Envelope.LastZ);
        if (to <= from) return;

        var span = to - from;
        var z0 = from + (int)MathF.Round(span * 0.42f);
        var z1 = to - (int)MathF.Round(span * 0.12f);
        if (z1 <= z0) { z0 = from; z1 = to; }

        var halfThickness = Math.Max(1, (int)MathF.Round(detail * 1.6f));

        for (var z = z0; z <= z1 && z < len; z++)
        {
            if (forward.Envelope.HalfWidth[z] < 1 || aft.Envelope.HalfWidth[z] < 1) continue;

            // Seated on the aft hull's spine rather than on the centreline. On a hollow engineering
            // hull -- a ring, a fork -- the centreline is the hole, so a neck dropped down x=0 meets
            // nothing at the bottom and the two hulls stay separate solids. Offsetting also turns
            // the single dorsal into a pair of struts once mirrored, which is the right answer for a
            // hull that has two flanks and no middle.
            var spine = aft.Envelope.SpineOffset(z);

            for (var dx = -halfThickness; dx <= halfThickness; dx++)
            {
                var x = spine + dx;
                if (!Section(forward.Envelope, z, x, out _, out var forwardKeel)) continue;
                if (!Section(aft.Envelope, z, x, out var aftDeck, out _)) continue;

                // Overlap the ends by a voxel each so the neck fuses with both hulls rather than
                // merely touching them -- a shared face is not a shared voxel, and the flood fill
                // that checks a ship is one piece walks voxels.
                for (var y = aftDeck - 1; y <= forwardKeel + 1; y++)
                    grid.SetMirrored(x, y, z, VoxelMaterial.Hull);
            }
        }

        _ = p;
    }

    /// <summary>
    /// A large emissive dish set into the bow of the aft hull, ringed by a dark housing.
    ///
    /// Cheap and out of all proportion to its cost in how much it sells the silhouette: it is the
    /// one feature that says "this is the front of the engineering hull" rather than "this is the
    /// blunt end of a second fuselage". Set *into* the bow rather than stuck on it -- a dish
    /// standing proud reads as a satellite antenna.
    /// </summary>
    private static void GrowDeflector(VoxelGrid grid, ShipParameters p, HullColumn aft, int len)
    {
        var env = aft.Envelope;

        // The dish is sized off the hull's fullest section, so it is worth having, but it is *drawn*
        // on the hull's forward-facing surface wherever that turns out to be. Those are two
        // different slices and conflating them is what made an earlier version invisible: it sized
        // the dish at the broad slice and then searched for a surface starting just ahead of that
        // slice, where the hull is already solid, so every voxel of the dish was painted inside the
        // bow with material in front of it.
        var wanted = Math.Max(3, (int)MathF.Round(env.HalfWidth.Max() * 0.8f));
        var broadZ = -1;
        for (var z = env.FirstZ; z <= env.LastZ; z++)
            if (env.HalfWidth[z] >= wanted) { broadZ = z; break; }

        if (broadZ < 0) return;

        var halfWidth = env.HalfWidth[broadZ];
        var halfHeight = Math.Max(1, (env.Top[broadZ] + env.Bottom[broadZ]) / 2);
        var radius = Math.Max(3, (int)MathF.Round(MathF.Min(halfWidth, halfHeight) * 0.8f));
        var centreY = env.CentreY + (env.Top[broadZ] - env.Bottom[broadZ]) / 2;

        var depth = Math.Max(1, DetailUnit(len));
        var rim = Math.Max(1, radius / 4);

        // Build a short cylindrical boss over the bow, then face it with the dish.
        //
        // A deflector is a flat disc facing forward, and a tapering bow has no flat face to put one
        // on. Painting the cone's surface instead wraps the dish over a dozen slices, where it reads
        // as a lit nose rather than as a dish. Boring a socket *out* of the bow gives a face but is
        // not safe: the bore's centre is taken at the broad slice while the hull's own mid-line
        // wanders forward of it, so the cylinder misses part of some sections and leaves slivers
        // behind -- ten of twenty-four composite ships came apart.
        //
        // Filling the cone out to a cylinder does the same job by adding rather than removing, and
        // added material welded onto the hull cannot disconnect anything.
        // Anchored at the hull's frontmost slice, not a little behind it: a boss set back from the
        // tip has the bow's own cone standing in front of it, so its face is not the front of
        // anything and the dish is buried again -- which is exactly what the first two attempts did.
        var bossLength = Math.Max(2, depth);
        var bossZ = env.FirstZ;

        for (var z = bossZ; z <= bossZ + bossLength && z < len; z++)
        {
            // On a hollow bow -- a ring, a fork -- the centreline is the hole, so a boss sized only
            // for the dish sits in mid-air inside it and floats off with the dish on its face. The
            // plug widens to meet the hull's inner rim wherever there is one; on a solid hull the
            // inner rim is zero and this is exactly the dish's own radius.
            var plug = Math.Max(radius, env.InnerHalfWidth[z] + 1);

            for (var dx = -plug; dx <= plug; dx++)
                for (var dy = -plug; dy <= plug; dy++)
                {
                    if (dx * dx + dy * dy > plug * plug) continue;
                    grid.SetMirrored(aft.XOffset + dx, centreY + dy, z, VoxelMaterial.Hull);
                }
        }

        // The boss's own front face, which is flat by construction.
        for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
            {
                var d2 = dx * dx + dy * dy;
                if (d2 > radius * radius) continue;

                var x = aft.XOffset + dx;
                var y = centreY + dy;

                // The outer annulus is the housing, the inner disc is the emitter.
                var housing = d2 > (radius - rim) * (radius - rim);
                for (var dz = 0; dz < depth; dz++)
                {
                    var z = bossZ + dz;
                    if (z >= len || !grid.IsFilled(x, y, z)) break;
                    grid.SetMirrored(x, y, z, housing ? VoxelMaterial.HullDark : VoxelMaterial.Glow);
                }
            }

        _ = p;
    }

    /// <summary>
    /// A sum of a few random low-frequency sine waves. This is what actually carries the
    /// seed-to-seed silhouette difference: per-step walk noise gets averaged away by the
    /// smoothing pass (and by rounding to whole voxels), whereas a long-wavelength bulge or waist
    /// survives both, so it still reads as a distinct ship rather than as surface fuzz.
    /// </summary>
    private sealed class ProfileWave
    {
        private readonly (float Amplitude, float Frequency, float Phase)[] _octaves;

        public ProfileWave(Random rng, float amplitude)
        {
            _octaves = new (float, float, float)[3];
            for (var i = 0; i < _octaves.Length; i++)
            {
                var falloff = 1f / (i + 1);
                _octaves[i] = (amplitude * falloff,
                    0.6f + i * 1.4f + (float)rng.NextDouble() * 1.2f,
                    (float)rng.NextDouble() * MathF.PI * 2f);
            }
        }

        public float At(float u)
        {
            var sum = 0f;
            foreach (var (amplitude, frequency, phase) in _octaves)
                sum += amplitude * MathF.Sin(u * frequency * MathF.PI * 2f + phase);
            return sum;
        }
    }

    public static VoxelGrid Grow(ShipParameters p, HullClassPreset preset, out int lengthVoxels)
    {
        var rng = new Random(p.Seed);
        var grid = new VoxelGrid();

        lengthVoxels = Math.Max(40, (int)MathF.Round(p.Length / VoxelSize));
        var beamVoxels = Math.Max(14, (int)MathF.Round(p.Beam / VoxelSize));
        var maxHalfWidth = Math.Max(5, beamVoxels / 2);
        var maxHalfHeight = HalfHeightFor(p, preset);

        // Drawn before the envelopes so the jitter does not shift when a second envelope is
        // generated for an outrigger -- otherwise adding a hull would also move the wings.
        var layout = new Layout(
            WingCenter: 0.56f + Noise(rng, 0.07f),
            TowerCenter: 0.42f + Noise(rng, 0.10f),
            NacelleCenter: 0.60f + Noise(rng, 0.07f),
            TurretSpread: 0.6f + Noise(rng, 0.15f));

        var hulls = HullLayout(rng, p, preset, lengthVoxels, maxHalfWidth, maxHalfHeight);
        var primary = hulls[0];

        FillHull(grid, hulls, lengthVoxels);

        // Each hull takes the structural idiom its planform calls for: discs get concentric decks
        // and radial ribs, elongated hulls get fore-and-aft terraces. Discs are structured
        // individually rather than only on the primary, since an unribbed outrigger disc next to a
        // ribbed centre one would read as an unfinished part.
        foreach (var hull in hulls)
            if (HullShapeProfile.IsDisc(hull.Shape))
                GrowDiscStructure(grid, p, hull, lengthVoxels);

        if (!HullShapeProfile.IsDisc(primary.Shape))
            AddDeckTerraces(grid, p, primary, lengthVoxels);

        // Whatever joins the hulls must go in before anything else reads the surface: without it
        // the hulls are separate solids, and the ship would export as unconnected pieces.
        if (p.HullArrangement == HullArrangement.Composite)
        {
            GrowNeck(grid, p, primary, hulls[1], lengthVoxels);

            if (p.Deflector)
                GrowDeflector(grid, p, hulls[1], lengthVoxels);
        }
        // The test is "is any hull off the centreline", not "is there more than one entry" -- a
        // catamaran is a single entry at +d whose mirror is the second hull, so counting entries
        // would skip the one layout that most needs joining.
        else if (hulls.Any(h => h.XOffset != 0))
        {
            GrowHullBridges(grid, hulls, lengthVoxels);
        }

        // Before the wings and pods, so those spring from the hull rather than from the ridge, and
        // after the terraces, so the ridge sits on the stepped deck rather than through it.
        if (p.DorsalSpine)
            GrowDorsalSpine(grid, p, primary, lengthVoxels, maxHalfHeight);

        if (p.WingStyle != WingStyle.None)
            GrowWings(grid, p, hulls, layout, lengthVoxels, maxHalfHeight);

        if (p.Nacelles)
            GrowNacelles(grid, p, hulls, layout, lengthVoxels, maxHalfHeight);

        if (p.Superstructure && p.HullClass != HullClass.Fighter)
            GrowSuperstructure(grid, p, primary, layout, lengthVoxels, maxHalfHeight);

        GrowEngines(grid, p, hulls, lengthVoxels, maxHalfHeight);

        if (p.CockpitStyle != CockpitStyle.None)
            CarveCockpit(grid, p, preset, primary, lengthVoxels);

        DetailPass(grid, rng, p, hulls, lengthVoxels, maxHalfHeight);

        // Turrets go on after the surface detail, not before. The carving pass decides what is
        // hull deck by comparing height against Top[z] -- a per-slice maximum -- and on a disc the
        // deck height varies a lot across one slice, so an outboard turret sits below the crown of
        // its own slice and gets carved away as if it were deck. Mounting them last sidesteps the
        // proxy entirely instead of trying to make it smarter.
        if (p.TurretCount > 0)
            GrowTurrets(grid, p, primary, layout, lengthVoxels);

        DropDetachedFragments(grid);

        return grid;
    }

    /// <summary>
    /// Removes anything not connected to the ship's main body, and reports how much it removed.
    ///
    /// A dozen passes each seat their own structure, and "this piece touches the hull" has turned out
    /// to be wrong in a new way at four of them -- a fin raised from the envelope's deck instead of the
    /// real surface, a tower on a hollow hull's empty centreline, a globe tangent to a flat top,
    /// a rim sliver isolated by a panel recess. Each was worth fixing at source and each was. But an
    /// exported game asset being a single solid is an invariant, not a hope, and enforcing it once
    /// here is far more reliable than getting every future pass right by inspection.
    ///
    /// It is a safety net and not a licence: what it removes is asserted to be tiny. A large
    /// fragment means a pass is genuinely broken and should be found and fixed, not quietly swept up.
    /// </summary>
    /// <summary>
    /// Coordinates packed into one long, biased so negatives pack too. Measured, not assumed: a first
    /// version walked a <c>HashSet</c> of (int,int,int) tuples and spent over a second on a
    /// million-voxel ship -- half the build -- because six tuple hashes per voxel is the whole cost of
    /// the pass. One integer key per voxel makes the same walk a fraction of that.
    /// </summary>
    private const int PackBias = 1 << 20;

    private static long Pack(int x, int y, int z) =>
        ((long)(x + PackBias) << 42) | ((long)(y + PackBias) << 21) | (uint)(z + PackBias);

    public static int DropDetachedFragments(VoxelGrid grid)
    {
        if (grid.Voxels.Count == 0) return 0;

        var remaining = new HashSet<long>(grid.Voxels.Count);
        foreach (var (x, y, z) in grid.Voxels.Keys) remaining.Add(Pack(x, y, z));

        // Components are collected as (start, size) and only the sizes compared; the voxels
        // themselves are not kept, which keeps this to one allocation per component rather than one
        // list per component holding every voxel in it.
        var components = new List<(int X, int Y, int Z, int Size)>();
        var stack = new Stack<(int X, int Y, int Z)>();

        foreach (var origin in grid.Voxels.Keys)
        {
            if (!remaining.Remove(Pack(origin.X, origin.Y, origin.Z))) continue;

            var size = 0;
            stack.Clear();
            stack.Push(origin);

            while (stack.Count > 0)
            {
                var (x, y, z) = stack.Pop();
                size++;

                for (var i = 0; i < Neighbours.Length; i++)
                {
                    var (dx, dy, dz) = Neighbours[i];
                    if (remaining.Remove(Pack(x + dx, y + dy, z + dz))) stack.Push((x + dx, y + dy, z + dz));
                }
            }

            components.Add((origin.X, origin.Y, origin.Z, size));
        }

        if (components.Count <= 1) return 0;

        // Re-walk the main body to know what to keep, then drop the rest. Two walks over the largest
        // component is still far cheaper than having held every component's voxels in memory.
        var biggest = components[0];
        foreach (var c in components)
            if (c.Size > biggest.Size) biggest = c;

        var keep = new HashSet<long>(biggest.Size);
        stack.Clear();
        stack.Push((biggest.X, biggest.Y, biggest.Z));
        keep.Add(Pack(biggest.X, biggest.Y, biggest.Z));

        while (stack.Count > 0)
        {
            var (x, y, z) = stack.Pop();
            for (var i = 0; i < Neighbours.Length; i++)
            {
                var (dx, dy, dz) = Neighbours[i];
                var nx = x + dx;
                var ny = y + dy;
                var nz = z + dz;
                if (grid.IsFilled(nx, ny, nz) && keep.Add(Pack(nx, ny, nz))) stack.Push((nx, ny, nz));
            }
        }

        var doomed = grid.Voxels.Keys.Where(k => !keep.Contains(Pack(k.X, k.Y, k.Z))).ToList();
        foreach (var (x, y, z) in doomed) grid.Remove(x, y, z);
        return doomed.Count;
    }

    private static readonly (int X, int Y, int Z)[] Neighbours =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    };

    // ---- Hull envelope ------------------------------------------------------------------

    private static Envelope GrowEnvelope(
        Random rng, ShipParameters p, HullClassPreset preset, HullShape shape, int len, int nominalHW, int nominalHH, float scale = 1f)
    {
        // Disc planforms size themselves off the length instead of the beam, so ask the shape
        // what its working dimensions are rather than assuming the beam-derived ones. The
        // outrigger scale is applied *after* that, so a saucer float is a small saucer rather
        // than a full-size one that happens to sit outboard.
        var maxHW = Math.Max(2, (int)MathF.Round(HullShapeProfile.EffectiveHalfWidth(shape, nominalHW, len) * scale));
        var maxHH = Math.Max(1, (int)MathF.Round(HullShapeProfile.EffectiveHalfHeight(shape, nominalHH) * scale));
        var keelFlatness = Math.Clamp(p.KeelFlatness, 0f, 1f);

        var w = new float[len];
        var top = new float[len];
        var bottom = new float[len];

        // Jaggedness is a *relative* roughness: scaling it by the hull size keeps a "rough"
        // class equally rough at any resolution, instead of getting smoother as voxels shrink.
        //
        // Each axis is roughened in proportion to its own extent. Using the half-width for all three
        // left the deck and keel roughness scaled by the beam, which is a second, quieter way for the
        // width slider to move the height: a wide hull's top wandered a voxel higher than a narrow
        // one's at the same depth.
        var noiseScale = preset.Jaggedness * maxHW * 0.13f;
        var heightNoiseScale = preset.Jaggedness * maxHH * 0.13f;

        // Long-wavelength bulges/waists, one independent set per axis. Unlike the walk noise
        // these survive smoothing and rounding, so they are what makes two seeds read as
        // different hull designs rather than as the same hull with different surface fuzz.
        //
        // Expressed as a *relative* modulation and deliberately modest: as an absolute offset at
        // the previous amplitude, a wave crest could outweigh the planform itself and move the
        // widest point of the ship, which made every shape peak in the same place.
        var widthWave = new ProfileWave(rng, 0.11f * preset.Jaggedness);
        var topWave = new ProfileWave(rng, 0.14f * preset.Jaggedness);
        var bottomWave = new ProfileWave(rng, 0.1f * preset.Jaggedness);

        float cw = 0f, ct = 0f, cb = 0f;

        for (var z = 0; z < len; z++)
        {
            var u = z / (float)(len - 1);

            // The planform drives width and height on separate curves, so a wedge can fan out
            // sideways while staying flat and a spindle can swell in both axes at once.
            var widthProfile = HullShapeProfile.WidthAt(shape, u, p.Taper);
            var heightProfile = HullShapeProfile.HeightAt(shape, u, p.Taper);

            // A dorsal rise over the mid-body, and a flatter keel underneath: a hull that is
            // taller on top than below reads as "has decks" rather than as a symmetric tube.
            // Flattening the keel moves that depth upward rather than removing it, so a wedge keeps
            // its bulk and simply carries it all above the waterline.
            var dorsal = (0.72f + 0.5f * MathF.Sin(MathF.PI * Math.Clamp(u, 0f, 1f))) * (1f + keelFlatness * 0.45f);

            // Multiplying rather than adding keeps the waves subordinate to the planform: they
            // swell and pinch the hull the shape already describes instead of redefining it, and
            // they still fade out where the shape closes, so a tapering bow cannot sprout lumps.
            var targetW = widthProfile * maxHW * (1f + widthWave.At(u));
            var targetTop = heightProfile * maxHH * dorsal * (1f + topWave.At(u));
            var targetBottom = heightProfile * maxHH * (0.62f - keelFlatness * 0.38f) * (1f + bottomWave.At(u));

            // Upper clamps leave headroom above the nominal size so a wave crest can actually
            // bulge the hull instead of being flattened against the limit.
            cw = Math.Clamp(cw + (targetW - cw) * 0.5f + Noise(rng, noiseScale), 0f, maxHW * 1.45f);
            ct = Math.Clamp(ct + (targetTop - ct) * 0.5f + Noise(rng, heightNoiseScale * 0.6f), 0f, maxHH * 1.6f);
            cb = Math.Clamp(cb + (targetBottom - cb) * 0.5f + Noise(rng, heightNoiseScale * 0.45f), 0f, maxHH * 1.3f);

            w[z] = cw;
            top[z] = ct;
            bottom[z] = cb;
        }

        // Smooth twice: the raw walk produces single-voxel spikes that read as damage rather
        // than as design. Smoothing keeps the seed-to-seed silhouette differences but turns them
        // into clean swells and steps.
        Smooth(w);
        Smooth(w);
        Smooth(top);
        Smooth(top);
        Smooth(bottom);
        Smooth(bottom);

        var halfWidth = Quantize(w);
        CloseInteriorPinches(halfWidth);

        // The hollow core is derived from the *final* outer width, after smoothing and rounding,
        // so the two can never cross. Leaving at least MinRimVoxels of material also closes the
        // hole automatically wherever the outline pinches in -- which is what makes a ring a
        // continuous band rather than two loose arcs.
        const int minRimVoxels = 2;
        var inner = new int[len];
        for (var z = 0; z < len; z++)
        {
            var fraction = HullShapeProfile.InnerFractionAt(shape, z / (float)(len - 1));
            if (fraction <= 0f) continue;

            var candidate = (int)MathF.Round(halfWidth[z] * fraction);
            inner[z] = Math.Max(0, Math.Min(candidate, halfWidth[z] - minRimVoxels));
        }

        CloseUnreachableHollows(halfWidth, inner);

        return new Envelope
        {
            HalfWidth = halfWidth,
            Top = Quantize(top),
            Bottom = Quantize(bottom),
            InnerHalfWidth = inner,
            KeelFlatness = keelFlatness,
        };
    }

    /// <summary>
    /// Keeps a hull in one piece by refusing to let its half-width reach zero anywhere between its
    /// bow and its stern.
    ///
    /// The profile waves multiply the planform, and where a shape is already narrow a trough can
    /// take the width to zero for a few slices. That does not read as a waist -- it severs the hull,
    /// and the tip beyond the pinch becomes a separate solid that exports as a loose lump floating
    /// ahead of the ship. Only interior zeros are filled: the runs of zero outside the hull are what
    /// give a hull that occupies part of the ship its extent.
    /// </summary>
    private static void CloseInteriorPinches(int[] halfWidth)
    {
        var first = -1;
        var last = -1;
        for (var z = 0; z < halfWidth.Length; z++)
        {
            if (halfWidth[z] <= 0) continue;
            if (first < 0) first = z;
            last = z;
        }

        if (first < 0) return;

        for (var z = first; z <= last; z++)
            if (halfWidth[z] < 1) halfWidth[z] = 1;
    }

    /// <summary>
    /// Narrows a hollow wherever it would leave a slice unable to reach its neighbours.
    ///
    /// A hollow slice is two arcs rather than one solid section, spanning the radii between the
    /// inner and outer edges. Two adjacent slices are only joined if those spans overlap, so a slice
    /// whose hollow is wider than the neighbouring slice's whole half-width touches nothing there.
    /// A fork's hollow is widest at the bow, which is also where the hull is thinnest, and its
    /// prongs came off as loose crescents floating ahead of the ship.
    ///
    /// Clamping each slice's inner radius to the narrower of its two neighbours' half-widths is the
    /// exact condition for the spans to overlap. Simply filling the hollow at the hull's end slices
    /// -- a first attempt at this -- fixes one direction and breaks the other: the solid plug it
    /// leaves at slice zero then has no neighbour in the hollow slice behind it.
    /// </summary>
    private static void CloseUnreachableHollows(int[] halfWidth, int[] inner)
    {
        var first = -1;
        var last = -1;
        for (var z = 0; z < halfWidth.Length; z++)
        {
            if (halfWidth[z] <= 0) continue;
            if (first < 0) first = z;
            last = z;
        }

        if (first < 0) return;

        for (var z = first; z <= last; z++)
        {
            if (inner[z] <= 0) continue;

            // A hull end has only one neighbour, so only that one constrains it.
            var reach = int.MaxValue;
            if (z > first) reach = Math.Min(reach, halfWidth[z - 1]);
            if (z < last) reach = Math.Min(reach, halfWidth[z + 1]);

            if (inner[z] > reach) inner[z] = Math.Max(0, reach);
        }
    }

    private static float Noise(Random rng, float amplitude) => ((float)rng.NextDouble() - 0.5f) * 2f * amplitude;

    private static void Smooth(float[] values)
    {
        var copy = (float[])values.Clone();
        for (var i = 1; i < values.Length - 1; i++)
            values[i] = (copy[i - 1] + copy[i] * 2f + copy[i + 1]) / 4f;
    }

    private static int[] Quantize(float[] values)
    {
        var result = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
            result[i] = (int)MathF.Round(values[i]);
        return result;
    }

    /// <summary>Fills each z-slice as a flat-decked trapezoid: full height across the middle,
    /// chamfering down toward the flanks. This is the single biggest difference from a plain
    /// box extrusion -- it gives every ship a hard-SF chamfered hull profile.</summary>
    private static void FillHull(VoxelGrid grid, IReadOnlyList<HullColumn> hulls, int len)
    {
        foreach (var hull in hulls)
        {
            var env = hull.Envelope;
            for (var z = 0; z < len; z++)
            {
                var hw = env.HalfWidth[z];
                if (hw < 0) continue;

                // Sweep the full width of this hull rather than 0..hw: an offset hull is not
                // centred on x=0, so its port flank is a distinct set of columns. The mirror
                // then reproduces the whole hull on the other side of the ship.
                var inner = env.InnerHalfWidth[z];

                for (var dx = -hw; dx <= hw; dx++)
                {
                    if (!Section(env, z, dx, out var yTop, out var yBottom)) continue;

                    for (var y = yBottom; y <= yTop; y++)
                        grid.SetMirrored(hull.XOffset + dx, y, z, VoxelMaterial.Hull);
                }
            }
        }
    }

    /// <summary>
    /// Bare-hull section at one column: the deck and keel heights <see cref="FillHull"/> lays down,
    /// or false where the column is inside a hollow core. Shared with the disc pass so the two
    /// cannot drift, and so that pass can compute the surface directly instead of scanning the
    /// grid for it -- the largest hulls have thousands of columns, and the scan showed up in the
    /// build time as soon as discs made those hulls tall.
    /// </summary>
    /// <summary>
    /// Bare-hull section at one column, in ship coordinates: <paramref name="yTop"/> and
    /// <paramref name="yBottom"/> are the actual deck and keel heights, not magnitudes either side
    /// of zero. Absolute because a hull can sit off the ship's axis, and a chamfer measured from
    /// zero on a hull whose centre is elsewhere is not a chamfer.
    /// </summary>
    private static bool Section(Envelope env, int z, int dx, out int yTop, out int yBottom)
    {
        yTop = 0;
        yBottom = 0;

        var hw = env.HalfWidth[z];
        var inner = env.InnerHalfWidth[z];
        var offset = Math.Abs(dx);
        if (hw < 1 || offset > hw) return false;
        if (inner > 0 && offset < inner) return false;

        // On a hollow slice the section tapers toward *both* rims, not just the outer one, so the
        // ring reads as a band with a rounded inner edge rather than a plate with a hole in it.
        var fromOuter = Shoulder(offset, hw);
        var shoulder = inner > 0
            ? MathF.Max(fromOuter, Shoulder(hw - offset + inner, hw))
            : fromOuter;

        yTop = env.CentreY + (int)MathF.Round(env.Top[z] * (1f - shoulder * 0.62f));

        // A flat keel is the lower chamfer switched off: at flatness 1 the underside is one plane
        // from flank to flank, which is what makes a wedge read as a knife rather than as a lens.
        var keelChamfer = 0.55f * (1f - Math.Clamp(env.KeelFlatness, 0f, 1f));
        yBottom = env.CentreY - (int)MathF.Round(env.Bottom[z] * (1f - shoulder * keelChamfer));
        return true;
    }

    private static int HalfWidthOf(HullColumn hull, int z) => hull.Envelope.HalfWidth[z];

    /// <summary>Outermost filled X the hulls reach at this slice -- where wings and nacelle pylons
    /// have to start so they spring from the outboard hull rather than from inside it.</summary>
    private static int OuterEdge(IReadOnlyList<HullColumn> hulls, int z)
    {
        var edge = 0;
        foreach (var hull in hulls)
            edge = Math.Max(edge, hull.XOffset + hull.Envelope.HalfWidth[z]);
        return edge;
    }

    /// <summary>Lateral spars tying the hulls together. Without them a multi-hull ship is several
    /// separate solids that merely look joined, and exports as disconnected pieces.</summary>
    private static void GrowHullBridges(VoxelGrid grid, IReadOnlyList<HullColumn> hulls, int len)
    {
        var detail = DetailUnit(len);
        var outer = hulls[^1];
        var outerOffset = outer.XOffset;
        var spars = 3;

        // Spaced across the *outboard* hull's extent rather than the ship's. Once an outrigger can be
        // shorter than the ship, a spar at a fixed fraction of the ship reaches out to where that
        // hull is not, and the beam ends in mid-air with nothing on the far end of it.
        var first = outer.Envelope.FirstZ;
        var last = outer.Envelope.LastZ;

        for (var i = 0; i < spars; i++)
        {
            var z = first + (int)MathF.Round((0.18f + i * 0.28f) * (last - first));
            if (z < 0 || z >= len) continue;

            var halfDepth = Math.Max(1, detail * 2);
            var halfHeight = Math.Max(1, (int)MathF.Round(hulls[0].Envelope.Top[z] * 0.3f));

            for (var dz = -halfDepth; dz <= halfDepth; dz++)
            {
                var zz = z + dz;
                if (zz < 0 || zz >= len) continue;

                // Only where both ends actually have hull to land on.
                if (hulls[0].Envelope.HalfWidth[zz] < 1 || outer.Envelope.HalfWidth[zz] < 1) continue;

                // Span from the centreline out to the outboard hull. For a catamaran that beam
                // crosses x=0 and joins the two hulls; for a trimaran it ties each outrigger to
                // the centre hull. Either way the mirror completes the other side.
                for (var x = 0; x <= outerOffset; x++)
                    for (var y = -halfHeight; y <= halfHeight; y++)
                        grid.SetMirrored(x, y, zz, VoxelMaterial.HullDark);
            }
        }
    }

    /// <summary>0 while inside the flat deck band, ramping to 1 at the outer flank.</summary>
    private static float Shoulder(int x, int hw)
    {
        if (hw <= 0) return 0f;
        var t = x / (float)hw;
        return Math.Clamp((t - DeckFlatFraction) / (1f - DeckFlatFraction), 0f, 1f);
    }

    /// <summary>
    /// Structure for a disc hull: concentric decks stepping up toward the centre, radial ribs
    /// spoking out to the rim, and a stepped bridge dome on the crown. This is the polar
    /// equivalent of <see cref="AddDeckTerraces"/> -- decks running bow to stern say nothing about
    /// a round hull, and a bare lens reads as a flying plate rather than as a vessel.
    ///
    /// Everything is built up from the column's real surface, so the structure lands on the deck
    /// wherever the deck actually is, and a hollow ring simply has no columns to build on.
    /// </summary>
    private static void GrowDiscStructure(VoxelGrid grid, ShipParameters p, HullColumn hull, int len)
    {
        var env = hull.Envelope;
        var detail = DetailUnit(len);
        var radius = env.HalfWidth.Max();
        if (radius < 8) return;

        // The disc's own middle, not the ship's. A saucer that occupies only the forward half of a
        // composite hull would otherwise have its terraces centred somewhere out behind it.
        var centreZ = (env.FirstZ + env.LastZ) / 2;
        var crown = Math.Max(1, env.Top.Max());

        // Concentric decks. One more tier than an elongated hull gets, because on a disc the
        // terracing is the silhouette rather than a detail on top of it.
        // Deliberately shallow steps. A saucer's crown is only modestly thicker than its rim; step
        // heights big enough to be individually dramatic stack into a cone, and each raised column
        // is filled solid, so the amplitude drives voxel count and build time as much as looks.
        var tiers = Math.Clamp(p.Decks + 2, 3, MaxDecks + 2);
        var tierStep = Math.Max(1, (int)MathF.Round(crown * 0.14f));

        const int ribCount = 12;
        const float ribFraction = 0.34f;
        var ribHeight = Math.Max(1, detail);

        var domeHeight = Math.Max(2, (int)MathF.Round(crown * 0.5f));

        // Collected separately and applied at the end. Section() derives the bare-hull surface
        // from Top[], so raising Top[] inside the loop would feed each column a surface that the
        // previous column had already lifted, and the deck would climb away runaway-style.
        // Absolute heights, matching what Section now returns.
        var raisedTop = new int[len];
        for (var z = 0; z < len; z++) raisedTop[z] = int.MinValue;

        for (var z = 0; z < len; z++)
        {
            var hw = env.HalfWidth[z];
            if (hw < 1) continue;

            for (var dx = -hw; dx <= hw; dx++)
            {
                // This pass runs straight after FillHull, so the surface is exactly the bare
                // section -- computing it beats scanning the grid for it on a hull this wide.
                if (!Section(env, z, dx, out var surface, out _)) continue;
                var x = hull.XOffset + dx;

                var dz = z - centreZ;
                var dist = MathF.Sqrt(dx * dx + (float)dz * dz);
                var r = dist / radius;
                if (r > 1f) continue;

                // Concentric terraces: quantising the radius is what turns a smooth dome into
                // stacked annular decks with visible risers between them.
                var tier = Math.Clamp((int)((1f - r) * tiers), 0, tiers - 1);
                var raise = tier * tierStep;

                // Radial ribs. Measured from the disc centre so they converge properly rather
                // than running parallel like the fore-and-aft seams of an elongated hull.
                var angle = MathF.Atan2(dx, dz) + MathF.PI;
                var spoke = angle / (2f * MathF.PI) * ribCount;
                var onRib = spoke - MathF.Floor(spoke) < ribFraction && r > 0.25f;
                if (onRib) raise += ribHeight;

                // Bridge dome on the crown, itself stepped rather than smooth.
                if (r < 0.22f)
                {
                    var domeTier = (int)((1f - r / 0.22f) * 3f);
                    raise += domeTier * Math.Max(1, domeHeight / 3);
                }

                if (raise <= 0) continue;

                var material = onRib ? VoxelMaterial.Panel : VoxelMaterial.Hull;
                for (var dy = 1; dy <= raise; dy++)
                    grid.SetMirrored(x, surface + dy, z, material);

                var newTop = surface + raise;
                if (newTop > raisedTop[z]) raisedTop[z] = newTop;
            }
        }

        // Top[] has to follow the new deck, or the surface-detail passes -- which treat anything
        // above Top[] as a mounted structure -- would refuse to decorate the disc at all.
        for (var z = 0; z < len; z++)
            if (raisedTop[z] != int.MinValue) env.RaiseDeckTo(z, raisedTop[z]);
    }

    private static void AddDeckTerraces(VoxelGrid grid, ShipParameters p, HullColumn primary, int len)
    {
        var env = primary.Envelope;
        var decks = Math.Clamp(p.Decks, 1, MaxDecks);
        var maxTop = env.Top.Max();

        for (var deck = 1; deck <= decks; deck++)
        {
            // Each terrace's inset is a fraction of the *stack*, not a fixed step. With a fixed
            // step the fifth terrace came out zero-width and every deck past it was silently
            // nothing, which made the slider stop having an effect halfway along its travel.
            var t = deck / (float)(decks + 1);

            var widthFraction = 1f - t;
            var z0 = (int)MathF.Round(len * (0.24f + t * 0.33f));
            var z1 = (int)MathF.Round(len * (0.82f - t * 0.30f));
            if (z1 <= z0) break;

            var height = Math.Max(1, (int)MathF.Round(maxTop * 0.16f));

            for (var z = z0; z <= z1 && z < len; z++)
            {
                var hw = (int)MathF.Round(HalfWidthOf(primary, z) * widthFraction);
                if (hw < 1) continue;

                // Terraces are built on the primary hull only, which is also the hull Top[]
                // tracks. Stepping outriggers as well would desynchronise the shared envelope
                // from every hull it is supposed to describe.
                for (var dx = -hw; dx <= hw; dx++)
                {
                    var x = primary.XOffset + dx;
                    var shoulder = Shoulder(Math.Abs(dx), hw);
                    var slabTop = Math.Max(1, height - (int)MathF.Round(shoulder * height * 0.5f));

                    // Fill from the column's real surface up to the target height rather than from
                    // Top[z] up: out toward the terrace's edge the chamfered deck sits well below
                    // the centreline top, so starting at Top[z] leaves the slab hanging in mid-air.
                    var surface = TopFilledY(grid, x, z);
                    if (surface is null) continue;

                    for (var y = surface.Value + 1; y <= env.DeckY(z) + slabTop; y++)
                        grid.SetMirrored(x, y, z, VoxelMaterial.Hull);
                }

                env.Top[z] += height;
            }
        }
    }

    // ---- Structures ---------------------------------------------------------------------

    /// <summary>Wings with a real airfoil-ish section: thickness falls off toward the tip *and*
    /// toward the leading/trailing edges, so the profile is a chamfered blade rather than a slab.</summary>
    private static void GrowWings(VoxelGrid grid, ShipParameters p, IReadOnlyList<HullColumn> hulls, Layout layout, int len, int maxHH)
    {
        var env = hulls[0].Envelope;
        var span = Math.Max(2, (int)MathF.Round(p.WingSpan / VoxelSize));

        // Negative sweep is forward-swept, which is a real planform and not a mistake. Still bounded
        // short of 90 degrees on both sides: the tangent runs away there, and a wing whose tip chord
        // sits a whole hull length off the root reads as a detached spar rather than as a wing.
        var sweep = MathF.Tan(Math.Clamp(p.WingSweepDegrees, -MaxWingSweepDegrees, MaxWingSweepDegrees) * MathF.PI / 180f);

        var (rootOffset, chordFraction, thicknessBase) = p.WingStyle switch
        {
            WingStyle.Delta => (0.06f, 0.34f, maxHH * 0.5f),
            WingStyle.TwinFin => (0.16f, 0.20f, maxHH * 0.42f),
            WingStyle.Cross => (0.10f, 0.18f, maxHH * 0.30f),
            _ => (0f, 0.26f, maxHH * 0.45f),
        };

        // Along the hull the wings spring from, not along the ship. The last of the passes to still
        // be measuring in ship coordinates: on a composite hull whose saucer stops at a third of the
        // length, a wing root nominally at 56% started off the end of it.
        var rootCenter = Math.Clamp(layout.WingCenter + rootOffset, 0.3f, 0.8f);
        var centerZ = env.SliceAt(rootCenter);
        var rootChord = Math.Max(2, (int)MathF.Round(len * chordFraction * 0.5f));
        var thick0 = Math.Max(1, (int)MathF.Round(thicknessBase));

        // Spring the wing from the outboard hull's flank, not from the centreline hull's, or on a
        // multi-hull ship the wing root would start buried inside the outrigger.
        var rootHalfWidth = OuterEdge(hulls, Math.Clamp(centerZ, 0, len - 1));

        for (var offset = 0; offset <= span; offset++)
        {
            var t = offset / (float)span;
            var x = rootHalfWidth + offset;

            // Sweep shifts the chord aft with distance from the hull; the chord also tapers,
            // so the planform is a swept trapezoid rather than a rectangle.
            var shift = (int)MathF.Round(sweep * offset);
            var chord = Math.Max(1, (int)MathF.Round(rootChord * (1f - t * 0.62f)));
            var zc = centerZ + shift;

            for (var dz = -chord; dz <= chord; dz++)
            {
                var z = zc + dz;
                if (z < 0 || z >= len) continue;

                var edge = MathF.Abs(dz) / chord;
                var thickness = Math.Max(0, (int)MathF.Round(thick0 * (1f - t * 0.6f) * (1f - edge * edge * 0.75f)));

                if (p.WingStyle == WingStyle.TwinFin)
                {
                    // Fins stand vertically off the hull instead of spreading horizontally.
                    var finX = Math.Max(1, rootHalfWidth - 1) + offset / 3;

                    // Raised from the column's *real* surface, not from the envelope's centreline
                    // deck. Out at the flank the chamfered deck sits well below Top[z], so starting
                    // there left the whole fin hanging in mid-air -- which is what made a twin-fin
                    // ship export as three separate pieces. Where there is no hull under the column
                    // at all there is nothing to stand on, and the fin simply stops there.
                    var surface = TopFilledY(grid, finX, z);
                    if (surface is null) continue;

                    for (var dy = 1; dy <= thickness * 2 + 2; dy++)
                        grid.SetMirrored(finX, surface.Value + dy, z, VoxelMaterial.Hull);
                    continue;
                }

                if (p.WingStyle == WingStyle.Cross)
                {
                    // Four arms rather than two, splayed above and below the axis. Only the two
                    // starboard arms are written; the mirror supplies the port pair. The rise is
                    // less than the run so the X is wider than it is tall, which is how a
                    // snubfighter reads -- an even 45 degrees looks like a caltrop.
                    var rise = (int)MathF.Round(offset * 0.55f);
                    var armThickness = Math.Max(1, thickness / 2);

                    foreach (var sign in new[] { 1, -1 })
                        for (var y = -armThickness; y <= armThickness; y++)
                            grid.SetMirrored(x, env.CentreY + sign * rise + y, z, VoxelMaterial.Hull);
                    continue;
                }

                for (var y = -thickness; y <= thickness; y++)
                    grid.SetMirrored(x, env.CentreY + y, z, VoxelMaterial.Hull);
            }
        }
    }

    private static void GrowNacelles(VoxelGrid grid, ShipParameters p, IReadOnlyList<HullColumn> hulls, Layout layout, int len, int maxHH)
    {
        var detail = DetailUnit(len);
        var radius = Math.Max(2, (int)MathF.Round(maxHH * 0.75f * p.NacelleWidth));
        var halfLength = Math.Max(4, (int)MathF.Round(len * 0.2f * p.NacelleLength));

        // Which hull the pylons spring from. On a composite ship this is the whole difference
        // between a Starfleet layout and a wrong one: the pods belong on the engineering hull, and
        // "widest" would put them on the saucer's rim, where they read as ordinary wing pods.
        var mount = p.NacelleMount switch
        {
            NacelleMount.Primary => hulls[0],
            NacelleMount.Secondary => hulls[^1],
            _ => null,
        };

        var reference = mount ?? hulls[0];
        var env = reference.Envelope;

        // Placed along the mounting hull's own extent rather than the ship's, or a pod nominally at
        // 60% of the ship would sit off the end of a hull that stops at 55%.
        var first = env.FirstZ;
        var last = env.LastZ;
        var centerZ = Math.Clamp(
            first + (int)MathF.Round(Math.Clamp(layout.NacelleCenter, 0.35f, 0.8f) * (last - first)), 0, len - 1);

        var rootX = mount is null
            ? OuterEdge(hulls, centerZ)
            : mount.XOffset + Math.Max(1, mount.Envelope.HalfWidth[centerZ]);
        var rootY = env.CentreY;

        // Clearance between hull flank and pod, scaled off the hull's own height so the gap stays
        // proportionally the same at any voxel resolution rather than shrinking as voxels get finer.
        var gap = Math.Max(1, (int)MathF.Round(maxHH * 0.6f * p.NacelleSpacing));
        var nacelleX = rootX + radius + gap;

        // Where the pod sits relative to its root. Rise lifts it above the hull instead of slinging
        // it underneath, sweep pushes it aft: together they give the raised, swept-back mounting
        // that reads as a warp nacelle rather than an engine pod bolted under a wing.
        var nacelleY = rootY + (int)MathF.Round((radius + 1) * p.NacelleRise);
        var nacelleZ = Math.Clamp(centerZ + (int)MathF.Round(len * 0.5f * p.NacelleSweep), halfLength, len - 1);

        var pylonHalfThickness = Math.Max(1, detail);
        var pylonHalfChord = Math.Max(pylonHalfThickness,
            (int)MathF.Round(pylonHalfThickness * Math.Clamp(p.PylonChord, 0.2f, 6f)));
        GrowPylon(grid, rootX, rootY, centerZ, nacelleX, nacelleY, nacelleZ, pylonHalfThickness, pylonHalfChord);

        var warp = p.NacelleStyle == NacelleStyle.Warp;
        var collectorDepth = Math.Max(1, detail);
        var grilleHalfHeight = Math.Max(1, radius / 3);
        var grilleBand = Math.Max(1, detail / 2);

        for (var dz = -halfLength; dz <= halfLength; dz++)
        {
            var z = nacelleZ + dz;
            if (z < 0 || z >= len) continue;

            // Taper the pod toward both ends so it reads as a streamlined engine pod.
            var t = MathF.Abs(dz) / (float)halfLength;
            var r = (int)MathF.Round(radius * (1f - t * t * 0.55f));

            // The collector caps the *front* -- bow is z=0 -- while a plain thruster lights the
            // aft end instead. Which end glows is most of what separates the two readings.
            var collector = warp && dz <= -halfLength + collectorDepth;
            var thruster = !warp && dz == halfLength;
            var inGrilleSpan = warp && MathF.Abs(dz) < halfLength * 0.72f;

            for (var dx = -r; dx <= r; dx++)
                for (var dy = -r; dy <= r; dy++)
                {
                    if (dx * dx + dy * dy > r * r + r) continue;

                    var material = VoxelMaterial.Hull;
                    if (collector || thruster)
                    {
                        material = VoxelMaterial.Glow;
                    }
                    else if (inGrilleSpan && MathF.Abs(dy) <= grilleHalfHeight)
                    {
                        // The lit grille runs along the pod's flanks at mid-height. Found from the
                        // circle's own edge at this dy rather than from a fixed dx, or the band
                        // would sink inside the pod wherever the section is widest.
                        var edge = (int)MathF.Sqrt(Math.Max(0, r * r + r - dy * dy));
                        if (Math.Abs(dx) >= edge - grilleBand) material = VoxelMaterial.Glow;
                    }

                    grid.SetMirrored(nacelleX + dx, nacelleY + dy, z, material);
                }
        }
    }

    /// <summary>
    /// A strut running from the hull flank to wherever the pod ended up, interpolating all three
    /// axes at once. The pylon has to be general in 3D rather than a purely vertical drop: once a
    /// pod can be raised *and* swept aft, a strut that only descends leaves it floating.
    ///
    /// Stepping along the longest axis guarantees consecutive samples move at most one voxel on
    /// it and no more on the others, so the boxes always overlap and the pod stays attached
    /// however steep the sweep.
    ///
    /// The cross-section is thin vertically and deep fore-and-aft, so raising
    /// <paramref name="halfChord"/> turns the strut into the broad swept blade Starfleet pylons
    /// are, rather than merely a thicker rod.
    /// </summary>
    private static void GrowPylon(
        VoxelGrid grid, int rootX, int rootY, int rootZ, int tipX, int tipY, int tipZ,
        int halfThickness, int halfChord)
    {
        var steps = Math.Max(1, Math.Max(
            Math.Abs(tipX - rootX), Math.Max(Math.Abs(tipY - rootY), Math.Abs(tipZ - rootZ))));

        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var x = (int)MathF.Round(rootX + (tipX - rootX) * t);
            var y = (int)MathF.Round(rootY + (tipY - rootY) * t);
            var z = (int)MathF.Round(rootZ + (tipZ - rootZ) * t);

            for (var dy = -halfThickness; dy <= halfThickness; dy++)
                for (var dz = -halfChord; dz <= halfChord; dz++)
                    grid.SetMirrored(x, y + dy, z + dz, VoxelMaterial.HullDark);
        }
    }

    /// <summary>
    /// A terraced ridge down the centreline, rising toward the stern.
    ///
    /// This is the Imperial read, and it is a different thing from either a bridge tower or a deck
    /// terrace: a tower is a block at one station, a terrace steps the whole beam, and this steps
    /// only a narrow band of the centreline while climbing the length of the ship. Together with a
    /// flat keel it is what turns a wedge planform into a Star Destroyer rather than a flat iron.
    ///
    /// Each column is raised from the surface actually under it, so the ridge follows a chamfered
    /// deck or a terraced one instead of hanging over it. Top[] is deliberately *not* raised: the
    /// ridge is a mounted structure, and telling the surface passes it is deck would have them carve
    /// panel recesses into it.
    /// </summary>
    private static void GrowDorsalSpine(VoxelGrid grid, ShipParameters p, HullColumn hull, int len, int maxHH)
    {
        var env = hull.Envelope;
        var from = env.SliceAt(0.3f);
        var to = env.SliceAt(0.95f);
        if (to <= from) return;

        var scale = Math.Clamp(p.SpineHeight, 0.1f, 4f);
        var peak = Math.Max(1, (int)MathF.Round(maxHH * 0.75f * scale));
        var detail = DetailUnit(len);

        // Terraced rather than a smooth ramp: the steps are the read. Enough of them to be a
        // staircase, few enough that each riser is more than a voxel at any resolution.
        var tiers = Math.Clamp(peak / Math.Max(1, detail), 3, 7);

        for (var z = from; z <= to && z < len; z++)
        {
            var hullHalfWidth = HalfWidthOf(hull, z);
            if (hullHalfWidth < 2) continue;

            var t = (z - from) / (float)(to - from);
            var tier = Math.Clamp((int)(t * tiers), 0, tiers - 1);
            var height = Math.Max(1, (int)MathF.Round(peak * (0.28f + 0.72f * (tier / (float)(tiers - 1)))));

            // Narrows as it climbs, so the ridge reads as a spine rather than as a second hull.
            var halfWidth = Math.Max(1, (int)MathF.Round(hullHalfWidth * (0.46f - 0.16f * t)));
            var spine = env.SpineOffset(z);

            for (var dx = -halfWidth; dx <= halfWidth; dx++)
            {
                var x = hull.XOffset + spine + dx;
                var surface = TopFilledY(grid, x, z);
                if (surface is null) continue;

                // The flanks of the ridge are chamfered in too, or its edges read as a cut rather
                // than as a built-up structure.
                var shoulder = Shoulder(Math.Abs(dx), halfWidth);
                var columnHeight = Math.Max(1, (int)MathF.Round(height * (1f - shoulder * 0.55f)));
                var material = Math.Abs(dx) > halfWidth - Math.Max(1, detail) ? VoxelMaterial.Panel : VoxelMaterial.Hull;

                for (var dy = 1; dy <= columnHeight; dy++)
                    grid.SetMirrored(x, surface.Value + dy, z, material);
            }
        }
    }

    /// <summary>A stepped command tower topped by a thin antenna mast. The mast is what gives the
    /// silhouette a recognizable "bridge" read from a distance, so it is deliberately tall and thin.</summary>
    private static void GrowSuperstructure(VoxelGrid grid, ShipParameters p, HullColumn primary, Layout layout, int len, int maxHH)
    {
        var env = primary.Envelope;

        // The parameter sets the station and the seed only jitters around it, at half the amplitude
        // it used to have on its own. Before, the position *was* the jitter, so an aft-mounted
        // bridge -- the whole Imperial silhouette -- could not be asked for at any setting.
        var station = Math.Clamp(p.TowerPosition + (layout.TowerCenter - 0.42f) * 0.5f, 0.08f, 0.95f);
        var centerZ = env.SliceAt(station);
        // Floored just above zero rather than at 0.4: the point of a low setting is a bridge that
        // barely breaks the deck line, and a floor of 0.4 made the bottom of the slider's travel
        // do nothing at all.
        var scale = Math.Max(0.1f, p.SuperstructureSize);

        // On a hollow hull the centreline is empty, so the tower is seated on the band instead.
        var spine = env.SpineOffset(centerZ);

        // Seated on the surface actually under the tower's own column, not on the envelope's deck.
        // On a hollow hull the section is chamfered toward both rims, so the band the tower stands on
        // sits well below Top[z] and a tower placed there floats -- with, on a fork, the canopy
        // painted onto it, since the surface passes then find the tower instead of the hull.
        var seated = TopFilledY(grid, primary.XOffset + spine, centerZ);
        if (seated is null) return;
        var y = seated.Value;

        for (var tier = 0; tier < 3; tier++)
        {
            var halfWidth = Math.Max(1, (int)MathF.Round(HalfWidthOf(primary, centerZ) * (0.55f - tier * 0.13f) * scale));
            var halfLength = Math.Max(1, (int)MathF.Round(len * (0.09f - tier * 0.02f) * scale));
            var height = Math.Max(1, (int)MathF.Round(maxHH * (0.45f - tier * 0.08f) * scale));

            for (var dz = -halfLength; dz <= halfLength; dz++)
            {
                var z = centerZ + dz;
                if (z < 0 || z >= len) continue;
                for (var dx = -halfWidth; dx <= halfWidth; dx++)
                    for (var dy = 1; dy <= height; dy++)
                        grid.SetMirrored(primary.XOffset + spine + dx, y + dy, z, tier == 1 ? VoxelMaterial.Panel : VoxelMaterial.Hull);
            }

            y += height;
        }

        // Mast thickness follows the detail unit: a fixed 1-voxel spike would vanish to a hair
        // at high resolution, and the mast is a silhouette cue that needs to stay readable.
        var detail = DetailUnit(len);

        if (p.TowerDomes)
            GrowTowerDomes(grid, primary, spine, centerZ, y,
                Math.Max(1, (int)MathF.Round(HalfWidthOf(primary, centerZ) * 0.29f * scale)),
                Math.Max(2, (int)MathF.Round(maxHH * 0.3f * scale)));
        var mastHalf = Math.Max(0, detail / 2);
        var mastHeight = Math.Max(4, (int)MathF.Round(maxHH * 2.2f * scale));

        for (var dy = 1; dy <= mastHeight; dy++)
            for (var dx = -mastHalf; dx <= mastHalf; dx++)
                for (var dz = -mastHalf; dz <= mastHalf; dz++)
                    grid.SetMirrored(primary.XOffset + spine + dx, y + dy, centerZ + dz, VoxelMaterial.HullDark);

        // A short crossbar near the top -- reads as a sensor array and breaks the bare spike.
        var barY = y + (int)MathF.Round(mastHeight * 0.7f);
        var barHalf = Math.Max(1, detail * 2);
        for (var dx = -barHalf; dx <= barHalf; dx++)
            grid.SetMirrored(primary.XOffset + spine + dx, barY, centerZ, VoxelMaterial.HullDark);
    }

    /// <summary>
    /// The pair of geodesic sensor globes flanking the top of the tower.
    ///
    /// Centred *on* the tower's top outboard corner rather than beside it. A sphere pushed clear of
    /// the tower and dropped to be tangent to it touches at one point, and a sphere's extreme points
    /// lie on its axes -- so its innermost voxel is at the top and its lowest voxel is at the
    /// outside, neither of which is anywhere near the tower. Centring on a voxel known to be filled
    /// puts the sphere's whole lower-inner octant inside the tower block, and what remains visible is
    /// the upper three-quarters sitting on the bridge's shoulder.
    ///
    /// The radius is capped by the tower's own width for the same reason it looks better: a globe
    /// wider than the structure carrying it has nothing to hold it.
    /// </summary>
    private static void GrowTowerDomes(
        VoxelGrid grid, HullColumn primary, int spine, int centerZ, int towerTop, int towerHalfWidth, int radius)
    {
        radius = Math.Clamp(radius, 2, Math.Max(2, towerHalfWidth));

        var x0 = primary.XOffset + spine + towerHalfWidth;

        for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
                for (var dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dy * dy + dz * dz > radius * radius + radius) continue;
                    grid.SetMirrored(x0 + dx, towerTop + dy, centerZ + dz, VoxelMaterial.Panel);
                }
    }

    private static void GrowTurrets(VoxelGrid grid, ShipParameters p, HullColumn primary, Layout layout, int len)
    {
        var spread = Math.Clamp(layout.TurretSpread, 0.4f, 0.75f);
        var detail = DetailUnit(len);
        var baseRadius = Math.Max(1, detail);
        var barrelLength = Math.Max(2, detail * 3);

        for (var i = 0; i < p.TurretCount; i++)
        {
            var t = (i + 0.5f) / p.TurretCount;
            var z = Math.Clamp(primary.Envelope.SliceAt(0.2f + t * spread), baseRadius + 1, len - baseRadius - 2);
            var hw = HalfWidthOf(primary, z);
            if (hw < 2) continue;

            var spine = primary.Envelope.SpineOffset(z);
            var x = primary.XOffset + spine + (spine > 0 ? 0 : Math.Max(1, (int)MathF.Round(hw * 0.55f)));
            var onTop = i % 2 == 0;
            var dir = onTop ? 1 : -1;

            // Seat the mount on the *actual* surface at this column, not on the envelope arrays:
            // the real deck sits lower than Top[z] out on the chamfered flank and outside the
            // terraces' width, so trusting the envelope leaves turrets floating clear of the hull.
            var surfaceY = onTop ? TopFilledY(grid, x, z) : BottomFilledY(grid, x, z);
            if (surfaceY is null) continue;
            var baseY = surfaceY.Value + dir;

            // Base ring, then a smaller housing, then a barrel poking forward.
            for (var dz = -baseRadius; dz <= baseRadius; dz++)
                for (var dx = -baseRadius; dx <= baseRadius; dx++)
                    for (var dy = 0; dy < detail; dy++)
                        grid.SetMirrored(x + dx, baseY + dy * dir, z + dz, VoxelMaterial.HullDark);

            var housingY = baseY + detail * dir;
            var housingHalf = Math.Max(1, baseRadius / 2);
            for (var dz = -housingHalf; dz <= housingHalf; dz++)
                for (var dy = 0; dy < detail; dy++)
                    grid.SetMirrored(x, housingY + dy * dir, z + dz, VoxelMaterial.Panel);

            // Start the barrel flush against the housing's forward face. Measuring it from the
            // wider base ring instead left a one-voxel gap, detaching the barrel entirely.
            for (var dz = 1; dz <= barrelLength; dz++)
                grid.SetMirrored(x, housingY, z - housingHalf - dz, VoxelMaterial.HullDark);
        }
    }

    /// <summary>Engine block: recessed dark housings at the stern with a stepped, shrinking
    /// exhaust plume behind each one. The plume extends past the hull so the glow is visible in
    /// silhouette rather than buried inside the tail.</summary>
    private static void GrowEngines(VoxelGrid grid, ShipParameters p, IReadOnlyList<HullColumn> hulls, int len, int maxHH)
    {
        // Every hull gets its own engine block: an outrigger trailing no exhaust reads as dead
        // weight bolted to the side rather than as part of the ship's propulsion.
        foreach (var hull in hulls)
            GrowEnginesOnHull(grid, p, hull, len, maxHH);
    }

    private static void GrowEnginesOnHull(VoxelGrid grid, ShipParameters p, HullColumn hull, int len, int maxHH)
    {
        var env = hull.Envelope;

        // This hull's own stern, not the ship's. A saucer stops short of the stern, and its engines
        // belong at its trailing edge -- which is exactly where Starfleet puts impulse engines.
        var tailZ = env.LastZ;
        var tailHalfWidth = Math.Max(2, HalfWidthOf(hull, tailZ));
        var tailTop = Math.Max(1, (int)MathF.Round(env.Top[tailZ]));
        var count = Math.Max(1, p.EngineCount);
        var radius = Math.Max(2, (int)MathF.Round(MathF.Min(tailHalfWidth, maxHH) * (count <= 2 ? 0.85f : 0.6f)));

        var positions = new List<(int X, int Y)>();
        if (count == 1)
        {
            positions.Add((0, 0));
        }
        else if (count == 2)
        {
            positions.Add((tailHalfWidth - radius, 0));
            positions.Add((-(tailHalfWidth - radius), 0));
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var angle = i / (float)count * MathF.PI * 2f + MathF.PI / count;
                positions.Add((
                    (int)MathF.Round(MathF.Cos(angle) * (tailHalfWidth - radius) * 0.9f),
                    (int)MathF.Round(MathF.Sin(angle) * MathF.Max(1, tailTop - radius) * 0.9f)));
            }
        }

        var housingDepth = Math.Max(3, (int)MathF.Round(len * 0.07f));
        var plumeLength = p.EngineStyle == EngineStyle.Ring ? radius + 2 : radius + 5;

        // On a hollow hull the stern centreline is empty, so shift the whole engine block out onto
        // the band. Without this a ring ship's engines hang unattached in the middle of the hole.
        var spine = env.SpineOffset(tailZ);

        foreach (var (ex, ey) in positions)
        {
            // Housing: a dark cylinder sunk into the tail.
            for (var dz = -housingDepth; dz <= 0; dz++)
            {
                var z = tailZ + dz;
                for (var dx = -radius; dx <= radius; dx++)
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        var d2 = dx * dx + dy * dy;
                        if (d2 > radius * radius + radius) continue;
                        // Ring engines are hollow: only the outer shell is solid.
                        if (p.EngineStyle == EngineStyle.Ring && d2 < (radius - 1) * (radius - 1)) continue;
                        grid.SetMirrored(hull.XOffset + spine + ex + dx, env.CentreY + ey + dy, z, VoxelMaterial.HullDark);
                    }
            }

            // Plume: shrinking discs of emissive, stepping out behind the hull.
            for (var dz = 1; dz <= plumeLength; dz++)
            {
                var t = dz / (float)plumeLength;
                var r = (int)MathF.Round(radius * (1f - t * 0.75f));
                if (r < 0) continue;
                for (var dx = -r; dx <= r; dx++)
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (dx * dx + dy * dy > r * r + r) continue;
                        grid.SetMirrored(hull.XOffset + spine + ex + dx, env.CentreY + ey + dy, tailZ + dz, VoxelMaterial.Glow);
                    }
            }
        }
    }

    /// <summary>Sets a canopy into the nose deck: the glass sits inside a darker frame, which is
    /// what makes it read as a cockpit rather than as a colored patch on the plating.</summary>
    private static void CarveCockpit(VoxelGrid grid, ShipParameters p, HullClassPreset preset, HullColumn primary, int len)
    {
        var size = Math.Max(0.1f, p.CockpitSize);
        var centerZ = primary.Envelope.SliceAt(preset.NoseFraction * 1.15f);
        var halfLength = Math.Max(2, (int)MathF.Round(len * 0.07f * size));
        var detail = DetailUnit(len);

        for (var dz = -halfLength; dz <= halfLength; dz++)
        {
            var z = centerZ + dz;
            if (z < 0 || z >= len) continue;

            var widthFactor = p.CockpitStyle == CockpitStyle.FlatCanopy ? 0.7f : 0.55f;
            var hw = (int)MathF.Round(HalfWidthOf(primary, z) * widthFactor * size);
            if (hw < 1) continue;

            // Frame thickness follows the detail unit, so the canopy surround stays a visible
            // border rather than thinning to a single voxel as resolution rises -- but never so
            // thick that it swallows the glass. A small canopy used to come out as solid frame with
            // no window in it at all, which made the bottom of the size slider produce no canopy.
            var frameDepth = Math.Clamp(detail, 1, Math.Max(1, halfLength - 1));
            var isFrameSlice = Math.Abs(dz) > halfLength - frameDepth;

            var spine = primary.Envelope.SpineOffset(z);

            for (var dx = -hw; dx <= hw; dx++)
            {
                var x = primary.XOffset + spine + dx;
                var topY = TopFilledY(grid, x, z);
                if (topY is null || !IsPlating(grid, x, topY.Value, z)) continue;

                var isFrame = Math.Abs(dx) > hw - Math.Min(detail, hw) || isFrameSlice;
                grid.SetMirrored(x, topY.Value, z, isFrame ? VoxelMaterial.HullDark : VoxelMaterial.Cockpit);
            }
        }
    }

    // ---- Surface detail -----------------------------------------------------------------

    /// <summary>Everything that turns a clean solid into something that reads as a built machine:
    /// lateral panel seams, longitudinal accent stripes, lit ports along the flanks, and randomly
    /// scattered raised plates and recessed pockets. All of it queries the real voxel surface, so
    /// it follows terraces, wing roots and towers instead of floating over them.</summary>
    private static void DetailPass(VoxelGrid grid, Random rng, ShipParameters p, IReadOnlyList<HullColumn> hulls, int len, int maxHH)
    {
        var detail = DetailUnit(len);

        // Decorate every hull, not just the primary one: an undecorated outrigger next to a
        // plated, port-lit main hull reads as an unfinished block rather than as part of the ship.
        foreach (var hull in hulls)
        {
            PaintPanelSeams(grid, hull, len, detail);
            PaintWindows(grid, hull, len, detail);

            if (p.Greebles)
            {
                AddRaisedPlates(grid, rng, p, hull, len, detail);
                CarveRecesses(grid, rng, p, hull, len, detail);
            }
        }

        // Stripes run once over the whole ship: the wing band and bow chevron are ship-level
        // markings, not per-hull ones.
        PaintAccentStripes(grid, p, hulls, len, maxHH, detail);
    }

    /// <summary>Dark seams every few slices across the top deck, plus a continuous seam down the
    /// chamfer line where the flat deck meets the flank. Seam width scales with the detail unit,
    /// so a seam stays a visible groove instead of thinning to a hairline as resolution rises.</summary>
    private static void PaintPanelSeams(VoxelGrid grid, HullColumn hull, int len, int detail)
    {
        var spacing = Math.Max(4, len / 9);

        for (var z = 0; z < len; z++)
        {
            var hw = HalfWidthOf(hull, z);
            if (hw < 1) continue;

            var lateralSeam = z % spacing < detail;
            var chamferX = (int)MathF.Round(hw * DeckFlatFraction);

            for (var dx = -hw; dx <= hw; dx++)
            {
                var absX = Math.Abs(dx);
                var onChamfer = absX >= chamferX && absX < chamferX + detail;
                if (!lateralSeam && !onChamfer) continue;

                var x = hull.XOffset + dx;
                var topY = TopFilledY(grid, x, z);
                if (topY is null || !IsPlating(grid, x, topY.Value, z)) continue;
                grid.SetMirrored(x, topY.Value, z, VoxelMaterial.HullDark);
            }
        }
    }

    /// <summary>Longitudinal squadron stripes: one along each upper flank, a chevron across the
    /// bow, and a band over the wings. These are the strongest readability cue at a glance, so
    /// the flank stripe runs the full length rather than being broken up.</summary>
    private static void PaintAccentStripes(VoxelGrid grid, ShipParameters p, IReadOnlyList<HullColumn> hulls, int len, int maxHH, int detail)
    {
        var noseEnd = (int)MathF.Round(len * 0.22f);
        var outerHull = hulls[^1];
        var primary = hulls[0];

        for (var z = 0; z < len; z++)
        {
            // Flank stripe: run it down the ship's outboard flank -- on a multi-hull that is the
            // outrigger's outer side, which is the flank actually seen in profile.
            var outerHalfWidth = HalfWidthOf(outerHull, z);
            if (outerHalfWidth >= 1)
            {
                var stripeY = outerHull.Envelope.DeckFractionY(z, 0.35f);
                for (var dy = 0; dy < detail; dy++)
                {
                    var searchTo = outerHull.XOffset + outerHalfWidth + 1;
                    var sideX = SideFilledX(grid, stripeY + dy, z, searchTo);
                    if (sideX is not null && IsPlating(grid, sideX.Value, stripeY + dy, z))
                        grid.SetMirrored(sideX.Value, stripeY + dy, z, VoxelMaterial.Accent);
                }
            }

            // Nose chevron: a few thin lateral accent bands across the top of the primary bow.
            var hw = HalfWidthOf(primary, z);
            if (hw < 1 || z >= noseEnd || z % (6 * detail) >= detail) continue;

            for (var dx = -hw; dx <= hw; dx++)
            {
                var x = primary.XOffset + dx;
                var topY = TopFilledY(grid, x, z);
                if (topY is null || !IsPlating(grid, x, topY.Value, z)) continue;
                grid.SetMirrored(x, topY.Value, z, VoxelMaterial.Accent);
            }
        }

        if (p.WingStyle is WingStyle.None or WingStyle.TwinFin) return;

        // Wing stripe: one narrow band across the outboard third of each wing. Deliberately a
        // single band rather than a repeating pattern -- in this art style the markings are a
        // sparse accent, and striping half the span turns the wing blue instead of marking it.
        var span = Math.Max(2, (int)MathF.Round(p.WingSpan / VoxelSize));
        var wingRootZ = Math.Clamp((int)MathF.Round(0.56f * (len - 1)), 0, len - 1);
        var wingRootX = OuterEdge(hulls, wingRootZ);
        var bandStart = wingRootX + (int)MathF.Round(span * 0.62f);
        var bandWidth = Math.Max(2, detail * 2);

        for (var x = bandStart; x < bandStart + bandWidth; x++)
        {
            for (var z = 0; z < len; z++)
            {
                var topY = TopFilledY(grid, x, z);
                if (topY is null || topY.Value > maxHH) continue;
                if (!IsPlating(grid, x, topY.Value, z)) continue;
                grid.SetMirrored(x, topY.Value, z, VoxelMaterial.Accent);
            }
        }
    }

    /// <summary>Rows of lit ports down each flank. Deliberately sparse and evenly spaced: windows
    /// are a scale cue, and scattering them densely reads as noise rather than as decks. The
    /// search is capped at the hull half-width so ports land on the fuselage, not on wingtips.</summary>
    private static void PaintWindows(VoxelGrid grid, HullColumn hull, int len, int detail)
    {
        var env = hull.Envelope;
        var spacing = Math.Max(4, len / 8);

        for (var z = 0; z < len; z++)
        {
            if (z % spacing != 1) continue;
            if (HalfWidthOf(hull, z) < 2) continue;

            // Two rows at different deck heights, so tall hulls look multi-decked. Each port is a
            // detail-sized patch rather than a single voxel, so ports stay legible as windows.
            for (var row = 0; row < 2; row++)
            {
                var y0 = env.DeckFractionY(z, row == 0 ? 0.55f : 0.15f);

                for (var dz = 0; dz < detail; dz++)
                    for (var dy = 0; dy < detail; dy++)
                    {
                        var y = y0 + dy;
                        var zz = z + dz;
                        if (zz >= len) continue;

                        var searchTo = hull.XOffset + HalfWidthOf(hull, zz) + 1;
                        var sideX = SideFilledX(grid, y, zz, searchTo);
                        if (sideX is null || !IsPlating(grid, sideX.Value, y, zz)) continue;
                        grid.SetMirrored(sideX.Value, y, zz, VoxelMaterial.Window);
                    }
            }
        }
    }

    /// <summary>Scatters raised plates on the deck. Plate footprint *and* height scale with the
    /// detail unit, so higher resolution yields the same reading of chunky plating at a finer
    /// grain rather than a rash of one-voxel pimples.</summary>
    private static void AddRaisedPlates(VoxelGrid grid, Random rng, ShipParameters p, HullColumn hull, int len, int detail)
    {
        var count = (int)MathF.Round(p.GreebleDensity * len * 0.7f / detail);

        for (var i = 0; i < count; i++)
        {
            var z0 = rng.Next(2, Math.Max(3, len - 2));
            var lengthZ = rng.Next(2, 6) * detail;
            var hw = HalfWidthOf(hull, Math.Clamp(z0, 0, len - 1));
            if (hw < 2) continue;

            var dx0 = rng.Next(-hw, hw);
            var widthX = rng.Next(1, Math.Max(2, hw - dx0 + 1));

            for (var z = z0; z < Math.Min(z0 + lengthZ, len); z++)
                for (var dx = dx0; dx <= Math.Min(dx0 + widthX, hw); dx++)
                {
                    var x = hull.XOffset + dx;
                    var topY = TopFilledY(grid, x, z);
                    if (topY is null || !IsHullDeck(hull, topY.Value, z)) continue;
                    if (!IsPlating(grid, x, topY.Value, z)) continue;
                    for (var dy = 1; dy <= detail; dy++)
                        grid.SetMirrored(x, topY.Value + dy, z, VoxelMaterial.Panel);
                }
        }
    }

    /// <summary>Cuts shallow pockets into the deck and darkens their floor. Recesses matter as
    /// much as raised plates: they add self-shadowing, which is what sells the surface as machined.</summary>
    private static void CarveRecesses(VoxelGrid grid, Random rng, ShipParameters p, HullColumn hull, int len, int detail)
    {
        var count = (int)MathF.Round(p.GreebleDensity * len * 0.5f / detail);

        for (var i = 0; i < count; i++)
        {
            var z0 = rng.Next(2, Math.Max(3, len - 2));
            var lengthZ = rng.Next(2, 5) * detail;
            var hw = HalfWidthOf(hull, Math.Clamp(z0, 0, len - 1));
            if (hw < 3) continue;

            var dx0 = rng.Next(-hw, hw - 1);
            var widthX = rng.Next(1, Math.Max(2, hw - dx0));

            for (var z = z0; z < Math.Min(z0 + lengthZ, len); z++)
                for (var dx = dx0; dx <= Math.Min(dx0 + widthX, hw); dx++)
                {
                    var x = hull.XOffset + dx;
                    // Never cut a pocket deeper than the plate it sits in. Toward the tail and out
                    // on the flanks the hull thins to a couple of voxels, and cutting the full
                    // depth there punches straight through, severing the column and stranding the
                    // voxels outboard of it as loose debris.
                    var columnTop = TopFilledY(grid, x, z);
                    var columnBottom = BottomFilledY(grid, x, z);
                    if (columnTop is null || columnBottom is null) continue;
                    if (columnTop.Value - columnBottom.Value < detail + 1) continue;

                    // Cut `detail` voxels deep so the pocket keeps a visible lip at any resolution.
                    for (var step = 0; step < detail; step++)
                    {
                        var topY = TopFilledY(grid, x, z);
                        if (topY is null || !IsHullDeck(hull, topY.Value, z)) break;
                        if (!IsPlating(grid, x, topY.Value, z)) break;

                        grid.Remove(x, topY.Value, z);
                        grid.Remove(-x, topY.Value, z);
                    }

                    var floorY = TopFilledY(grid, x, z);
                    if (floorY is not null && IsPlating(grid, x, floorY.Value, z))
                        grid.SetMirrored(x, floorY.Value, z, VoxelMaterial.HullDark);
                }
        }
    }

    // ---- Surface queries ----------------------------------------------------------------

    /// <summary>Topmost filled voxel in a column, or null if the column is empty. Scans from the
    /// grid's tracked upper bound rather than a guessed range, which keeps these queries cheap as
    /// the resolution (and so the number of them) rises.</summary>
    private static int? TopFilledY(VoxelGrid grid, int x, int z)
    {
        if (grid.IsEmpty) return null;

        for (var y = grid.MaxY; y >= grid.MinY; y--)
            if (grid.IsFilled(x, y, z))
                return y;
        return null;
    }

    /// <summary>Bottommost filled voxel in a column, or null if the column is empty -- the
    /// underside counterpart to <see cref="TopFilledY"/>, used to seat belly-mounted turrets.</summary>
    private static int? BottomFilledY(VoxelGrid grid, int x, int z)
    {
        if (grid.IsEmpty) return null;

        for (var y = grid.MinY; y <= grid.MaxY; y++)
            if (grid.IsFilled(x, y, z))
                return y;
        return null;
    }

    /// <summary>Outermost filled voxel in a row (positive X side), or null if the row is empty.</summary>
    private static int? SideFilledX(VoxelGrid grid, int y, int z, int searchSide)
    {
        for (var x = searchSide; x >= 0; x--)
            if (grid.IsFilled(x, y, z))
                return x;
        return null;
    }

    /// <summary>Whether a voxel is ordinary plating, and therefore safe to repaint. Guards the
    /// detail passes against overwriting canopy glass, engine glow or lit ports.</summary>
    private static bool IsPlating(VoxelGrid grid, int x, int y, int z) =>
        grid.Get(x, y, z) is VoxelMaterial.Hull or VoxelMaterial.Panel or VoxelMaterial.HullDark;

    /// <summary>Whether a column's surface is the hull deck itself rather than something standing
    /// on it. Turrets, the tower and the mast are built from the same plating materials as the
    /// hull, so <see cref="IsPlating"/> cannot tell them apart -- without this guard the carving
    /// pass eats into mounted structures and can detach a turret or its barrel from the ship.</summary>
    private static bool IsHullDeck(HullColumn hull, int y, int z) => y <= hull.Envelope.DeckY(z);
}
