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

    private sealed class Envelope
    {
        public required int[] HalfWidth { get; init; }
        public required int[] Top { get; init; }
        public required int[] Bottom { get; init; }
    }

    /// <summary>Per-seed placement jitter for the bolt-on structures. Drawn up front in a fixed
    /// order so the whole ship stays deterministic for a given seed, and applied so that two
    /// seeds differ in *where* things sit, not just in the hull outline.</summary>
    private sealed record Layout(float WingCenter, float TowerCenter, float NacelleCenter, float TurretSpread);

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
        var maxHalfHeight = Math.Max(2, (int)MathF.Round(maxHalfWidth * preset.HeightRatio));

        var env = GrowEnvelope(rng, p, preset, lengthVoxels, maxHalfWidth, maxHalfHeight);

        var layout = new Layout(
            WingCenter: 0.56f + Noise(rng, 0.07f),
            TowerCenter: 0.42f + Noise(rng, 0.10f),
            NacelleCenter: 0.60f + Noise(rng, 0.07f),
            TurretSpread: 0.6f + Noise(rng, 0.15f));

        FillHull(grid, env, lengthVoxels);
        AddDeckTerraces(grid, env, p, lengthVoxels);

        if (p.WingStyle != WingStyle.None)
            GrowWings(grid, p, env, layout, lengthVoxels, maxHalfHeight);

        if (p.Nacelles)
            GrowNacelles(grid, p, env, layout, lengthVoxels, maxHalfHeight);

        if (p.Superstructure && p.HullClass != HullClass.Fighter)
            GrowSuperstructure(grid, p, env, layout, lengthVoxels, maxHalfHeight);

        if (p.TurretCount > 0)
            GrowTurrets(grid, p, env, layout, lengthVoxels);

        GrowEngines(grid, p, env, lengthVoxels, maxHalfWidth, maxHalfHeight);

        if (p.CockpitStyle != CockpitStyle.None)
            CarveCockpit(grid, p, preset, env, lengthVoxels, maxHalfHeight);

        DetailPass(grid, rng, p, env, lengthVoxels, maxHalfWidth, maxHalfHeight);

        return grid;
    }

    // ---- Hull envelope ------------------------------------------------------------------

    /// <summary>The 0..1 base envelope shape before noise: a rise through the nose taper, a
    /// flat body, then a taper toward the tail that doesn't fully close (the engine block caps it).
    /// <paramref name="taper"/> bends the nose curve from bulbous (0) through linear (0.5) to a
    /// sharp point (1).</summary>
    private static float EnvelopeShape(float u, HullClassPreset preset, float taper)
    {
        if (u < preset.NoseFraction)
            return MathF.Pow(u / preset.NoseFraction, 0.45f + taper * 1.1f);
        if (u < preset.TailFraction)
            return 1f;
        var t = (u - preset.TailFraction) / (1f - preset.TailFraction);
        return 1f - t * 0.45f;
    }

    private static Envelope GrowEnvelope(Random rng, ShipParameters p, HullClassPreset preset, int len, int maxHW, int maxHH)
    {
        var w = new float[len];
        var top = new float[len];
        var bottom = new float[len];

        // Jaggedness is a *relative* roughness: scaling it by the hull size keeps a "rough"
        // class equally rough at any resolution, instead of getting smoother as voxels shrink.
        var noiseScale = preset.Jaggedness * maxHW * 0.13f;

        // Long-wavelength bulges/waists, one independent set per axis. Unlike the walk noise
        // these survive smoothing and rounding, so they are what makes two seeds read as
        // different hull designs rather than as the same hull with different surface fuzz.
        var widthWave = new ProfileWave(rng, maxHW * 0.34f * preset.Jaggedness);
        var topWave = new ProfileWave(rng, maxHH * 0.4f * preset.Jaggedness);
        var bottomWave = new ProfileWave(rng, maxHH * 0.22f * preset.Jaggedness);

        float cw = 0f, ct = 0f, cb = 0f;

        for (var z = 0; z < len; z++)
        {
            var u = z / (float)(len - 1);
            var shape = EnvelopeShape(u, preset, p.Taper);

            // A dorsal rise over the mid-body, and a flatter keel underneath: a hull that is
            // taller on top than below reads as "has decks" rather than as a symmetric tube.
            var dorsal = 0.72f + 0.5f * MathF.Sin(MathF.PI * Math.Clamp(u, 0f, 1f));

            // The waves scale with `shape` so they fade out at the nose and tail: a bulge should
            // swell the mid-body, not make the bow sprout lumps where the hull should be closing.
            var targetW = shape * (maxHW + widthWave.At(u));
            var targetTop = shape * (maxHH * dorsal + topWave.At(u));
            var targetBottom = shape * (maxHH * 0.62f + bottomWave.At(u));

            // Upper clamps leave headroom above the nominal size so a wave crest can actually
            // bulge the hull instead of being flattened against the limit.
            cw = Math.Clamp(cw + (targetW - cw) * 0.5f + Noise(rng, noiseScale), 0f, maxHW * 1.45f);
            ct = Math.Clamp(ct + (targetTop - ct) * 0.5f + Noise(rng, noiseScale * 0.6f), 0f, maxHH * 1.6f);
            cb = Math.Clamp(cb + (targetBottom - cb) * 0.5f + Noise(rng, noiseScale * 0.45f), 0f, maxHH * 1.3f);

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

        return new Envelope
        {
            HalfWidth = Quantize(w),
            Top = Quantize(top),
            Bottom = Quantize(bottom),
        };
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
    private static void FillHull(VoxelGrid grid, Envelope env, int len)
    {
        for (var z = 0; z < len; z++)
        {
            var hw = env.HalfWidth[z];
            if (hw < 0) continue;

            for (var x = 0; x <= hw; x++)
            {
                var shoulder = Shoulder(x, hw);
                var yTop = (int)MathF.Round(env.Top[z] * (1f - shoulder * 0.62f));
                var yBottom = (int)MathF.Round(env.Bottom[z] * (1f - shoulder * 0.55f));
                for (var y = -yBottom; y <= yTop; y++)
                    grid.SetMirrored(x, y, z, VoxelMaterial.Hull);
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

    /// <summary>Stacks progressively narrower, shorter slabs on the spine -- the terraced
    /// step-down silhouette that makes a voxel hull read as a layered capital ship instead of a
    /// single extruded block. Updates Top[] so later passes sit on the new surface.</summary>
    private static void AddDeckTerraces(VoxelGrid grid, Envelope env, ShipParameters p, int len)
    {
        var decks = Math.Clamp(p.Decks, 1, 5);
        var maxTop = env.Top.Max();

        for (var deck = 1; deck <= decks; deck++)
        {
            var widthFraction = 1f - deck * 0.2f;
            var z0 = (int)MathF.Round(len * (0.24f + deck * 0.055f));
            var z1 = (int)MathF.Round(len * (0.82f - deck * 0.05f));
            if (z1 <= z0) break;

            var height = Math.Max(1, (int)MathF.Round(maxTop * 0.16f));

            for (var z = z0; z <= z1 && z < len; z++)
            {
                var hw = (int)MathF.Round(env.HalfWidth[z] * widthFraction);
                if (hw < 1) continue;

                for (var x = 0; x <= hw; x++)
                {
                    var shoulder = Shoulder(x, hw);
                    var slabTop = Math.Max(1, height - (int)MathF.Round(shoulder * height * 0.5f));
                    for (var dy = 1; dy <= slabTop; dy++)
                        grid.SetMirrored(x, env.Top[z] + dy, z, VoxelMaterial.Hull);
                }

                env.Top[z] += height;
            }
        }
    }

    // ---- Structures ---------------------------------------------------------------------

    /// <summary>Wings with a real airfoil-ish section: thickness falls off toward the tip *and*
    /// toward the leading/trailing edges, so the profile is a chamfered blade rather than a slab.</summary>
    private static void GrowWings(VoxelGrid grid, ShipParameters p, Envelope env, Layout layout, int len, int maxHH)
    {
        var span = Math.Max(2, (int)MathF.Round(p.WingSpan / VoxelSize));
        var sweep = MathF.Tan(Math.Clamp(p.WingSweepDegrees, 0f, 70f) * MathF.PI / 180f);

        var (rootOffset, chordFraction, thicknessBase) = p.WingStyle switch
        {
            WingStyle.Delta => (0.06f, 0.34f, maxHH * 0.5f),
            WingStyle.TwinFin => (0.16f, 0.20f, maxHH * 0.42f),
            _ => (0f, 0.26f, maxHH * 0.45f),
        };

        var rootCenter = Math.Clamp(layout.WingCenter + rootOffset, 0.3f, 0.8f);
        var centerZ = (int)MathF.Round(rootCenter * (len - 1));
        var rootChord = Math.Max(2, (int)MathF.Round(len * chordFraction * 0.5f));
        var thick0 = Math.Max(1, (int)MathF.Round(thicknessBase));
        var rootHalfWidth = env.HalfWidth[Math.Clamp(centerZ, 0, len - 1)];

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
                    for (var dy = 0; dy <= thickness * 2 + 1; dy++)
                        grid.SetMirrored(finX, env.Top[z] + dy, z, VoxelMaterial.Hull);
                    continue;
                }

                for (var y = -thickness; y <= thickness; y++)
                    grid.SetMirrored(x, y, z, VoxelMaterial.Hull);
            }
        }
    }

    private static void GrowNacelles(VoxelGrid grid, ShipParameters p, Envelope env, Layout layout, int len, int maxHH)
    {
        var radius = Math.Max(2, (int)MathF.Round(maxHH * 0.75f * p.NacelleSize));
        var halfLength = Math.Max(4, (int)MathF.Round(len * 0.2f * p.NacelleSize));
        var centerZ = (int)MathF.Round(Math.Clamp(layout.NacelleCenter, 0.35f, 0.8f) * (len - 1));
        var hullHalfWidth = env.HalfWidth[Math.Clamp(centerZ, 0, len - 1)];
        var nacelleX = hullHalfWidth + radius + 3;
        var nacelleY = -radius - 1;

        // Pylon: a thin blade bridging hull to pod, angled down so the pod hangs below the wing line.
        for (var x = hullHalfWidth; x <= nacelleX; x++)
        {
            var drop = (int)MathF.Round((x - hullHalfWidth) / (float)Math.Max(1, nacelleX - hullHalfWidth) * -nacelleY);
            for (var dz = -1; dz <= 1; dz++)
                for (var dy = -drop; dy <= 0; dy++)
                    grid.SetMirrored(x, dy, centerZ + dz, VoxelMaterial.HullDark);
        }

        for (var dz = -halfLength; dz <= halfLength; dz++)
        {
            var z = centerZ + dz;

            // Taper the pod toward both ends so it reads as a streamlined engine pod.
            var t = MathF.Abs(dz) / (float)halfLength;
            var r = (int)MathF.Round(radius * (1f - t * t * 0.55f));
            var material = dz == halfLength ? VoxelMaterial.Glow : VoxelMaterial.Hull;

            for (var dx = -r; dx <= r; dx++)
                for (var dy = -r; dy <= r; dy++)
                {
                    if (dx * dx + dy * dy > r * r + r) continue;
                    grid.SetMirrored(nacelleX + dx, nacelleY + dy, z, material);
                }
        }
    }

    /// <summary>A stepped command tower topped by a thin antenna mast. The mast is what gives the
    /// silhouette a recognizable "bridge" read from a distance, so it is deliberately tall and thin.</summary>
    private static void GrowSuperstructure(VoxelGrid grid, ShipParameters p, Envelope env, Layout layout, int len, int maxHH)
    {
        var centerZ = Math.Clamp((int)MathF.Round(Math.Clamp(layout.TowerCenter, 0.25f, 0.7f) * (len - 1)), 0, len - 1);
        var scale = Math.Max(0.4f, p.SuperstructureSize);
        var y = env.Top[centerZ];

        for (var tier = 0; tier < 3; tier++)
        {
            var halfWidth = Math.Max(1, (int)MathF.Round(env.HalfWidth[centerZ] * (0.55f - tier * 0.13f) * scale));
            var halfLength = Math.Max(1, (int)MathF.Round(len * (0.09f - tier * 0.02f) * scale));
            var height = Math.Max(1, (int)MathF.Round(maxHH * (0.45f - tier * 0.08f) * scale));

            for (var dz = -halfLength; dz <= halfLength; dz++)
            {
                var z = centerZ + dz;
                if (z < 0 || z >= len) continue;
                for (var x = 0; x <= halfWidth; x++)
                    for (var dy = 1; dy <= height; dy++)
                        grid.SetMirrored(x, y + dy, z, tier == 1 ? VoxelMaterial.Panel : VoxelMaterial.Hull);
            }

            y += height;
        }

        // Mast thickness follows the detail unit: a fixed 1-voxel spike would vanish to a hair
        // at high resolution, and the mast is a silhouette cue that needs to stay readable.
        var detail = DetailUnit(len);
        var mastHalf = Math.Max(0, detail / 2);
        var mastHeight = Math.Max(4, (int)MathF.Round(maxHH * 2.2f * scale));

        for (var dy = 1; dy <= mastHeight; dy++)
            for (var x = 0; x <= mastHalf; x++)
                for (var dz = -mastHalf; dz <= mastHalf; dz++)
                    grid.SetMirrored(x, y + dy, centerZ + dz, VoxelMaterial.HullDark);

        // A short crossbar near the top -- reads as a sensor array and breaks the bare spike.
        var barY = y + (int)MathF.Round(mastHeight * 0.7f);
        for (var x = 0; x <= Math.Max(1, detail * 2); x++)
            grid.SetMirrored(x, barY, centerZ, VoxelMaterial.HullDark);
    }

    private static void GrowTurrets(VoxelGrid grid, ShipParameters p, Envelope env, Layout layout, int len)
    {
        var spread = Math.Clamp(layout.TurretSpread, 0.4f, 0.75f);
        var detail = DetailUnit(len);
        var baseRadius = Math.Max(1, detail);
        var barrelLength = Math.Max(2, detail * 3);

        for (var i = 0; i < p.TurretCount; i++)
        {
            var t = (i + 0.5f) / p.TurretCount;
            var z = Math.Clamp((int)MathF.Round((0.2f + t * spread) * (len - 1)), baseRadius + 1, len - baseRadius - 2);
            var hw = env.HalfWidth[z];
            if (hw < 2) continue;

            var x = Math.Max(1, (int)MathF.Round(hw * 0.55f));
            var onTop = i % 2 == 0;
            var baseY = onTop ? env.Top[z] + 1 : -env.Bottom[z] - 1;
            var dir = onTop ? 1 : -1;

            // Base ring, then a smaller housing, then a barrel poking forward.
            for (var dz = -baseRadius; dz <= baseRadius; dz++)
                for (var dx = -baseRadius; dx <= baseRadius; dx++)
                    for (var dy = 0; dy < detail; dy++)
                        grid.SetMirrored(x + dx, baseY + dy * dir, z + dz, VoxelMaterial.HullDark);

            var housingY = baseY + detail * dir;
            for (var dz = -baseRadius / 2; dz <= baseRadius / 2; dz++)
                for (var dy = 0; dy < detail; dy++)
                    grid.SetMirrored(x, housingY + dy * dir, z + dz, VoxelMaterial.Panel);

            for (var dz = 1; dz <= barrelLength; dz++)
                grid.SetMirrored(x, housingY, z - baseRadius - dz, VoxelMaterial.HullDark);
        }
    }

    /// <summary>Engine block: recessed dark housings at the stern with a stepped, shrinking
    /// exhaust plume behind each one. The plume extends past the hull so the glow is visible in
    /// silhouette rather than buried inside the tail.</summary>
    private static void GrowEngines(VoxelGrid grid, ShipParameters p, Envelope env, int len, int maxHW, int maxHH)
    {
        var tailZ = len - 1;
        var tailHalfWidth = Math.Max(2, env.HalfWidth[tailZ]);
        var tailTop = Math.Max(1, env.Top[tailZ]);
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
                        grid.Set(ex + dx, ey + dy, z, VoxelMaterial.HullDark);
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
                        grid.Set(ex + dx, ey + dy, tailZ + dz, VoxelMaterial.Glow);
                    }
            }
        }
    }

    /// <summary>Sets a canopy into the nose deck: the glass sits inside a darker frame, which is
    /// what makes it read as a cockpit rather than as a colored patch on the plating.</summary>
    private static void CarveCockpit(VoxelGrid grid, ShipParameters p, HullClassPreset preset, Envelope env, int len, int maxHH)
    {
        var size = Math.Max(0.4f, p.CockpitSize);
        var centerZ = Math.Clamp((int)MathF.Round(preset.NoseFraction * 1.15f * (len - 1)), 2, len - 3);
        var halfLength = Math.Max(2, (int)MathF.Round(len * 0.07f * size));
        var detail = DetailUnit(len);

        for (var dz = -halfLength; dz <= halfLength; dz++)
        {
            var z = centerZ + dz;
            if (z < 0 || z >= len) continue;

            var widthFactor = p.CockpitStyle == CockpitStyle.FlatCanopy ? 0.7f : 0.55f;
            var hw = (int)MathF.Round(env.HalfWidth[z] * widthFactor * size);
            if (hw < 1) continue;

            // Frame thickness follows the detail unit, so the canopy surround stays a visible
            // border rather than thinning to a single voxel as resolution rises.
            var isFrameSlice = Math.Abs(dz) > halfLength - detail;

            for (var x = 0; x <= hw; x++)
            {
                var topY = TopFilledY(grid, x, z);
                if (topY is null || !IsPlating(grid, x, topY.Value, z)) continue;

                var isFrame = x > hw - detail || isFrameSlice;
                grid.SetMirrored(x, topY.Value, z, isFrame ? VoxelMaterial.HullDark : VoxelMaterial.Cockpit);
            }
        }
    }

    // ---- Surface detail -----------------------------------------------------------------

    /// <summary>Everything that turns a clean solid into something that reads as a built machine:
    /// lateral panel seams, longitudinal accent stripes, lit ports along the flanks, and randomly
    /// scattered raised plates and recessed pockets. All of it queries the real voxel surface, so
    /// it follows terraces, wing roots and towers instead of floating over them.</summary>
    private static void DetailPass(VoxelGrid grid, Random rng, ShipParameters p, Envelope env, int len, int maxHW, int maxHH)
    {
        var detail = DetailUnit(len);

        PaintPanelSeams(grid, env, len, detail);
        PaintAccentStripes(grid, p, env, len, maxHH, detail);
        PaintWindows(grid, env, len, detail);

        if (!p.Greebles) return;

        AddRaisedPlates(grid, rng, p, env, len, detail);
        CarveRecesses(grid, rng, p, env, len, detail);
    }

    /// <summary>Dark seams every few slices across the top deck, plus a continuous seam down the
    /// chamfer line where the flat deck meets the flank. Seam width scales with the detail unit,
    /// so a seam stays a visible groove instead of thinning to a hairline as resolution rises.</summary>
    private static void PaintPanelSeams(VoxelGrid grid, Envelope env, int len, int detail)
    {
        var spacing = Math.Max(4, len / 9);

        for (var z = 0; z < len; z++)
        {
            var hw = env.HalfWidth[z];
            if (hw < 1) continue;

            var lateralSeam = z % spacing < detail;
            var chamferX = (int)MathF.Round(hw * DeckFlatFraction);

            for (var x = 0; x <= hw; x++)
            {
                var onChamfer = x >= chamferX && x < chamferX + detail;
                if (!lateralSeam && !onChamfer) continue;

                var topY = TopFilledY(grid, x, z);
                if (topY is null || !IsPlating(grid, x, topY.Value, z)) continue;
                grid.SetMirrored(x, topY.Value, z, VoxelMaterial.HullDark);
            }
        }
    }

    /// <summary>Longitudinal squadron stripes: one along each upper flank, a chevron across the
    /// bow, and a band over the wings. These are the strongest readability cue at a glance, so
    /// the flank stripe runs the full length rather than being broken up.</summary>
    private static void PaintAccentStripes(VoxelGrid grid, ShipParameters p, Envelope env, int len, int maxHH, int detail)
    {
        var noseEnd = (int)MathF.Round(len * 0.22f);

        for (var z = 0; z < len; z++)
        {
            var hw = env.HalfWidth[z];
            if (hw < 1) continue;

            // Flank stripe: the outermost filled voxel at roughly shoulder height, `detail` rows
            // tall. Searching only out to the hull's own half-width keeps the stripe on the
            // fuselage instead of jumping to a wingtip wherever a wing crosses this slice.
            var stripeY = (int)MathF.Round(env.Top[z] * 0.35f);
            for (var dy = 0; dy < detail; dy++)
            {
                var sideX = SideFilledX(grid, stripeY + dy, z, hw + 1);
                if (sideX is not null && IsPlating(grid, sideX.Value, stripeY + dy, z))
                    grid.SetMirrored(sideX.Value, stripeY + dy, z, VoxelMaterial.Accent);
            }

            // Nose chevron: a few thin lateral accent bands across the top of the bow.
            if (z >= noseEnd || z % (6 * detail) >= detail) continue;

            for (var x = 0; x <= hw; x++)
            {
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
        var wingRootX = env.HalfWidth[wingRootZ];
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
    private static void PaintWindows(VoxelGrid grid, Envelope env, int len, int detail)
    {
        var spacing = Math.Max(4, len / 8);

        for (var z = 0; z < len; z++)
        {
            if (z % spacing != 1) continue;

            var hw = env.HalfWidth[z];
            if (hw < 2) continue;

            // Two rows at different deck heights, so tall hulls look multi-decked. Each port is a
            // detail-sized patch rather than a single voxel, so ports stay legible as windows.
            for (var row = 0; row < 2; row++)
            {
                var y0 = (int)MathF.Round(env.Top[z] * (row == 0 ? 0.55f : 0.15f));

                for (var dz = 0; dz < detail; dz++)
                    for (var dy = 0; dy < detail; dy++)
                    {
                        var y = y0 + dy;
                        var zz = z + dz;
                        if (zz >= len) continue;

                        var sideX = SideFilledX(grid, y, zz, env.HalfWidth[zz] + 1);
                        if (sideX is null || !IsPlating(grid, sideX.Value, y, zz)) continue;
                        grid.SetMirrored(sideX.Value, y, zz, VoxelMaterial.Window);
                    }
            }
        }
    }

    /// <summary>Scatters raised plates on the deck. Plate footprint *and* height scale with the
    /// detail unit, so higher resolution yields the same reading of chunky plating at a finer
    /// grain rather than a rash of one-voxel pimples.</summary>
    private static void AddRaisedPlates(VoxelGrid grid, Random rng, ShipParameters p, Envelope env, int len, int detail)
    {
        var count = (int)MathF.Round(p.GreebleDensity * len * 0.7f / detail);

        for (var i = 0; i < count; i++)
        {
            var z0 = rng.Next(2, Math.Max(3, len - 2));
            var lengthZ = rng.Next(2, 6) * detail;
            var hw = env.HalfWidth[Math.Clamp(z0, 0, len - 1)];
            if (hw < 2) continue;

            var x0 = rng.Next(0, hw);
            var widthX = rng.Next(1, Math.Max(2, hw - x0 + 1));

            for (var z = z0; z < Math.Min(z0 + lengthZ, len); z++)
                for (var x = x0; x <= Math.Min(x0 + widthX, hw); x++)
                {
                    var topY = TopFilledY(grid, x, z);
                    if (topY is null || !IsPlating(grid, x, topY.Value, z)) continue;
                    for (var dy = 1; dy <= detail; dy++)
                        grid.SetMirrored(x, topY.Value + dy, z, VoxelMaterial.Panel);
                }
        }
    }

    /// <summary>Cuts shallow pockets into the deck and darkens their floor. Recesses matter as
    /// much as raised plates: they add self-shadowing, which is what sells the surface as machined.</summary>
    private static void CarveRecesses(VoxelGrid grid, Random rng, ShipParameters p, Envelope env, int len, int detail)
    {
        var count = (int)MathF.Round(p.GreebleDensity * len * 0.5f / detail);

        for (var i = 0; i < count; i++)
        {
            var z0 = rng.Next(2, Math.Max(3, len - 2));
            var lengthZ = rng.Next(2, 5) * detail;
            var hw = env.HalfWidth[Math.Clamp(z0, 0, len - 1)];
            if (hw < 3) continue;

            var x0 = rng.Next(0, hw - 1);
            var widthX = rng.Next(1, Math.Max(2, hw - x0));

            for (var z = z0; z < Math.Min(z0 + lengthZ, len); z++)
                for (var x = x0; x <= Math.Min(x0 + widthX, hw); x++)
                {
                    // Cut `detail` voxels deep so the pocket keeps a visible lip at any resolution.
                    for (var step = 0; step < detail; step++)
                    {
                        var topY = TopFilledY(grid, x, z);
                        if (topY is null || !IsPlating(grid, x, topY.Value, z)) break;

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
}
